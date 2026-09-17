namespace ModulePlatform.Core.Services;

/// <summary>
/// The platform's module lifecycle: install, activate, deactivate, uninstall,
/// enable, disable, and discovery.
/// </summary>
/// <remarks>
/// Every method returns a <see cref="ModuleOperationResult{T}"/> rather than
/// throwing, so the HTTP layer stays a thin mapping and invariants are enforced
/// in exactly one place.
/// </remarks>
public interface IModuleManager
{
    /// <summary>
    /// Modules the Shell should offer right now: enabled, with an active
    /// version. This is the only method the Shell's discovery path uses.
    /// </summary>
    Task<IReadOnlyList<ModuleDescriptorDto>> GetDiscoverableAsync(CancellationToken ct = default);

    Task<ModuleOperationResult<ModuleDescriptorDto>> GetDiscoverableByNameAsync(
        string name, CancellationToken ct = default);

    /// <summary>Full administrative view, including disabled modules and inactive versions.</summary>
    Task<IReadOnlyList<AdminModuleDto>> GetAllForAdminAsync(CancellationToken ct = default);

    Task<ModuleOperationResult<AdminModuleDto>> GetForAdminAsync(
        string name, CancellationToken ct = default);

    Task<ModuleOperationResult<IReadOnlyList<AdminModuleVersionDto>>> GetVersionsAsync(
        string name, CancellationToken ct = default);

    /// <summary>
    /// Validates and installs a package. Installing never activates: a new
    /// version lands dark so it can be smoke-tested before it reaches users.
    /// </summary>
    Task<ModuleOperationResult<AdminModuleDto>> InstallAsync(
        Stream packageStream, string? installedBy, CancellationToken ct = default);

    /// <summary>Makes one installed version the one served to browsers.</summary>
    Task<ModuleOperationResult<AdminModuleDto>> ActivateAsync(
        string name, string version, CancellationToken ct = default);

    /// <summary>Clears the active version, leaving the module installed but dark.</summary>
    Task<ModuleOperationResult<AdminModuleDto>> DeactivateAsync(
        string name, string version, CancellationToken ct = default);

    Task<ModuleOperationResult<AdminModuleDto>> SetEnabledAsync(
        string name, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes one version and its artifacts.
    /// Refuses to remove the active version.
    /// </summary>
    Task<ModuleOperationResult<AdminModuleDto>> UninstallVersionAsync(
        string name, string version, CancellationToken ct = default);
}
