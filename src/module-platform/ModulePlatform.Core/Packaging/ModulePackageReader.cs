using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModulePlatform.Core.Abstractions;
using ModulePlatform.Core.Options;

namespace ModulePlatform.Core.Packaging;

/// <summary>
/// Reads and validates an uploaded module package.
/// </summary>
/// <remarks>
/// <para>
/// This class is the platform's trust boundary. Everything it accepts will
/// later be executed as JavaScript inside the Shell's origin, in every user's
/// browser. It therefore refuses anything it does not positively recognise
/// rather than trying to enumerate bad input.
/// </para>
/// <para>Specifically it defends against:</para>
/// <list type="bullet">
///   <item>path traversal (<c>../</c>, <c>..\</c>) and absolute or rooted entry names</item>
///   <item>Windows drive-qualified and UNC entry names</item>
///   <item>zip bombs, via per-entry and total uncompressed caps plus a compression-ratio check</item>
///   <item>entry-count exhaustion</item>
///   <item>duplicate entry names, which some extractors resolve inconsistently</item>
///   <item>file types that have no business in a browser bundle (an allow-list, not a deny-list)</item>
///   <item>symlink and non-regular entries, and reserved Windows device names</item>
/// </list>
/// <para>
/// Note that this validates <em>packaging</em>, not <em>intent</em>. It cannot
/// tell benign module code from malicious module code — see the security notes
/// in the README on why module installation must be an authorised, audited,
/// privileged operation.
/// </para>
/// </remarks>
public sealed partial class ModulePackageReader(IOptions<ModulePlatformOptions> options)
    : IModulePackageReader
{
    private readonly ModulePlatformOptions _opt = options.Value;

    /// <summary>Registry names: lowercase, starts with a letter, kebab-case.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex ModuleNameRegex();

    /// <summary>Official SemVer 2.0.0 grammar (semver.org, ABNF-derived).</summary>
    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
        @"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$")]
    private static partial Regex SemVerRegex();

    /// <summary>Reserved Windows device names, which are unsafe as file names on disk storage.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Extensions permitted inside <c>federation/</c>. An allow-list: anything
    /// not named here (.exe, .dll, .sh, .ps1, .php, .jsp, .htaccess, …) is rejected.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".json", ".map",
        ".ico", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".avif",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".txt", ".html", ".webmanifest", ".wasm",
    };

    private const string ManifestEntryName = "module.json";
    private const string FederationPrefix = "federation/";

    public async Task<PackageReadResult> ReadAsync(Stream zipStream, CancellationToken ct = default)
    {
        // Buffer to a seekable stream: we hash the bytes AND read the archive,
        // and we must hash exactly what we validated.
        await using var buffer = new MemoryStream();
        await zipStream.CopyToAsync(buffer, ct);

        if (buffer.Length == 0)
        {
            return PackageReadResult.Fail("package.empty", "The uploaded file is empty.");
        }

        if (buffer.Length > _opt.MaxPackageBytes)
        {
            return PackageReadResult.Fail(
                "package.too_large",
                $"Package is {buffer.Length:N0} bytes; the limit is {_opt.MaxPackageBytes:N0}.");
        }

        buffer.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, ct));

        buffer.Position = 0;
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            return PackageReadResult.Fail("package.not_a_zip", $"Not a readable ZIP archive: {ex.Message}");
        }

        using (archive)
        {
            return await ReadArchiveAsync(archive, hash, ct);
        }
    }

    private async Task<PackageReadResult> ReadArchiveAsync(
        ZipArchive archive, string contentHash, CancellationToken ct)
    {
        var errors = new List<PackageError>();

        if (archive.Entries.Count > _opt.MaxEntryCount)
        {
            return PackageReadResult.Fail(
                "package.too_many_entries",
                $"Package has {archive.Entries.Count} entries; the limit is {_opt.MaxEntryCount}.");
        }

        var artifacts = new List<PackageArtifact>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        byte[]? manifestBytes = null;
        long totalUncompressed = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // Directory markers carry no content.
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            if (!TryNormalizeEntryPath(entry.FullName, out var path, out var pathError))
            {
                errors.Add(new PackageError("package.unsafe_entry",
                    $"Rejected entry '{entry.FullName}': {pathError}"));
                continue;
            }

            if (!seenPaths.Add(path))
            {
                errors.Add(new PackageError("package.duplicate_entry",
                    $"Duplicate entry '{path}'. Extractors disagree on which copy wins, so this is rejected."));
                continue;
            }

            if (entry.Length > _opt.MaxEntryBytes)
            {
                errors.Add(new PackageError("package.entry_too_large",
                    $"Entry '{path}' expands to {entry.Length:N0} bytes; the per-file limit is {_opt.MaxEntryBytes:N0}."));
                continue;
            }

            totalUncompressed += entry.Length;
            if (totalUncompressed > _opt.MaxTotalUncompressedBytes)
            {
                return PackageReadResult.Fail("package.zip_bomb",
                    $"Uncompressed size exceeds {_opt.MaxTotalUncompressedBytes:N0} bytes. Refusing to extract.");
            }

            // Compression-ratio guard: a classic zip bomb is a tiny entry that
            // inflates enormously. Only checked above a floor, because small,
            // highly-compressible files (a 2 KB all-zero .map) are legitimate.
            if (entry.CompressedLength > 0 &&
                entry.Length > _opt.CompressionRatioFloorBytes &&
                entry.Length / entry.CompressedLength > _opt.MaxCompressionRatio)
            {
                errors.Add(new PackageError("package.zip_bomb",
                    $"Entry '{path}' has a compression ratio of " +
                    $"{entry.Length / Math.Max(entry.CompressedLength, 1)}:1, above the limit of {_opt.MaxCompressionRatio}:1."));
                continue;
            }

            if (path.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase))
            {
                manifestBytes = await ReadEntryAsync(entry, ct);
                continue;
            }

            if (!path.StartsWith(FederationPrefix, StringComparison.Ordinal))
            {
                // Anything outside module.json + federation/ is not part of the
                // contract. Ignore quietly so build tools can add READMEs, but
                // never write it to storage.
                continue;
            }

            var ext = Path.GetExtension(path);
            if (!AllowedExtensions.Contains(ext))
            {
                errors.Add(new PackageError("package.disallowed_file_type",
                    $"Entry '{path}' has extension '{ext}', which is not in the allow-list of browser asset types."));
                continue;
            }

            artifacts.Add(new PackageArtifact(
                path[FederationPrefix.Length..],
                await ReadEntryAsync(entry, ct)));
        }

        if (manifestBytes is null)
        {
            errors.Add(new PackageError("package.missing_manifest",
                "Package does not contain a 'module.json' at its root."));
            return WithErrors(errors, contentHash);
        }

        ModulePackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModulePackageManifest>(manifestBytes);
        }
        catch (JsonException ex)
        {
            errors.Add(new PackageError("manifest.invalid_json", $"module.json is not valid JSON: {ex.Message}"));
            return WithErrors(errors, contentHash);
        }

        if (manifest is null)
        {
            errors.Add(new PackageError("manifest.invalid_json", "module.json deserialised to null."));
            return WithErrors(errors, contentHash);
        }

        ValidateManifest(manifest, errors);

        // Cross-check the manifest against the real Native Federation artifact.
        // This is what stops a package that *claims* to be a valid remote but
        // would fail at runtime in the browser — a much worse place to find out.
        FederationEntry? entryDoc = null;
        if (errors.Count == 0)
        {
            entryDoc = ValidateFederationEntry(manifest, artifacts, errors);
        }

        if (errors.Count > 0)
        {
            return WithErrors(errors, contentHash);
        }

        return new PackageReadResult
        {
            Manifest = manifest,
            Entry = entryDoc,
            Artifacts = artifacts,
            ContentHash = contentHash,
            TotalUncompressedBytes = totalUncompressed,
        };
    }

    /// <summary>
    /// Normalises a ZIP entry name to a safe, forward-slash relative path,
    /// or explains why it cannot be trusted.
    /// </summary>
    internal static bool TryNormalizeEntryPath(string raw, out string normalized, out string error)
    {
        normalized = "";
        error = "";

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "the entry name is empty";
            return false;
        }

        // Treat both separators as one: a ZIP produced on Windows may use '\'.
        var path = raw.Replace('\\', '/');

        if (path.Contains('\0'))
        {
            error = "the entry name contains a NUL byte";
            return false;
        }

        // Check UNC before the general absolute-path rule: '//host/share' is
        // also "absolute", but naming it precisely makes the rejection clearer.
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            error = "UNC paths are not allowed";
            return false;
        }

        if (path.StartsWith('/'))
        {
            error = "absolute paths are not allowed";
            return false;
        }

        // C:\... -- rooted on Windows even after normalising separators.
        if (path.Length >= 2 && path[1] == ':')
        {
            error = "drive-qualified paths are not allowed";
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // Reject rather than resolve. A package has no legitimate reason
                // to reference a parent directory, so '..' is always hostile.
                error = "path traversal ('..') is not allowed";
                return false;
            }

            if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(segment)))
            {
                error = $"'{segment}' is a reserved device name";
                return false;
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                error = $"segment '{segment}' ends with a space or dot, which is unsafe on Windows";
                return false;
            }

            if (segment.Any(c => char.IsControl(c)))
            {
                error = $"segment '{segment}' contains control characters";
                return false;
            }

            stack.Add(segment);
        }

        if (stack.Count == 0)
        {
            error = "the entry resolves to an empty path";
            return false;
        }

        normalized = string.Join('/', stack);

        if (normalized.Length > 400)
        {
            error = "the entry path is too long";
            return false;
        }

        return true;
    }

    private void ValidateManifest(ModulePackageManifest m, List<PackageError> errors)
    {
        if (!ModuleNameRegex().IsMatch(m.Name ?? ""))
        {
            errors.Add(new PackageError("manifest.invalid_name",
                $"Module name '{m.Name}' is invalid. Use lowercase kebab-case starting with a letter, 2-64 characters."));
        }
        else if (_opt.ReservedModuleNames.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
        {
            // Names that would shadow a platform route under /modules/.
            errors.Add(new PackageError("manifest.reserved_name",
                $"Module name '{m.Name}' is reserved by the platform."));
        }

        if (!SemVerRegex().IsMatch(m.Version ?? ""))
        {
            errors.Add(new PackageError("manifest.invalid_version",
                $"Version '{m.Version}' is not a valid SemVer 2.0.0 string."));
        }

        if (string.IsNullOrWhiteSpace(m.DisplayName))
        {
            errors.Add(new PackageError("manifest.missing_display_name", "displayName is required."));
        }

        if (string.IsNullOrWhiteSpace(m.Entry))
        {
            errors.Add(new PackageError("manifest.missing_entry", "entry is required."));
        }
        else if (!TryNormalizeEntryPath(m.Entry, out var normalizedEntry, out var entryError)
                 || normalizedEntry != m.Entry)
        {
            errors.Add(new PackageError("manifest.invalid_entry",
                $"entry '{m.Entry}' is not a safe relative path: {entryError}"));
        }

        if (string.IsNullOrWhiteSpace(m.RoutesExposedModule))
        {
            errors.Add(new PackageError("manifest.missing_routes_exposed_module",
                "routesExposedModule is required (for example './Routes')."));
        }
        else if (!m.RoutesExposedModule.StartsWith("./", StringComparison.Ordinal))
        {
            errors.Add(new PackageError("manifest.invalid_routes_exposed_module",
                $"routesExposedModule '{m.RoutesExposedModule}' must start with './' to match a Native Federation exposed key."));
        }
    }

    /// <summary>
    /// Verifies the package really is a Native Federation remote that will work
    /// when served from an arbitrary sub-path.
    /// </summary>
    private FederationEntry? ValidateFederationEntry(
        ModulePackageManifest manifest,
        List<PackageArtifact> artifacts,
        List<PackageError> errors)
    {
        var byPath = artifacts.ToDictionary(a => a.RelativePath, StringComparer.OrdinalIgnoreCase);

        if (!byPath.TryGetValue(manifest.Entry, out var entryArtifact))
        {
            errors.Add(new PackageError("package.missing_entry_artifact",
                $"module.json declares entry '{manifest.Entry}' but 'federation/{manifest.Entry}' is not in the package."));
            return null;
        }

        FederationEntry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<FederationEntry>(entryArtifact.Content);
        }
        catch (JsonException ex)
        {
            errors.Add(new PackageError("federation.invalid_json",
                $"The federation entry '{manifest.Entry}' is not valid JSON: {ex.Message}"));
            return null;
        }

        if (entry is null)
        {
            errors.Add(new PackageError("federation.invalid_json", "The federation entry deserialised to null."));
            return null;
        }

        if (!string.Equals(entry.Name, manifest.Name, StringComparison.Ordinal))
        {
            errors.Add(new PackageError("federation.name_mismatch",
                $"module.json says name '{manifest.Name}' but the federation entry says '{entry.Name}'. " +
                "These must match or the Shell cannot resolve the remote."));
        }

        if (entry.Exposes.Count == 0)
        {
            errors.Add(new PackageError("federation.no_exposes",
                "The federation entry exposes nothing, so the Shell would have nothing to mount."));
        }

        if (!entry.Exposes.Any(e => e.Key == manifest.RoutesExposedModule))
        {
            errors.Add(new PackageError("federation.missing_exposed_module",
                $"module.json declares routesExposedModule '{manifest.RoutesExposedModule}' but the remote exposes " +
                $"[{string.Join(", ", entry.Exposes.Select(e => e.Key))}]."));
        }

        // Every file the entry references must actually be present, otherwise the
        // browser would 404 mid-import and the failure would surface as an opaque
        // module-loading error at runtime.
        foreach (var missing in entry.Exposes
                     .Select(e => e.OutFileName)
                     .Concat(entry.Shared.Select(s => s.OutFileName))
                     .Where(f => !string.IsNullOrWhiteSpace(f))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(f => !byPath.ContainsKey(f)))
        {
            errors.Add(new PackageError("federation.missing_chunk",
                $"The federation entry references '{missing}', which is not in the package."));
        }

        return entry;
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static PackageReadResult WithErrors(List<PackageError> errors, string hash)
    {
        var result = new PackageReadResult { ContentHash = hash };
        result.Errors.AddRange(errors);
        return result;
    }
}
