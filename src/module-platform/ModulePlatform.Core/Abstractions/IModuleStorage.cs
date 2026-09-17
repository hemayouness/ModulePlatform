namespace ModulePlatform.Core.Abstractions;

/// <summary>
/// Content-addressed storage for extracted module artifacts.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately expressed in terms of <em>prefixes</em> and <em>relative
/// paths</em> rather than directories and files, so the same interface maps
/// cleanly onto Azure Blob Storage, S3 or MinIO where "directories" do not
/// exist. Nothing in the module-management services knows about the filesystem.
/// </para>
/// <para>
/// Implementations MUST treat a written prefix as immutable: once
/// <see cref="CommitAsync"/> has been called for <c>requests/1.0.0</c>, those
/// bytes are never rewritten. Re-installing the same version is rejected at the
/// service layer, not silently overwritten.
/// </para>
/// </remarks>
public interface IModuleStorage
{
    /// <summary>Whether any content exists under <paramref name="prefix"/>.</summary>
    Task<bool> ExistsAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Begins an atomic write. Content is staged out of band and only becomes
    /// visible at <paramref name="prefix"/> when <see cref="CommitAsync"/> runs,
    /// so a crashed install can never leave a half-extracted module being served.
    /// </summary>
    Task<IModuleStorageTransaction> BeginWriteAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Opens one artifact for reading, or returns <c>null</c> if it does not exist.
    /// <paramref name="relativePath"/> is always forward-slash separated.
    /// </summary>
    Task<Stream?> OpenReadAsync(string prefix, string relativePath, CancellationToken ct = default);

    /// <summary>Permanently removes everything under <paramref name="prefix"/>.</summary>
    Task DeleteAsync(string prefix, CancellationToken ct = default);

    /// <summary>Relative paths of every artifact under <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);
}

/// <summary>Staged write scope. Dispose without committing to roll back.</summary>
public interface IModuleStorageTransaction : IAsyncDisposable
{
    /// <summary>Writes one artifact into the staging area.</summary>
    Task WriteAsync(string relativePath, Stream content, CancellationToken ct = default);

    /// <summary>Atomically publishes every staged artifact at the target prefix.</summary>
    Task CommitAsync(CancellationToken ct = default);
}
