namespace ModulePlatform.Core.Packaging;

/// <summary>One validation problem, phrased for the uploading developer.</summary>
public sealed record PackageError(string Code, string Message);

/// <summary>An artifact accepted from the package, ready to be written to storage.</summary>
public sealed record PackageArtifact(string RelativePath, byte[] Content);

/// <summary>Outcome of reading and validating an uploaded package.</summary>
public sealed class PackageReadResult
{
    public bool IsValid => Errors.Count == 0;
    public List<PackageError> Errors { get; } = [];

    public ModulePackageManifest? Manifest { get; init; }
    public FederationEntry? Entry { get; init; }

    /// <summary>Artifacts to publish, relative to the version root.</summary>
    public IReadOnlyList<PackageArtifact> Artifacts { get; init; } = [];

    /// <summary>SHA-256 of the uploaded ZIP, lowercase hex.</summary>
    public string ContentHash { get; init; } = "";

    public long TotalUncompressedBytes { get; init; }

    public static PackageReadResult Fail(string code, string message)
    {
        var r = new PackageReadResult();
        r.Errors.Add(new PackageError(code, message));
        return r;
    }
}
