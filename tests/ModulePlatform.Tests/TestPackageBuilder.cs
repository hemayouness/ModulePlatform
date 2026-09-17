using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ModulePlatform.Tests;

/// <summary>
/// Builds module ZIPs in memory, including deliberately malformed ones, so the
/// validation tests exercise real archives rather than mocks.
/// </summary>
internal sealed class TestPackageBuilder
{
    private readonly List<(string Path, byte[] Content, CompressionLevel Level)> _entries = [];

    public static TestPackageBuilder Valid(
        string name = "requests",
        string version = "1.0.0",
        string routesKey = "./Routes")
    {
        var builder = new TestPackageBuilder();

        builder.WithManifest(new
        {
            name,
            displayName = "Requests",
            version,
            description = "Requests management module",
            entry = "remoteEntry.json",
            routesExposedModule = routesKey,
            navigation = new { icon = "📝", order = 10 },
            requiredPermissions = new[] { "requests.read" },
            contractsVersion = "1.0.0",
        });

        builder.WithFederationEntry(new
        {
            name,
            exposes = new[]
            {
                new { key = routesKey, outFileName = "Routes-ABC123.js" },
                new { key = "./Module", outFileName = "Module-DEF456.js" },
            },
            shared = new[]
            {
                new
                {
                    packageName = "@angular/core",
                    outFileName = "_angular_core.XYZ.js",
                    requiredVersion = "^21.2.0",
                    singleton = true,
                    strictVersion = true,
                    version = "21.2.23",
                },
            },
        });

        builder.AddFile("federation/Routes-ABC123.js", "export const routes = [];");
        builder.AddFile("federation/Module-DEF456.js", "export const moduleMeta = {};");
        builder.AddFile("federation/_angular_core.XYZ.js", "export default {};");
        builder.AddFile("federation/chunk-LAZY.js", "export const x = 1;");
        builder.AddFile("federation/styles-A1.css", "body{margin:0}");

        return builder;
    }

    public TestPackageBuilder WithManifest(object manifest) =>
        Replace("module.json", JsonSerializer.Serialize(manifest));

    public TestPackageBuilder WithRawManifest(string json) => Replace("module.json", json);

    public TestPackageBuilder WithFederationEntry(object entry) =>
        Replace("federation/remoteEntry.json", JsonSerializer.Serialize(entry));

    public TestPackageBuilder WithRawFederationEntry(string json) =>
        Replace("federation/remoteEntry.json", json);

    public TestPackageBuilder AddFile(string path, string content) =>
        AddFile(path, Encoding.UTF8.GetBytes(content));

    public TestPackageBuilder AddFile(string path, byte[] content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        _entries.Add((path, content, level));
        return this;
    }

    public TestPackageBuilder Remove(string path)
    {
        _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return this;
    }

    private TestPackageBuilder Replace(string path, string content)
    {
        Remove(path);
        return AddFile(path, content);
    }

    /// <summary>
    /// Writes the archive. Entry names are written verbatim, so hostile names
    /// such as <c>../../evil.js</c> survive into the real ZIP.
    /// </summary>
    public MemoryStream Build()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content, level) in _entries)
            {
                var entry = zip.CreateEntry(path, level);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    public byte[] BuildBytes() => Build().ToArray();
}
