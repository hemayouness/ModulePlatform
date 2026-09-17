namespace ModulePlatform.Core.Options;

/// <summary>
/// Limits and policy for the module platform. Bound from the
/// <c>ModulePlatform</c> configuration section.
/// </summary>
public sealed class ModulePlatformOptions
{
    public const string SectionName = "ModulePlatform";

    /// <summary>Root of the local filesystem store, relative to the content root unless rooted.</summary>
    public string StorageRoot { get; set; } = "storage/modules";

    /// <summary>Largest accepted upload. Also enforced by the request body limit.</summary>
    public long MaxPackageBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Largest single artifact after decompression.</summary>
    public long MaxEntryBytes { get; set; } = 16L * 1024 * 1024;

    /// <summary>Largest total decompressed size. The primary zip-bomb defence.</summary>
    public long MaxTotalUncompressedBytes { get; set; } = 192L * 1024 * 1024;

    /// <summary>Maximum entries in the archive.</summary>
    public int MaxEntryCount { get; set; } = 2_000;

    /// <summary>Highest tolerated per-entry compression ratio.</summary>
    public int MaxCompressionRatio { get; set; } = 200;

    /// <summary>Entries below this size skip the ratio check (small files compress extremely well).</summary>
    public long CompressionRatioFloorBytes { get; set; } = 64 * 1024;

    /// <summary>Names that would collide with platform paths under /modules/.</summary>
    public List<string> ReservedModuleNames { get; set; } =
        ["api", "admin", "shell", "modules", "assets", "static", "health", "current", "latest"];

    /// <summary>
    /// Largest <c>?pageSize=</c> the generic module-data list endpoint accepts.
    /// </summary>
    /// <remarks>
    /// A bigger page is <b>rejected with 400, not silently clamped</b>. Every
    /// paginated UI computes its own <c>totalPages = ceil(totalCount /
    /// pageSize)</c>; if the server quietly returned fewer rows than asked for,
    /// that arithmetic would still use the requested size, so the client would
    /// believe it had shown everything while silently hiding most of the
    /// collection — a data-correctness bug that surfaces no error anywhere. A
    /// 400 costs a developer one round trip and cannot hide data.
    ///
    /// Note this bounds row <i>count</i>, not response <i>bytes</i>: a record's
    /// <c>DataJson</c> is unbounded, so a real size ceiling would need a
    /// separate response-length limit.
    /// </remarks>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Page size applied when a caller supplies <c>?page=</c> without saying how
    /// big a page is. Paging itself stays opt-in — omit both and every matching
    /// row is returned, exactly as before pagination existed.
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// How long browsers may cache versioned artifacts. Safe to make very long
    /// because <c>/modules/{name}/{version}/*</c> is immutable by construction.
    /// </summary>
    public int ArtifactCacheSeconds { get; set; } = 31_536_000; // 1 year
}
