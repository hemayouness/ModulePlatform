using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulePlatform.Core.Abstractions;
using ModulePlatform.Core.Domain;
using ModulePlatform.Core.Packaging;
using ModulePlatform.Core.Services;
using ModulePlatform.Infrastructure.Data;

namespace ModulePlatform.Infrastructure.Services;

/// <summary>
/// Implements the module lifecycle on top of EF Core and <see cref="IModuleStorage"/>.
/// </summary>
/// <remarks>
/// The ordering rule throughout: <em>write artifacts first, commit the database
/// row last</em>. If the process dies between the two, storage holds an orphan
/// directory that nothing references and nothing serves — harmless, and
/// reclaimable. The opposite order would leave a database row pointing at
/// artifacts that do not exist, which the Shell would surface as a broken module.
/// </remarks>
public sealed class ModuleManager(
    ModulePlatformDbContext db,
    IModuleStorage storage,
    IModulePackageReader packageReader,
    ILogger<ModuleManager> log) : IModuleManager
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /* ---------------------------------------------------------------- */
    /* Discovery                                                         */
    /* ---------------------------------------------------------------- */

    public async Task<IReadOnlyList<ModuleDescriptorDto>> GetDiscoverableAsync(CancellationToken ct = default)
    {
        var modules = await db.Modules
            .AsNoTracking()
            .Where(m => m.IsEnabled && m.ActiveVersionId != null)
            .Include(m => m.ActiveVersion)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        return modules
            .Where(m => m.ActiveVersion is not null)
            .Select(m => ToDescriptor(m, m.ActiveVersion!))
            .ToList();
    }

    public async Task<ModuleOperationResult<ModuleDescriptorDto>> GetDiscoverableByNameAsync(
        string name, CancellationToken ct = default)
    {
        var module = await db.Modules
            .AsNoTracking()
            .Include(m => m.ActiveVersion)
            .FirstOrDefaultAsync(m => m.Name == name, ct);

        if (module is null)
        {
            return ModuleOperationResult<ModuleDescriptorDto>.NotFound($"No module named '{name}'.");
        }

        if (!module.IsEnabled)
        {
            // 404, not 403: whether a disabled module exists is not something an
            // ordinary Shell user needs to learn.
            return ModuleOperationResult<ModuleDescriptorDto>.NotFound(
                $"Module '{name}' is not available.");
        }

        if (module.ActiveVersion is null)
        {
            return ModuleOperationResult<ModuleDescriptorDto>.NotFound(
                $"Module '{name}' has no active version.");
        }

        return ModuleOperationResult<ModuleDescriptorDto>.Ok(ToDescriptor(module, module.ActiveVersion));
    }

    /* ---------------------------------------------------------------- */
    /* Administration                                                    */
    /* ---------------------------------------------------------------- */

    public async Task<IReadOnlyList<AdminModuleDto>> GetAllForAdminAsync(CancellationToken ct = default)
    {
        var modules = await db.Modules
            .AsNoTracking()
            .Include(m => m.Versions)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        return modules.Select(ToAdminDto).ToList();
    }

    public async Task<ModuleOperationResult<AdminModuleDto>> GetForAdminAsync(
        string name, CancellationToken ct = default)
    {
        var module = await db.Modules
            .AsNoTracking()
            .Include(m => m.Versions)
            .FirstOrDefaultAsync(m => m.Name == name, ct);

        return module is null
            ? ModuleOperationResult<AdminModuleDto>.NotFound($"No module named '{name}'.")
            : ModuleOperationResult<AdminModuleDto>.Ok(ToAdminDto(module));
    }

    public async Task<ModuleOperationResult<IReadOnlyList<AdminModuleVersionDto>>> GetVersionsAsync(
        string name, CancellationToken ct = default)
    {
        var module = await db.Modules
            .AsNoTracking()
            .Include(m => m.Versions)
            .FirstOrDefaultAsync(m => m.Name == name, ct);

        if (module is null)
        {
            return ModuleOperationResult<IReadOnlyList<AdminModuleVersionDto>>
                .NotFound($"No module named '{name}'.");
        }

        return ModuleOperationResult<IReadOnlyList<AdminModuleVersionDto>>.Ok(
            SortVersions(module.Versions).Select(ToVersionDto).ToList());
    }

    /* ---------------------------------------------------------------- */
    /* Install                                                           */
    /* ---------------------------------------------------------------- */

    public async Task<ModuleOperationResult<AdminModuleDto>> InstallAsync(
        Stream packageStream, string? installedBy, CancellationToken ct = default)
    {
        var read = await packageReader.ReadAsync(packageStream, ct);

        if (!read.IsValid)
        {
            log.LogWarning("Rejected module package: {Errors}",
                string.Join("; ", read.Errors.Select(e => e.Code)));

            return ModuleOperationResult<AdminModuleDto>.Invalid(
                "The module package failed validation.",
                read.Errors.Select(e => new ModuleOperationError(e.Code, e.Message)));
        }

        var manifest = read.Manifest!;
        var module = await db.Modules
            .Include(m => m.Versions)
            .FirstOrDefaultAsync(m => m.Name == manifest.Name, ct);

        if (module?.Versions.Any(v => v.Version == manifest.Version) == true)
        {
            // Versioned artifact URLs are immutable and may already be cached in
            // browsers and CDNs. Publish a new version instead of redefining one.
            return ModuleOperationResult<AdminModuleDto>.Conflict(
                "version_exists",
                $"Version {manifest.Version} of '{manifest.Name}' is already installed. " +
                "Installed versions are immutable — publish a new version instead.");
        }

        var prefix = $"{manifest.Name}/{manifest.Version}";

        if (await storage.ExistsAsync(prefix, ct))
        {
            // Storage and database disagree. Refuse rather than overwrite.
            return ModuleOperationResult<AdminModuleDto>.Conflict(
                "artifacts_exist",
                $"Artifacts already exist at '{prefix}' without a matching registry row. " +
                "This needs operator attention; the platform will not overwrite them.");
        }

        // --- 1. Artifacts first ------------------------------------------------
        await using (var tx = await storage.BeginWriteAsync(prefix, ct))
        {
            foreach (var artifact in read.Artifacts)
            {
                using var content = new MemoryStream(artifact.Content, writable: false);
                await tx.WriteAsync(artifact.RelativePath, content, ct);
            }

            await tx.CommitAsync(ct);
        }

        // --- 2. Registry row second -------------------------------------------
        try
        {
            var (major, minor, patch) = ParseSemVerSort(manifest.Version);

            module ??= NewModule(manifest);

            // Later packages may legitimately rename the module for display.
            module.DisplayName = manifest.DisplayName;
            module.Description = manifest.Description;
            module.UpdatedAt = DateTimeOffset.UtcNow;

            var version = new ModuleVersion
            {
                ModuleId = module.Id,
                Version = manifest.Version,
                SortMajor = major,
                SortMinor = minor,
                SortPatch = patch,
                EntryPath = manifest.Entry,
                RoutesExposedModule = manifest.RoutesExposedModule,
                StoragePrefix = prefix,
                ContentHash = read.ContentHash,
                SizeBytes = read.TotalUncompressedBytes,
                FileCount = read.Artifacts.Count,
                DisplayName = manifest.DisplayName,
                Description = manifest.Description,
                ContractsVersion = manifest.ContractsVersion,
                MetadataJson = JsonSerializer.Serialize(
                    new ModuleMetadata(manifest.Navigation, manifest.RequiredPermissions ?? []),
                    JsonOpts),
                // Installing never activates. A new version lands dark so it can
                // be verified before any user is exposed to it.
                IsActive = false,
                InstalledBy = installedBy,
            };

            module.Versions.Add(version);

            if (db.Entry(module).State == EntityState.Detached)
            {
                // New module: adding the root cascades Added through the graph.
                db.Modules.Add(module);
            }
            else
            {
                // Existing module. The version's key is client-generated
                // (Guid.CreateVersion7), so EF's "is this new?" heuristic would
                // see a non-default key on an entity reached through a
                // navigation and issue an UPDATE against a row that does not
                // exist. Mark it Added explicitly.
                db.ModuleVersions.Add(version);
            }

            await db.SaveChangesAsync(ct);

            log.LogInformation("Installed {Module} {Version} ({Files} files, {Bytes} bytes) by {User}",
                manifest.Name, manifest.Version, read.Artifacts.Count, read.TotalUncompressedBytes,
                installedBy ?? "unknown");

            return await ReloadAdminAsync(manifest.Name, ct);
        }
        catch
        {
            // Roll the artifacts back so a failed install leaves nothing behind.
            await SafeDeleteAsync(prefix, ct);
            throw;
        }
    }

    /* ---------------------------------------------------------------- */
    /* Activation                                                        */
    /* ---------------------------------------------------------------- */

    public async Task<ModuleOperationResult<AdminModuleDto>> ActivateAsync(
        string name, string version, CancellationToken ct = default)
    {
        /*
         * Activation and rollback are the same operation. Nothing is deleted, so
         * moving back to an older version is as cheap as moving forward.
         *
         * Two subtleties are handled here:
         *
         * 1. The switch must be TWO saves inside one transaction. The unique
         *    filtered index "one active version per module" is checked per
         *    statement, and EF gives no ordering guarantee between clearing the
         *    old flag and setting the new one -- a single SaveChanges can
         *    transiently leave two rows active and violate the constraint.
         *    Clearing first, then setting, is always legal.
         *
         * 2. The connection uses EnableRetryOnFailure, whose execution strategy
         *    refuses user-initiated transactions unless the whole unit runs
         *    inside it. The read is therefore inside the delegate too, so a
         *    retry re-reads state instead of replaying stale tracked entities.
         */
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async cancellation =>
        {
            // Retries replay this delegate, so start from a clean tracker.
            db.ChangeTracker.Clear();

            var module = await db.Modules
                .Include(m => m.Versions)
                .FirstOrDefaultAsync(m => m.Name == name, cancellation);

            if (module is null)
            {
                return ModuleOperationResult<AdminModuleDto>.NotFound($"No module named '{name}'.");
            }

            var target = module.Versions.FirstOrDefault(v => v.Version == version);
            if (target is null)
            {
                return ModuleOperationResult<AdminModuleDto>.NotFound(
                    $"Version {version} of '{name}' is not installed.");
            }

            if (!await storage.ExistsAsync(target.StoragePrefix, cancellation))
            {
                // Never point users at a version whose bytes are gone.
                return ModuleOperationResult<AdminModuleDto>.Conflict(
                    "artifacts_missing",
                    $"Artifacts for {name} {version} are missing from storage. " +
                    "Re-install the package before activating.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);

            foreach (var v in module.Versions)
            {
                v.IsActive = false;
            }

            module.ActiveVersionId = null;
            await db.SaveChangesAsync(cancellation);

            target.IsActive = true;
            module.ActiveVersionId = target.Id;
            module.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellation);

            await transaction.CommitAsync(cancellation);
            log.LogInformation("Activated {Module} {Version}", name, version);

            return await ReloadAdminAsync(name, cancellation);
        }, ct);
    }

    public async Task<ModuleOperationResult<AdminModuleDto>> DeactivateAsync(
        string name, string version, CancellationToken ct = default)
    {
        var module = await db.Modules
            .Include(m => m.Versions)
            .FirstOrDefaultAsync(m => m.Name == name, ct);

        if (module is null)
        {
            return ModuleOperationResult<AdminModuleDto>.NotFound($"No module named '{name}'.");
        }

        var target = module.Versions.FirstOrDefault(v => v.Version == version);
        if (target is null)
        {
            return ModuleOperationResult<AdminModuleDto>.NotFound(
                $"Version {version} of '{name}' is not installed.");
        }

        if (!target.IsActive)
        {
            return ModuleOperationResult<AdminModuleDto>.Conflict(
                "not_active", $"Version {version} of '{name}' is not the active version.");
        }

        // Clear the FK before the flag, so the restricted FK never dangles.
        module.ActiveVersionId = null;
        target.IsActive = false;
        module.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        log.LogInformation("Deactivated {Module} {Version}", name, version);

        return await ReloadAdminAsync(name, ct);
    }

    public async Task<ModuleOperationResult<AdminModuleDto>> SetEnabledAsync(
        string name, bool enabled, CancellationToken ct = default)
    {
        var module = await db.Modules
            .Include(m => m.Versions)
            .FirstOrDefaultAsync(m => m.Name == name, ct);

        if (module is null)
        {
            return ModuleOperationResult<AdminModuleDto>.NotFound($"No module named '{name}'.");
        }

        module.IsEnabled = enabled;
        module.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("{Action} module {Module}", enabled ? "Enabled" : "Disabled", name);
        return await ReloadAdminAsync(name, ct);
    }

    /* ---------------------------------------------------------------- */
    /* Uninstall                                                         */
    /* ---------------------------------------------------------------- */

    public async Task<ModuleOperationResult<AdminModuleDto>> UninstallVersionAsync(
        string name, string version, CancellationToken ct = default)
    {
        var module = await db.Modules
            .Include(m => m.Versions)
            .FirstOrDefaultAsync(m => m.Name == name, ct);

        if (module is null)
        {
            return ModuleOperationResult<AdminModuleDto>.NotFound($"No module named '{name}'.");
        }

        var target = module.Versions.FirstOrDefault(v => v.Version == version);
        if (target is null)
        {
            return ModuleOperationResult<AdminModuleDto>.NotFound(
                $"Version {version} of '{name}' is not installed.");
        }

        if (target.IsActive || module.ActiveVersionId == target.Id)
        {
            // Deleting what users are currently running would break the Shell for
            // everyone mid-session. Deactivate or activate another version first.
            return ModuleOperationResult<AdminModuleDto>.Conflict(
                "version_active",
                $"Version {version} of '{name}' is currently active. " +
                "Activate another version or deactivate it before uninstalling.");
        }

        // Row first here: if artifact deletion fails we are left with unreferenced
        // files (harmless) rather than a row pointing at nothing (broken).
        module.Versions.Remove(target);
        db.ModuleVersions.Remove(target);
        module.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await SafeDeleteAsync(target.StoragePrefix, ct);
        log.LogInformation("Uninstalled {Module} {Version}", name, version);

        return await ReloadAdminAsync(name, ct);
    }

    /* ---------------------------------------------------------------- */
    /* Helpers                                                           */
    /* ---------------------------------------------------------------- */

    private static Module NewModule(ModulePackageManifest manifest) => new()
    {
        Name = manifest.Name,
        DisplayName = manifest.DisplayName,
        Description = manifest.Description,
        // A brand-new module is enabled but has no active version, so it is
        // registered and inspectable without yet being served to anyone.
        IsEnabled = true,
    };

    private async Task<ModuleOperationResult<AdminModuleDto>> ReloadAdminAsync(
        string name, CancellationToken ct)
    {
        var fresh = await db.Modules
            .AsNoTracking()
            .Include(m => m.Versions)
            .FirstAsync(m => m.Name == name, ct);

        return ModuleOperationResult<AdminModuleDto>.Ok(ToAdminDto(fresh));
    }

    private async Task SafeDeleteAsync(string prefix, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(prefix, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to delete module artifacts at {Prefix}; they are now orphaned", prefix);
        }
    }

    private static ModuleDescriptorDto ToDescriptor(Module m, ModuleVersion v)
    {
        var meta = DeserializeMetadata(v.MetadataJson);

        return new ModuleDescriptorDto(
            m.Name,
            v.DisplayName ?? m.DisplayName,
            v.Description ?? m.Description,
            v.Version,
            // Same-origin absolute path. Native Federation derives the remote's
            // base URL from this entry's directory, so every chunk, shared
            // dependency and asset resolves under the version folder.
            $"/modules/{m.Name}/{v.Version}/{v.EntryPath}",
            v.RoutesExposedModule,
            meta.Navigation is null ? null : new NavigationDto(meta.Navigation.Icon, meta.Navigation.Order),
            meta.RequiredPermissions);
    }

    private static AdminModuleDto ToAdminDto(Module m) => new(
        m.Name,
        m.DisplayName,
        m.Description,
        m.IsEnabled,
        m.Versions.FirstOrDefault(v => v.IsActive)?.Version,
        SortVersions(m.Versions).Select(ToVersionDto).ToList());

    private static AdminModuleVersionDto ToVersionDto(ModuleVersion v) => new(
        v.Version, v.IsActive, v.InstalledAt, v.SizeBytes, v.FileCount,
        v.EntryPath, v.ContentHash, v.ContractsVersion);

    /// <summary>Newest first, by numeric SemVer components rather than string order.</summary>
    private static IEnumerable<ModuleVersion> SortVersions(IEnumerable<ModuleVersion> versions) =>
        versions
            .OrderByDescending(v => v.SortMajor)
            .ThenByDescending(v => v.SortMinor)
            .ThenByDescending(v => v.SortPatch)
            .ThenByDescending(v => v.InstalledAt);

    /// <summary>
    /// Extracts numeric SemVer components for ordering. The string has already
    /// been validated against the SemVer grammar by the package reader.
    /// </summary>
    internal static (long Major, long Minor, long Patch) ParseSemVerSort(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');

        return (
            parts.Length > 0 && long.TryParse(parts[0], out var ma) ? ma : 0,
            parts.Length > 1 && long.TryParse(parts[1], out var mi) ? mi : 0,
            parts.Length > 2 && long.TryParse(parts[2], out var pa) ? pa : 0);
    }

    private static ModuleMetadata DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ModuleMetadata(null, []);
        }

        try
        {
            return JsonSerializer.Deserialize<ModuleMetadata>(json, JsonOpts)
                   ?? new ModuleMetadata(null, []);
        }
        catch (JsonException)
        {
            // Bad stored metadata must not break discovery for every module.
            return new ModuleMetadata(null, []);
        }
    }

    private sealed record ModuleMetadata(
        ModuleNavigationHints? Navigation,
        IReadOnlyList<string> RequiredPermissions);
}
