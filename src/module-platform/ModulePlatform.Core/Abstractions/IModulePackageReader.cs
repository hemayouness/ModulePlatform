using ModulePlatform.Core.Packaging;

namespace ModulePlatform.Core.Abstractions;

/// <summary>Validates and reads an uploaded module package.</summary>
public interface IModulePackageReader
{
    /// <summary>
    /// Inspects the package without extracting anything to durable storage.
    /// Never throws for bad input — invalid packages come back as a failed
    /// <see cref="PackageReadResult"/> so the API can answer 400, not 500.
    /// </summary>
    Task<PackageReadResult> ReadAsync(Stream zipStream, CancellationToken ct = default);
}
