using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModulePlatform.Core.Abstractions;
using ModulePlatform.Core.Options;

namespace ModulePlatform.Infrastructure.Storage;

/// <summary>
/// Local-filesystem <see cref="IModuleStorage"/>, adequate for a single-node
/// deployment and for this proof of concept.
/// </summary>
/// <remarks>
/// <para>
/// Writes are staged into a sibling <c>.staging/{guid}</c> directory and moved
/// into place with a single directory rename on commit. A crash therefore
/// leaves a discardable staging directory, never a half-written version folder
/// that the static-file middleware would happily start serving.
/// </para>
/// <para>
/// To move to Azure Blob Storage / S3 / MinIO, implement this interface against
/// the object store (staging becomes an upload prefix, commit becomes a copy or
/// a manifest flip) and change one DI registration. No module-management logic
/// changes, because nothing above this class knows what a directory is.
/// </para>
/// </remarks>
public sealed class FileSystemModuleStorage : IModuleStorage
{
    private readonly string _root;
    private readonly string _stagingRoot;
    private readonly ILogger<FileSystemModuleStorage> _log;

    public FileSystemModuleStorage(
        IOptions<ModulePlatformOptions> options,
        string contentRoot,
        ILogger<FileSystemModuleStorage> log)
    {
        var configured = options.Value.StorageRoot;
        _root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRoot, configured));

        // Staging lives OUTSIDE the served root, so a half-written module can
        // never be reached over HTTP even momentarily, and so it cannot depend
        // on the static-file middleware's hidden-file rules for its safety.
        _stagingRoot = Path.GetFullPath(Path.Combine(_root, "..", ".module-staging"));

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_stagingRoot);
        _log = log;
    }

    /// <summary>Absolute path of a version root, used by the static-file middleware.</summary>
    public string GetPhysicalPath(string prefix) => ResolvePrefix(prefix);

    public string RootPath => _root;

    public Task<bool> ExistsAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult(Directory.Exists(ResolvePrefix(prefix)));

    public Task<IModuleStorageTransaction> BeginWriteAsync(string prefix, CancellationToken ct = default)
    {
        var target = ResolvePrefix(prefix);
        var staging = Path.Combine(_stagingRoot, Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(staging);

        return Task.FromResult<IModuleStorageTransaction>(
            new FileSystemTransaction(staging, target, _log));
    }

    public Task<Stream?> OpenReadAsync(string prefix, string relativePath, CancellationToken ct = default)
    {
        var full = ResolveWithin(ResolvePrefix(prefix), relativePath);
        if (full is null || !File.Exists(full))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(
            new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));
    }

    public Task DeleteAsync(string prefix, CancellationToken ct = default)
    {
        var dir = ResolvePrefix(prefix);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            _log.LogInformation("Deleted module artifacts at {Prefix}", prefix);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var dir = ResolvePrefix(prefix);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var files = Directory
            .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    /// <summary>
    /// Maps a storage prefix onto an absolute path, refusing anything that would
    /// escape the storage root. Belt and braces: package entries are already
    /// validated, but this class must be safe on its own terms.
    /// </summary>
    private string ResolvePrefix(string prefix)
    {
        var resolved = ResolveWithin(_root, prefix)
            ?? throw new ArgumentException($"Prefix '{prefix}' escapes the storage root.", nameof(prefix));
        return resolved;
    }

    /// <summary>Joins and canonicalises, returning null if the result leaves <paramref name="baseDir"/>.</summary>
    private static string? ResolveWithin(string baseDir, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var combined = Path.GetFullPath(
            Path.Combine(baseDir, relative.Replace('/', Path.DirectorySeparatorChar)));

        var baseFull = Path.GetFullPath(baseDir);
        var withBoundary = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;

        // Compare the canonical paths, so '..', symlink-ish inputs and casing
        // tricks cannot slip past a naive StartsWith on the raw string.
        return combined.StartsWith(withBoundary, StringComparison.OrdinalIgnoreCase)
            ? combined
            : null;
    }

    private sealed class FileSystemTransaction(string staging, string target, ILogger log)
        : IModuleStorageTransaction
    {
        private bool _committed;

        public async Task WriteAsync(string relativePath, Stream content, CancellationToken ct = default)
        {
            var full = ResolveWithin(staging, relativePath)
                ?? throw new ArgumentException($"'{relativePath}' escapes the staging directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            await using var file = new FileStream(
                full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await content.CopyToAsync(file, ct);
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            if (_committed)
            {
                throw new InvalidOperationException("This storage transaction has already been committed.");
            }

            if (Directory.Exists(target))
            {
                // Versioned prefixes are immutable. Reaching here means a caller
                // tried to overwrite a published version -- refuse rather than
                // silently invalidate what browsers have cached.
                throw new IOException(
                    $"Refusing to overwrite existing immutable artifacts at '{target}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // Single atomic rename: the version directory appears complete or not at all.
            Directory.Move(staging, target);
            _committed = true;
            log.LogInformation("Committed module artifacts to {Target}", target);

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed && Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception ex)
                {
                    // Never mask the original failure with a cleanup failure.
                    log.LogWarning(ex, "Could not clean up staging directory {Staging}", staging);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
