using System.Text.Json.Serialization;

namespace ModulePlatform.Core.Packaging;

/// <summary>
/// <c>module.json</c> — the package contract authored by the remote's build.
/// Mirrors the TypeScript <c>ModulePackageManifest</c> in <c>mfe-contracts</c>.
/// </summary>
public sealed class ModulePackageManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Entry artifact relative to <c>federation/</c>, e.g. <c>remoteEntry.json</c>.</summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "";

    /// <summary>Exposed key carrying the route tree, e.g. <c>./Routes</c>.</summary>
    [JsonPropertyName("routesExposedModule")]
    public string RoutesExposedModule { get; set; } = "";

    [JsonPropertyName("navigation")]
    public ModuleNavigationHints? Navigation { get; set; }

    [JsonPropertyName("requiredPermissions")]
    public List<string>? RequiredPermissions { get; set; }

    [JsonPropertyName("contractsVersion")]
    public string? ContractsVersion { get; set; }
}

public sealed class ModuleNavigationHints
{
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("order")]
    public int? Order { get; set; }
}

/// <summary>
/// Shape of the Native Federation entry artifact
/// (<c>@softarc/native-federation-runtime</c> 3.x <c>remoteEntry.json</c>).
/// </summary>
public sealed class FederationEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("exposes")]
    public List<FederationExpose> Exposes { get; set; } = [];

    [JsonPropertyName("shared")]
    public List<FederationShared> Shared { get; set; } = [];
}

public sealed class FederationExpose
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("outFileName")]
    public string OutFileName { get; set; } = "";
}

public sealed class FederationShared
{
    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = "";

    [JsonPropertyName("outFileName")]
    public string OutFileName { get; set; } = "";

    [JsonPropertyName("requiredVersion")]
    public string RequiredVersion { get; set; } = "";

    [JsonPropertyName("singleton")]
    public bool Singleton { get; set; }

    [JsonPropertyName("strictVersion")]
    public bool StrictVersion { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
