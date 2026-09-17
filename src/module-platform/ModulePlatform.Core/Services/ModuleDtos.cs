namespace ModulePlatform.Core.Services;

/// <summary>What the Shell receives from <c>GET /api/modules</c>.</summary>
public sealed record ModuleDescriptorDto(
    string Name,
    string DisplayName,
    string? Description,
    string Version,
    string EntryUrl,
    string RoutesExposedModule,
    NavigationDto? Navigation,
    IReadOnlyList<string> RequiredPermissions);

public sealed record NavigationDto(string? Icon, int? Order);

/// <summary>What the admin UI receives, including inactive versions.</summary>
public sealed record AdminModuleDto(
    string Name,
    string DisplayName,
    string? Description,
    bool IsEnabled,
    string? ActiveVersion,
    IReadOnlyList<AdminModuleVersionDto> Versions);

public sealed record AdminModuleVersionDto(
    string Version,
    bool IsActive,
    DateTimeOffset InstalledAt,
    long SizeBytes,
    int FileCount,
    string EntryPath,
    string ContentHash,
    string? ContractsVersion);
