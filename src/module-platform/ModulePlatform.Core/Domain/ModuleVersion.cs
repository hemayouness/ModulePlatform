using System.ComponentModel.DataAnnotations;

namespace ModulePlatform.Core.Domain;

/// <summary>
/// One immutable installed release of a <see cref="Module"/>.
/// </summary>
/// <remarks>
/// Rows here are append-only in practice: once written, the artifacts behind
/// <see cref="StoragePrefix"/> are never rewritten. That is what lets the
/// platform serve <c>/modules/{name}/{version}/*</c> with a long
/// <c>immutable</c> cache lifetime.
/// </remarks>
public class ModuleVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;

    /// <summary>Strict SemVer 2.0.0 string, e.g. <c>1.1.0</c> or <c>2.0.0-beta.1</c>.</summary>
    [MaxLength(64)]
    public required string Version { get; set; }

    /// <summary>
    /// Normalised sort key so SQL Server can order versions correctly
    /// (plain string ordering puts "1.10.0" before "1.9.0").
    /// </summary>
    public long SortMajor { get; set; }
    public long SortMinor { get; set; }
    public long SortPatch { get; set; }

    /// <summary>
    /// Entry artifact relative to the version root, e.g. <c>remoteEntry.json</c>.
    /// Stored rather than assumed, so a future Native Federation release that
    /// renames the entry does not require a schema change.
    /// </summary>
    [MaxLength(256)]
    public required string EntryPath { get; set; }

    /// <summary>The exposed key holding the module's route tree, e.g. <c>./Routes</c>.</summary>
    [MaxLength(128)]
    public required string RoutesExposedModule { get; set; }

    /// <summary>
    /// Storage-relative prefix of the extracted artifacts,
    /// e.g. <c>requests/1.1.0</c>. Resolved by <c>IModuleStorage</c>, so the
    /// row stays valid if storage moves from disk to blob storage.
    /// </summary>
    [MaxLength(256)]
    public required string StoragePrefix { get; set; }

    /// <summary>SHA-256 of the uploaded ZIP — content integrity and duplicate detection.</summary>
    [MaxLength(64)]
    public required string ContentHash { get; set; }

    public long SizeBytes { get; set; }
    public int FileCount { get; set; }

    [MaxLength(128)]
    public string? DisplayName { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>Serialised nav hints and required permissions from module.json.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Version of the shared mfe-contracts package this build targeted.</summary>
    [MaxLength(64)]
    public string? ContractsVersion { get; set; }

    /// <summary>
    /// Denormalised active flag. Kept in sync with <see cref="Module.ActiveVersionId"/>
    /// inside one transaction; it exists so discovery can filter without a self-join.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(128)]
    public string? InstalledBy { get; set; }
}
