using System.ComponentModel.DataAnnotations;

namespace ModulePlatform.Core.Domain;

/// <summary>
/// A federated micro frontend known to the platform, independent of any one version.
/// </summary>
/// <remarks>
/// Design note: the Module row carries only identity and the operational on/off
/// switch. Everything that can differ between releases (display name, entry
/// path, size, hash) lives on <see cref="ModuleVersion"/>, so installing a new
/// version never mutates data that older versions still depend on.
/// </remarks>
public class Module
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Registry name, lowercase kebab-case. Unique across the platform.</summary>
    [MaxLength(64)]
    public required string Name { get; set; }

    [MaxLength(128)]
    public required string DisplayName { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>
    /// Operator kill switch. Disabling hides the module from discovery without
    /// deleting artifacts, so it can be re-enabled instantly.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The version currently served to browsers. Nullable because a module can
    /// exist with every version deactivated (a deliberate "installed but dark"
    /// state used during staged rollouts).
    /// </summary>
    public Guid? ActiveVersionId { get; set; }
    public ModuleVersion? ActiveVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ModuleVersion> Versions { get; set; } = [];
}
