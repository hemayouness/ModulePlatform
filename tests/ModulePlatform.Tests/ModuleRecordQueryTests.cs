using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ModulePlatform.Core.Domain;
using ModulePlatform.Infrastructure.Data;

namespace ModulePlatform.Tests;

/// <summary>
/// Ordering, paging and counting of module records against a real relational
/// database (SQLite in-memory), so the query shapes are genuinely translated
/// rather than evaluated in LINQ-to-objects.
/// </summary>
public sealed class ModuleRecordQueryTests : IAsyncLifetime
{
    private const string Module = "employee-needs";
    private const string Collection = "employee-needs";

    private SqliteConnection _connection = null!;
    private ModulePlatformDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ModulePlatformDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ModulePlatformDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task SeedAsync(params (string Id, string? Status, string? CreatedBy, int MinutesOld)[] rows)
    {
        foreach (var (id, status, createdBy, minutes) in rows)
        {
            _db.ModuleRecords.Add(new ModuleRecord
            {
                ModuleName = Module,
                Collection = Collection,
                ExternalId = id,
                DataJson = """{"title":"x"}""",
                Status = status,
                CreatedBy = createdBy,
                CreatedAt = Epoch.AddMinutes(minutes),
                UpdatedAt = Epoch.AddMinutes(minutes),
            });
        }

        await _db.SaveChangesAsync();
    }

    private Task<List<string>> ListAsync(ModuleRecordSort sort, ModuleRecordPaging paging) =>
        paging.Apply(sort.Apply(_db.Filtered(Module, Collection)))
            .Select(r => r.ExternalId)
            .ToListAsync();

    /* ----------------------------- ordering ----------------------------- */

    [Fact]
    public async Task Default_order_is_oldest_first_so_a_new_record_sorts_last()
    {
        await SeedAsync(("1", "draft", null, 0), ("2", "draft", null, 10), ("3", "draft", null, 20));

        var ids = await ListAsync(ModuleRecordSort.Default, ModuleRecordPaging.None);

        ids.Should().Equal("1", "2", "3");
    }

    [Fact]
    public async Task Descending_reverses_the_order()
    {
        await SeedAsync(("1", "draft", null, 0), ("2", "draft", null, 10), ("3", "draft", null, 20));

        ModuleRecordSort.TryParse("-createdAt", out var sort, out _);
        var ids = await ListAsync(sort, ModuleRecordPaging.None);

        ids.Should().Equal("3", "2", "1");
    }

    [Fact]
    public async Task Records_created_in_the_same_instant_come_back_in_a_stable_order()
    {
        // The demo seeder POSTs its records with Promise.all, so identical
        // timestamps are routine rather than theoretical.
        await SeedAsync(("1", "draft", null, 0), ("2", "draft", null, 0), ("3", "draft", null, 0));

        var first = await ListAsync(ModuleRecordSort.Default, ModuleRecordPaging.None);
        var second = await ListAsync(ModuleRecordSort.Default, ModuleRecordPaging.None);

        second.Should().Equal(first,
            "without a tiebreaker the database is free to reorder ties between two identical "
            + "requests, which would let a row shuffle across a page boundary");
    }

    [Fact]
    public async Task Sorting_by_status_uses_the_indexed_column()
    {
        await SeedAsync(("1", "submitted", null, 0), ("2", "approved", null, 10), ("3", "draft", null, 20));

        ModuleRecordSort.TryParse("status", out var sort, out _);
        var ids = await ListAsync(sort, ModuleRecordPaging.None);

        ids.Should().Equal("2", "3", "1");
    }

    [Fact]
    public async Task Sorting_by_id_is_lexicographic_because_the_column_is_text()
    {
        await SeedAsync(("9", "draft", null, 0), ("10", "draft", null, 10));

        ModuleRecordSort.TryParse("id", out var sort, out _);
        var ids = await ListAsync(sort, ModuleRecordPaging.None);

        ids.Should().Equal(["10", "9"],
            "ExternalId is a string column — a module wanting chronological order should sort by "
            + "createdAt, which is why that stays the default");
    }

    [Fact]
    public async Task Ordering_never_reaches_into_another_modules_collection()
    {
        await SeedAsync(("1", "draft", null, 0));
        _db.ModuleRecords.Add(new ModuleRecord
        {
            ModuleName = "approvals",
            Collection = "approvals",
            ExternalId = "999",
            DataJson = "{}",
            Status = "draft",
            CreatedAt = Epoch,
            UpdatedAt = Epoch,
        });
        await _db.SaveChangesAsync();

        var ids = await ListAsync(ModuleRecordSort.Default, ModuleRecordPaging.None);

        ids.Should().Equal("1");
    }

    /* ------------------------------ paging ------------------------------ */

    [Fact]
    public async Task Without_paging_every_matching_row_comes_back()
    {
        await SeedAsync(("1", "draft", null, 0), ("2", "draft", null, 10), ("3", "draft", null, 20));

        var ids = await ListAsync(ModuleRecordSort.Default, ModuleRecordPaging.None);

        ids.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_page_returns_only_its_own_slice()
    {
        await SeedAsync(
            ("1", "draft", null, 0), ("2", "draft", null, 10), ("3", "draft", null, 20),
            ("4", "draft", null, 30), ("5", "draft", null, 40));

        ModuleRecordPaging.TryParse(2, 2, 200, 50, out var paging, out _);
        var ids = await ListAsync(ModuleRecordSort.Default, paging);

        ids.Should().Equal("3", "4");
    }

    [Fact]
    public async Task Paging_past_the_end_yields_nothing_rather_than_failing()
    {
        await SeedAsync(("1", "draft", null, 0));

        ModuleRecordPaging.TryParse(9, 5, 200, 50, out var paging, out _);
        var ids = await ListAsync(ModuleRecordSort.Default, paging);

        ids.Should().BeEmpty(
            "the client clamps back to the last page when this happens, so an empty page must be "
            + "an ordinary result and not an error");
    }

    /* ------------------------------ counts ------------------------------ */

    [Fact]
    public async Task Counts_are_grouped_by_status()
    {
        await SeedAsync(
            ("1", "draft", null, 0), ("2", "draft", null, 10),
            ("3", "submitted", null, 20), ("4", "approved", null, 30));

        var buckets = await _db.CountByStatusAsync(Module, Collection, null, null);

        buckets.Should().Equal(
            new ModuleRecordStatusCount("approved", 1),
            new ModuleRecordStatusCount("draft", 2),
            new ModuleRecordStatusCount("submitted", 1));
    }

    [Fact]
    public async Task Records_with_no_status_are_counted_under_null()
    {
        await SeedAsync(("1", null, null, 0), ("2", "draft", null, 10));

        var buckets = await _db.CountByStatusAsync(Module, Collection, null, null);

        buckets.Should().Contain(new ModuleRecordStatusCount(null, 1),
            "a module with no workflow concept still has records, and the platform does not get "
            + "to invent a name for their absent status");
    }

    [Fact]
    public async Task The_total_is_the_sum_of_the_buckets()
    {
        await SeedAsync(
            ("1", "draft", null, 0), ("2", "submitted", null, 10),
            ("3", "submitted", null, 20), ("4", null, null, 30));

        var buckets = await _db.CountByStatusAsync(Module, Collection, null, null);

        buckets.Sum(b => b.Count).Should().Be(4);
    }

    [Fact]
    public async Task Counts_honour_the_created_by_filter_so_they_compose_with_the_list()
    {
        await SeedAsync(
            ("1", "draft", "ada@example.local", 0),
            ("2", "draft", "grace@example.local", 10),
            ("3", "submitted", "ada@example.local", 20));

        var buckets = await _db.CountByStatusAsync(Module, Collection, null, "ada@example.local");

        buckets.Should().Equal(
            new ModuleRecordStatusCount("draft", 1),
            new ModuleRecordStatusCount("submitted", 1));
    }

    [Fact]
    public async Task Counts_are_scoped_to_one_module_and_collection()
    {
        await SeedAsync(("1", "draft", null, 0));
        _db.ModuleRecords.Add(new ModuleRecord
        {
            ModuleName = "approvals",
            Collection = "approvals",
            ExternalId = "1",
            DataJson = "{}",
            Status = "draft",
            CreatedAt = Epoch,
            UpdatedAt = Epoch,
        });
        await _db.SaveChangesAsync();

        var buckets = await _db.CountByStatusAsync(Module, Collection, null, null);

        buckets.Sum(b => b.Count).Should().Be(1);
    }

    [Fact]
    public async Task An_empty_collection_counts_to_nothing_rather_than_failing()
    {
        var buckets = await _db.CountByStatusAsync(Module, Collection, null, null);

        buckets.Should().BeEmpty();
        buckets.Sum(b => b.Count).Should().Be(0);
    }
}
