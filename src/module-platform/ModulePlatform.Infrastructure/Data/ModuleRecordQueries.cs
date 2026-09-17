using Microsoft.EntityFrameworkCore;
using ModulePlatform.Core.Domain;

namespace ModulePlatform.Infrastructure.Data;

/// <summary>One status bucket and how many records are in it.</summary>
/// <param name="Status">
/// <c>null</c> for records carrying no status at all — the platform reports the
/// absence rather than inventing a name for it, because what "no status" means
/// is the module's business, not the platform's.
/// </param>
public readonly record struct ModuleRecordStatusCount(string? Status, int Count);

/// <summary>
/// The query shapes the generic module-data endpoints are built from.
/// </summary>
/// <remarks>
/// These live here rather than inline in the endpoint so the list and the count
/// routes provably start from the same filter — a count that disagreed with the
/// list it describes would be worse than no count — and so they can be tested
/// against a real relational provider without standing up HTTP.
/// </remarks>
public static class ModuleRecordQueries
{
    /// <summary>
    /// One module's collection, narrowed by the two indexed generic columns.
    /// </summary>
    public static IQueryable<ModuleRecord> Filtered(
        this ModulePlatformDbContext db,
        string moduleName, string collection, string? status = null, string? createdBy = null)
    {
        var query = db.ModuleRecords
            .AsNoTracking()
            .Where(r => r.ModuleName == moduleName && r.Collection == collection);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(createdBy)) query = query.Where(r => r.CreatedBy == createdBy);
        return query;
    }

    /// <summary>
    /// How many records sit in each status, in a single grouped query.
    /// </summary>
    /// <remarks>
    /// The alternative a caller reaches for otherwise is listing with a filter
    /// purely to read <c>X-Total-Count</c> — which transfers and discards every
    /// matching record, and needs one round trip per status.
    /// Ordered in memory (there are only ever a handful of buckets) so responses
    /// are deterministic.
    /// </remarks>
    public static async Task<IReadOnlyList<ModuleRecordStatusCount>> CountByStatusAsync(
        this ModulePlatformDbContext db,
        string moduleName, string collection, string? status, string? createdBy,
        CancellationToken ct = default)
    {
        var groups = await db.Filtered(moduleName, collection, status, createdBy)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return groups
            .Select(g => new ModuleRecordStatusCount(g.Status, g.Count))
            .OrderBy(g => g.Status ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }
}
