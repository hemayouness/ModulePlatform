using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ModulePlatform.Core.Options;
using ModulePlatform.Core.Packaging;

namespace ModulePlatform.Tests;

/// <summary>
/// Validation tests for the platform's trust boundary.
/// Everything accepted here later executes as JavaScript in users' browsers.
/// </summary>
public class ModulePackageReaderTests
{
    private static ModulePackageReader CreateReader(Action<ModulePlatformOptions>? configure = null)
    {
        var opts = new ModulePlatformOptions();
        configure?.Invoke(opts);
        return new ModulePackageReader(Options.Create(opts));
    }

    /* ---------------- happy path ---------------- */

    [Fact]
    public async Task Accepts_a_well_formed_package()
    {
        var result = await CreateReader().ReadAsync(TestPackageBuilder.Valid().Build());

        result.IsValid.Should().BeTrue(
            "a package built by the documented pipeline must install: {0}",
            string.Join("; ", result.Errors.Select(e => e.Message)));

        result.Manifest!.Name.Should().Be("requests");
        result.Manifest.Version.Should().Be("1.0.0");
        result.Entry!.Exposes.Should().Contain(e => e.Key == "./Routes");
        result.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Strips_the_federation_prefix_from_artifact_paths()
    {
        var result = await CreateReader().ReadAsync(TestPackageBuilder.Valid().Build());

        // Artifacts are stored relative to the version root, so the entry lands
        // at /modules/{name}/{version}/remoteEntry.json.
        result.Artifacts.Select(a => a.RelativePath).Should()
            .Contain("remoteEntry.json")
            .And.Contain("chunk-LAZY.js")
            .And.NotContain(p => p.StartsWith("federation/"));
    }

    [Fact]
    public async Task Ignores_files_outside_the_contract_without_failing()
    {
        var zip = TestPackageBuilder.Valid()
            .AddFile("README.md", "# notes for humans")
            .AddFile("docs/design.md", "not part of the contract")
            .Build();

        var result = await CreateReader().ReadAsync(zip);

        result.IsValid.Should().BeTrue();
        result.Artifacts.Should().NotContain(a => a.RelativePath.Contains("README"));
    }

    /* ---------------- malformed archives ---------------- */

    [Fact]
    public async Task Rejects_a_file_that_is_not_a_zip()
    {
        var notAZip = new MemoryStream(Encoding.UTF8.GetBytes("this is definitely not a zip archive"));

        var result = await CreateReader().ReadAsync(notAZip);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("package.not_a_zip");
    }

    [Fact]
    public async Task Rejects_an_empty_upload()
    {
        var result = await CreateReader().ReadAsync(new MemoryStream());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("package.empty");
    }

    [Fact]
    public async Task Rejects_a_package_with_no_manifest()
    {
        var zip = TestPackageBuilder.Valid().Remove("module.json").Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "package.missing_manifest");
    }

    [Fact]
    public async Task Rejects_a_manifest_that_is_not_valid_json()
    {
        var zip = TestPackageBuilder.Valid().WithRawManifest("{ this is not json ").Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "manifest.invalid_json");
    }

    /* ---------------- path traversal ---------------- */

    [Theory]
    [InlineData("../../../evil.js")]
    [InlineData("federation/../../evil.js")]
    [InlineData("federation/sub/../../../evil.js")]
    [InlineData("..\\..\\windows\\system32\\evil.js")]
    [InlineData("/etc/cron.d/evil.js")]
    [InlineData("C:\\inetpub\\wwwroot\\evil.js")]
    [InlineData("//attacker-host/share/evil.js")]
    public async Task Rejects_path_traversal_and_rooted_entries(string hostilePath)
    {
        var zip = TestPackageBuilder.Valid().AddFile(hostilePath, "owned()").Build();

        var result = await CreateReader().ReadAsync(zip);

        result.IsValid.Should().BeFalse($"'{hostilePath}' must never be extracted");
        result.Errors.Should().Contain(e => e.Code == "package.unsafe_entry");
    }

    [Theory]
    [InlineData("../../evil.js", "path traversal")]
    [InlineData("/absolute.js", "absolute")]
    [InlineData("C:/rooted.js", "drive-qualified")]
    [InlineData("//unc/share.js", "UNC")]
    [InlineData("federation/CON.js", "reserved device name")]
    // A segment ending in a space or dot is silently trimmed by Windows, so two
    // different package entries could collide on disk.
    [InlineData("federation/subdir /chunk.js", "space or dot")]
    [InlineData("federation/chunk.js.", "space or dot")]
    public void Path_normalisation_rejects_unsafe_names(string raw, string expectedFragment)
    {
        var ok = ModulePackageReader.TryNormalizeEntryPath(raw, out _, out var error);

        ok.Should().BeFalse($"'{raw}' is not a safe relative path");
        if (!string.IsNullOrEmpty(expectedFragment))
        {
            error.Should().Contain(expectedFragment);
        }
    }

    [Theory]
    [InlineData("federation/remoteEntry.json", "federation/remoteEntry.json")]
    [InlineData("federation\\chunk-A.js", "federation/chunk-A.js")]
    [InlineData("./federation/a/b.js", "federation/a/b.js")]
    public void Path_normalisation_accepts_and_canonicalises_safe_names(string raw, string expected)
    {
        var ok = ModulePackageReader.TryNormalizeEntryPath(raw, out var normalized, out _);

        ok.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    /* ---------------- zip bombs and resource limits ---------------- */

    [Fact]
    public async Task Rejects_a_zip_bomb_by_total_uncompressed_size()
    {
        // 8 MB of zeroes compresses to a few KB.
        var bomb = new byte[8 * 1024 * 1024];
        var zip = TestPackageBuilder.Valid().AddFile("federation/bomb.js", bomb).Build();

        var result = await CreateReader(o =>
        {
            o.MaxTotalUncompressedBytes = 1024 * 1024;
            o.MaxEntryBytes = 32 * 1024 * 1024;
        }).ReadAsync(zip);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "package.zip_bomb");
    }

    [Fact]
    public async Task Rejects_an_entry_with_an_absurd_compression_ratio()
    {
        var highlyCompressible = new byte[4 * 1024 * 1024];
        var zip = TestPackageBuilder.Valid().AddFile("federation/bomb.js", highlyCompressible).Build();

        var result = await CreateReader(o =>
        {
            o.MaxTotalUncompressedBytes = 64 * 1024 * 1024;
            o.MaxEntryBytes = 32 * 1024 * 1024;
            o.MaxCompressionRatio = 50;
        }).ReadAsync(zip);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "package.zip_bomb");
    }

    [Fact]
    public async Task Rejects_an_oversized_single_entry()
    {
        var big = Encoding.UTF8.GetBytes(new string('x', 200_000));
        var zip = TestPackageBuilder.Valid().AddFile("federation/big.js", big).Build();

        var result = await CreateReader(o => o.MaxEntryBytes = 1024).ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "package.entry_too_large");
    }

    [Fact]
    public async Task Rejects_an_archive_with_too_many_entries()
    {
        var builder = TestPackageBuilder.Valid();
        for (var i = 0; i < 50; i++)
        {
            builder.AddFile($"federation/chunk-{i}.js", "x");
        }

        var result = await CreateReader(o => o.MaxEntryCount = 10).ReadAsync(builder.Build());

        result.Errors.Should().Contain(e => e.Code == "package.too_many_entries");
    }

    [Fact]
    public async Task Rejects_an_upload_larger_than_the_package_limit()
    {
        var zip = TestPackageBuilder.Valid().Build();

        var result = await CreateReader(o => o.MaxPackageBytes = 10).ReadAsync(zip);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be("package.too_large");
    }

    /* ---------------- hostile or unexpected content ---------------- */

    [Theory]
    [InlineData("federation/payload.exe")]
    [InlineData("federation/run.sh")]
    [InlineData("federation/deploy.ps1")]
    [InlineData("federation/shell.php")]
    [InlineData("federation/web.config")]
    [InlineData("federation/.htaccess")]
    public async Task Rejects_file_types_that_do_not_belong_in_a_browser_bundle(string path)
    {
        var zip = TestPackageBuilder.Valid().AddFile(path, "payload").Build();

        var result = await CreateReader().ReadAsync(zip);

        result.IsValid.Should().BeFalse($"'{path}' is outside the asset allow-list");
        result.Errors.Should().Contain(e => e.Code == "package.disallowed_file_type");
    }

    [Fact]
    public async Task Rejects_duplicate_entry_names()
    {
        // Two entries with the same name: extractors disagree on which wins,
        // which is a classic way to smuggle content past a scanner.
        var zip = TestPackageBuilder.Valid()
            .AddFile("federation/chunk-DUP.js", "benign()")
            .AddFile("federation/chunk-DUP.js", "hostile()")
            .Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "package.duplicate_entry");
    }

    /* ---------------- manifest validation ---------------- */

    [Theory]
    [InlineData("Requests")]        // uppercase
    [InlineData("1requests")]       // leading digit
    [InlineData("my_module")]       // underscore
    [InlineData("a")]               // too short
    [InlineData("my module")]       // space
    [InlineData("../evil")]         // traversal via the name
    public async Task Rejects_invalid_module_names(string name)
    {
        var zip = TestPackageBuilder.Valid(name: name).Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "manifest.invalid_name");
    }

    [Theory]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("current")]
    public async Task Rejects_reserved_module_names(string name)
    {
        var zip = TestPackageBuilder.Valid(name: name).Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "manifest.reserved_name");
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("latest")]
    [InlineData("01.0.0")]
    public async Task Rejects_non_semver_versions(string version)
    {
        var zip = TestPackageBuilder.Valid(version: version).Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "manifest.invalid_version");
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.1.3")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("1.0.0+build.5")]
    public async Task Accepts_valid_semver_versions(string version)
    {
        var zip = TestPackageBuilder.Valid(version: version).Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().NotContain(e => e.Code == "manifest.invalid_version");
    }

    /* ---------------- federation cross-checks ---------------- */

    [Fact]
    public async Task Rejects_a_name_mismatch_between_manifest_and_federation_entry()
    {
        var zip = TestPackageBuilder.Valid()
            .WithFederationEntry(new
            {
                name = "something-else",
                exposes = new[] { new { key = "./Routes", outFileName = "Routes-ABC123.js" } },
                shared = Array.Empty<object>(),
            })
            .Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "federation.name_mismatch");
    }

    [Fact]
    public async Task Rejects_a_package_that_does_not_expose_the_declared_routes_module()
    {
        var zip = TestPackageBuilder.Valid()
            .WithFederationEntry(new
            {
                name = "requests",
                exposes = new[] { new { key = "./SomethingElse", outFileName = "Other.js" } },
                shared = Array.Empty<object>(),
            })
            .AddFile("federation/Other.js", "export {};")
            .Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "federation.missing_exposed_module");
    }

    [Fact]
    public async Task Rejects_a_package_whose_entry_references_a_missing_chunk()
    {
        // Catches a truncated or badly assembled package before it 404s in the browser.
        var zip = TestPackageBuilder.Valid().Remove("federation/Routes-ABC123.js").Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "federation.missing_chunk");
    }

    [Fact]
    public async Task Rejects_a_package_whose_declared_entry_artifact_is_absent()
    {
        var zip = TestPackageBuilder.Valid().Remove("federation/remoteEntry.json").Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "package.missing_entry_artifact");
    }

    [Fact]
    public async Task Rejects_an_entry_path_that_escapes_the_package()
    {
        var zip = TestPackageBuilder.Valid()
            .WithManifest(new
            {
                name = "requests",
                displayName = "Requests",
                version = "1.0.0",
                entry = "../../../etc/passwd",
                routesExposedModule = "./Routes",
            })
            .Build();

        var result = await CreateReader().ReadAsync(zip);

        result.Errors.Should().Contain(e => e.Code == "manifest.invalid_entry");
    }

    [Fact]
    public async Task Hashes_identical_packages_identically()
    {
        var bytes = TestPackageBuilder.Valid().BuildBytes();

        var a = await CreateReader().ReadAsync(new MemoryStream(bytes));
        var b = await CreateReader().ReadAsync(new MemoryStream(bytes));

        a.ContentHash.Should().Be(b.ContentHash, "the hash is used for integrity reporting");
    }
}
