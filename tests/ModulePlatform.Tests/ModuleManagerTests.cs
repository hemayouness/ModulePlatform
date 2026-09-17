using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModulePlatform.Core.Abstractions;
using ModulePlatform.Core.Options;
using ModulePlatform.Core.Packaging;
using ModulePlatform.Core.Services;
using ModulePlatform.Infrastructure.Data;
using ModulePlatform.Infrastructure.Services;
using ModulePlatform.Infrastructure.Storage;

namespace ModulePlatform.Tests;

/// <summary>
/// Lifecycle tests against a real relational database (SQLite in-memory) and
/// the real filesystem storage, so the transactional and immutability
/// invariants are actually exercised rather than mocked away.
/// </summary>
public sealed class ModuleManagerTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ModulePlatformDbContext _db = null!;
    private FileSystemModuleStorage _storage = null!;
    private ModuleManager _manager = null!;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ModulePlatformDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ModulePlatformDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _storageRoot = Path.Combine(Path.GetTempPath(), "mfe-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_storageRoot);

        var platformOptions = Options.Create(new ModulePlatformOptions { StorageRoot = "modules" });

        _storage = new FileSystemModuleStorage(
            platformOptions, _storageRoot, NullLogger<FileSystemModuleStorage>.Instance);

        _manager = new ModuleManager(
            _db,
            _storage,
            new ModulePackageReader(platformOptions),
            NullLogger<ModuleManager>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();

        try
        {
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort; a locked file must not fail the test run.
        }
    }

    private Task<ModuleOperationResult<AdminModuleDto>> InstallAsync(
        string name = "requests", string version = "1.0.0") =>
        _manager.InstallAsync(TestPackageBuilder.Valid(name, version).Build(), "test-user");

    /* ---------------- installation ---------------- */

    [Fact]
    public async Task Install_registers_the_module_and_writes_artifacts()
    {
        var result = await InstallAsync();

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        result.Value!.Name.Should().Be("requests");
        result.Value.Versions.Should().ContainSingle().Which.Version.Should().Be("1.0.0");

        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeTrue();
        (await _storage.ListAsync("requests/1.0.0")).Should().Contain("remoteEntry.json");
    }

    [Fact]
    public async Task Install_does_not_activate_the_new_version()
    {
        // A freshly installed version lands dark so it can be verified before
        // any user is exposed to it.
        var result = await InstallAsync();

        result.Value!.ActiveVersion.Should().BeNull();
        (await _manager.GetDiscoverableAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Install_records_who_installed_it()
    {
        await InstallAsync();

        var version = await _db.ModuleVersions.SingleAsync();
        version.InstalledBy.Should().Be("test-user", "installs are privileged and must be auditable");
    }

    [Fact]
    public async Task Install_rejects_an_invalid_package_without_touching_storage()
    {
        var hostile = TestPackageBuilder.Valid().AddFile("../../evil.js", "owned()").Build();

        var result = await _manager.InstallAsync(hostile, "test-user");

        result.Status.Should().Be(ModuleOperationStatus.Invalid);
        (await _db.Modules.CountAsync()).Should().Be(0);
        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeFalse();
    }

    [Fact]
    public async Task Installing_the_same_version_twice_is_a_conflict()
    {
        await InstallAsync(version: "1.0.0");

        var second = await InstallAsync(version: "1.0.0");

        second.Status.Should().Be(ModuleOperationStatus.Conflict);
        second.Errors.Should().ContainSingle().Which.Code.Should().Be("version_exists");
    }

    [Fact]
    public async Task Installing_a_duplicate_version_leaves_the_original_artifacts_intact()
    {
        await InstallAsync(version: "1.0.0");
        var before = await File.ReadAllTextAsync(
            Path.Combine(_storage.GetPhysicalPath("requests/1.0.0"), "remoteEntry.json"));

        await InstallAsync(version: "1.0.0");

        var after = await File.ReadAllTextAsync(
            Path.Combine(_storage.GetPhysicalPath("requests/1.0.0"), "remoteEntry.json"));

        after.Should().Be(before, "published versioned artifacts are immutable");
    }

    [Fact]
    public async Task Multiple_versions_coexist()
    {
        await InstallAsync(version: "1.0.0");
        await InstallAsync(version: "1.1.0");
        await InstallAsync(version: "2.0.0");

        var admin = await _manager.GetForAdminAsync("requests");

        admin.Value!.Versions.Select(v => v.Version)
            .Should().Equal(["2.0.0", "1.1.0", "1.0.0"], "versions are listed newest first");

        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeTrue();
        (await _storage.ExistsAsync("requests/1.1.0")).Should().BeTrue();
        (await _storage.ExistsAsync("requests/2.0.0")).Should().BeTrue();
    }

    [Fact]
    public async Task Version_ordering_is_numeric_not_lexicographic()
    {
        await InstallAsync(version: "1.9.0");
        await InstallAsync(version: "1.10.0");

        var admin = await _manager.GetForAdminAsync("requests");

        admin.Value!.Versions.First().Version.Should().Be("1.10.0", "1.10.0 > 1.9.0");
    }

    /* ---------------- activation and discovery ---------------- */

    [Fact]
    public async Task Activate_makes_the_module_discoverable_with_a_versioned_entry_url()
    {
        await InstallAsync(version: "1.0.0");

        await _manager.ActivateAsync("requests", "1.0.0");

        var discoverable = await _manager.GetDiscoverableAsync();
        var module = discoverable.Should().ContainSingle().Subject;

        module.Name.Should().Be("requests");
        module.Version.Should().Be("1.0.0");
        module.RoutesExposedModule.Should().Be("./Routes");

        // The entry URL is what Native Federation uses to derive the remote's
        // base URL, so every chunk resolves under the version folder.
        module.EntryUrl.Should().Be("/modules/requests/1.0.0/remoteEntry.json");
    }

    [Fact]
    public async Task Discovery_carries_navigation_hints_and_required_permissions()
    {
        await InstallAsync();
        await _manager.ActivateAsync("requests", "1.0.0");

        var module = (await _manager.GetDiscoverableAsync()).Single();

        module.Navigation!.Icon.Should().Be("📝");
        module.Navigation.Order.Should().Be(10);
        module.RequiredPermissions.Should().Contain("requests.read");
    }

    [Fact]
    public async Task Activating_a_new_version_switches_traffic_without_deleting_the_old_one()
    {
        await InstallAsync(version: "1.0.0");
        await InstallAsync(version: "1.1.0");
        await _manager.ActivateAsync("requests", "1.0.0");

        await _manager.ActivateAsync("requests", "1.1.0");

        (await _manager.GetDiscoverableAsync()).Single().EntryUrl
            .Should().Be("/modules/requests/1.1.0/remoteEntry.json");

        // 1.0.0 stays on disk, so its URLs remain valid for anything already
        // cached and rollback is instant.
        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeTrue();
    }

    [Fact]
    public async Task Only_one_version_is_ever_active()
    {
        await InstallAsync(version: "1.0.0");
        await InstallAsync(version: "1.1.0");

        await _manager.ActivateAsync("requests", "1.0.0");
        await _manager.ActivateAsync("requests", "1.1.0");

        var admin = await _manager.GetForAdminAsync("requests");
        admin.Value!.Versions.Count(v => v.IsActive).Should().Be(1);
        admin.Value.ActiveVersion.Should().Be("1.1.0");
    }

    [Fact]
    public async Task Rollback_to_an_older_version_is_just_an_activation()
    {
        await InstallAsync(version: "1.0.0");
        await InstallAsync(version: "1.1.0");
        await _manager.ActivateAsync("requests", "1.1.0");

        var rollback = await _manager.ActivateAsync("requests", "1.0.0");

        rollback.IsSuccess.Should().BeTrue();
        (await _manager.GetDiscoverableAsync()).Single().Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task Activating_a_version_that_is_not_installed_is_not_found()
    {
        await InstallAsync(version: "1.0.0");

        var result = await _manager.ActivateAsync("requests", "9.9.9");

        result.Status.Should().Be(ModuleOperationStatus.NotFound);
    }

    [Fact]
    public async Task Activating_refuses_when_artifacts_are_missing_from_storage()
    {
        await InstallAsync(version: "1.0.0");

        // Simulate storage drift, e.g. a restored database with an empty volume.
        await _storage.DeleteAsync("requests/1.0.0");

        var result = await _manager.ActivateAsync("requests", "1.0.0");

        result.Status.Should().Be(ModuleOperationStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("artifacts_missing");
    }

    [Fact]
    public async Task Deactivate_removes_the_module_from_discovery_but_keeps_it_installed()
    {
        await InstallAsync();
        await _manager.ActivateAsync("requests", "1.0.0");

        await _manager.DeactivateAsync("requests", "1.0.0");

        (await _manager.GetDiscoverableAsync()).Should().BeEmpty();
        (await _manager.GetForAdminAsync("requests")).Value!.Versions.Should().ContainSingle();
        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeTrue();
    }

    /* ---------------- enable / disable ---------------- */

    [Fact]
    public async Task A_disabled_module_is_hidden_from_discovery()
    {
        await InstallAsync();
        await _manager.ActivateAsync("requests", "1.0.0");

        await _manager.SetEnabledAsync("requests", false);

        (await _manager.GetDiscoverableAsync()).Should().BeEmpty(
            "the Shell must not offer a disabled module");

        // ...but the admin view still shows it, so it can be re-enabled.
        (await _manager.GetForAdminAsync("requests")).Value!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Fetching_a_disabled_module_by_name_reports_not_found()
    {
        await InstallAsync();
        await _manager.ActivateAsync("requests", "1.0.0");
        await _manager.SetEnabledAsync("requests", false);

        var result = await _manager.GetDiscoverableByNameAsync("requests");

        result.Status.Should().Be(ModuleOperationStatus.NotFound);
    }

    [Fact]
    public async Task Re_enabling_restores_discovery_with_the_same_active_version()
    {
        await InstallAsync();
        await _manager.ActivateAsync("requests", "1.0.0");
        await _manager.SetEnabledAsync("requests", false);

        await _manager.SetEnabledAsync("requests", true);

        (await _manager.GetDiscoverableAsync()).Single().Version.Should().Be("1.0.0");
    }

    /* ---------------- uninstall ---------------- */

    [Fact]
    public async Task Uninstalling_the_active_version_is_refused()
    {
        await InstallAsync();
        await _manager.ActivateAsync("requests", "1.0.0");

        var result = await _manager.UninstallVersionAsync("requests", "1.0.0");

        result.Status.Should().Be(ModuleOperationStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("version_active");

        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeTrue(
            "a refused uninstall must not touch the artifacts");
    }

    [Fact]
    public async Task Uninstalling_an_inactive_version_removes_row_and_artifacts()
    {
        await InstallAsync(version: "1.0.0");
        await InstallAsync(version: "1.1.0");
        await _manager.ActivateAsync("requests", "1.1.0");

        var result = await _manager.UninstallVersionAsync("requests", "1.0.0");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Versions.Should().ContainSingle().Which.Version.Should().Be("1.1.0");
        (await _storage.ExistsAsync("requests/1.0.0")).Should().BeFalse();

        // The active version is untouched.
        (await _manager.GetDiscoverableAsync()).Single().Version.Should().Be("1.1.0");
    }

    [Fact]
    public async Task A_version_can_be_reinstalled_after_being_uninstalled()
    {
        await InstallAsync(version: "1.0.0");
        await InstallAsync(version: "1.1.0");
        await _manager.ActivateAsync("requests", "1.1.0");
        await _manager.UninstallVersionAsync("requests", "1.0.0");

        var again = await InstallAsync(version: "1.0.0");

        again.IsSuccess.Should().BeTrue(
            "removing a version frees its identity, since nothing can still be caching it");
    }

    /* ---------------- multi-tenant / multi-team ---------------- */

    [Fact]
    public async Task Modules_from_different_teams_are_independent()
    {
        await InstallAsync("requests", "1.0.0");
        await InstallAsync("approvals", "2.1.0");
        await _manager.ActivateAsync("requests", "1.0.0");
        await _manager.ActivateAsync("approvals", "2.1.0");

        await _manager.SetEnabledAsync("requests", false);

        var discoverable = await _manager.GetDiscoverableAsync();

        discoverable.Should().ContainSingle().Which.Name.Should().Be("approvals",
            "disabling one team's module must not affect another's");
    }

    [Fact]
    public async Task Unknown_modules_report_not_found()
    {
        (await _manager.GetDiscoverableByNameAsync("nope")).Status
            .Should().Be(ModuleOperationStatus.NotFound);
        (await _manager.GetForAdminAsync("nope")).Status
            .Should().Be(ModuleOperationStatus.NotFound);
        (await _manager.ActivateAsync("nope", "1.0.0")).Status
            .Should().Be(ModuleOperationStatus.NotFound);
    }
}
