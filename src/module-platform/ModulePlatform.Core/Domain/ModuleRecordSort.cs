namespace ModulePlatform.Core.Domain;

/// <summary>
/// The columns a module's collection may be ordered by.
/// </summary>
/// <remarks>
/// Deliberately an enum over real columns rather than a field name carried
/// through as a string. Nothing a caller types ever reaches the query — the
/// name is resolved to one of these members up front and
/// <see cref="ModuleRecordSort.Apply"/> switches over compile-time lambdas — so
/// there is no dynamic LINQ, nothing for injection to attach to, and no way for
/// an unrecognised field to fall through to something surprising.
/// </remarks>
public enum ModuleRecordSortField
{
    CreatedAt,
    UpdatedAt,
    Status,
    CreatedBy,
    ExternalId,
}

/// <summary>
/// A parsed <c>?sort=</c> specification: one column, one direction.
/// </summary>
/// <remarks>
/// <para>
/// Syntax is <c>?sort=field</c> with an optional leading <c>-</c> for
/// descending (<c>?sort=-updatedAt</c>). That is the JSON:API convention, needs
/// no delimiter escaping, and leaves room to accept a comma-separated list of
/// keys later without inventing a second level of delimiter.
/// </para>
/// <para>
/// Only real columns are sortable. Fields inside a record's <c>DataJson</c> are
/// not, and are rejected rather than ignored: the platform stores that payload
/// as an opaque blob, so sorting by one would need either a provider-specific
/// <c>JSON_VALUE</c> (which the SQLite test path could not run) or an in-memory
/// sort of the whole filtered collection — which would quietly undo the point
/// of having a page-size limit at all.
/// </para>
/// </remarks>
public readonly record struct ModuleRecordSort(ModuleRecordSortField Field, bool Descending)
{
    /// <summary>
    /// <c>CreatedAt</c> ascending — what the endpoint did before <c>?sort=</c>
    /// existed. Callers rely on it: a newly created record sorts last, which is
    /// how a module's UI knows to jump to the final page after a create.
    /// </summary>
    public static ModuleRecordSort Default => new(ModuleRecordSortField.CreatedAt, false);

    private static readonly Dictionary<string, ModuleRecordSortField> Fields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = ModuleRecordSortField.CreatedAt,
            ["updatedAt"] = ModuleRecordSortField.UpdatedAt,
            ["status"] = ModuleRecordSortField.Status,
            ["createdBy"] = ModuleRecordSortField.CreatedBy,
            ["externalId"] = ModuleRecordSortField.ExternalId,
            // `id` is what the DTO calls ExternalId, so accept the name callers
            // actually see in a response rather than the one the column has.
            ["id"] = ModuleRecordSortField.ExternalId,
        };

    /// <summary>Field names accepted by <see cref="TryParse"/>, for error messages.</summary>
    public static IReadOnlyCollection<string> AllowedFields => Fields.Keys;

    /// <summary>
    /// Parses a <c>?sort=</c> value. An absent or blank value is
    /// <see cref="Default"/>, not an error — sorting is opt-in.
    /// </summary>
    public static bool TryParse(string? raw, out ModuleRecordSort sort, out string? error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            sort = Default;
            error = null;
            return true;
        }

        var spec = raw.Trim();
        var descending = spec.StartsWith('-');
        var name = descending ? spec[1..] : spec;

        if (!Fields.TryGetValue(name, out var field))
        {
            sort = Default;
            error =
                $"Unknown sort field '{name}'. Sortable fields: {string.Join(", ", Fields.Keys)}. " +
                "Fields inside a record's 'data' payload are not sortable — the platform stores it " +
                "as an opaque blob.";
            return false;
        }

        sort = new ModuleRecordSort(field, descending);
        error = null;
        return true;
    }

    /// <summary>Applies this ordering, with a deterministic tiebreaker.</summary>
    /// <remarks>
    /// The <c>ThenBy(Id)</c> matters more than it looks. Without it, rows
    /// sharing a value — several records created in the same instant by a
    /// parallel seed, or every record with the same status — come back in
    /// whatever order the database felt like, so two identical requests can
    /// disagree about which row sits on a page boundary. <c>Id</c> is the
    /// primary key, so the tiebreak is total and costs nothing.
    /// </remarks>
    public IQueryable<ModuleRecord> Apply(IQueryable<ModuleRecord> query)
    {
        IOrderedQueryable<ModuleRecord> ordered = (Field, Descending) switch
        {
            (ModuleRecordSortField.CreatedAt, false) => query.OrderBy(r => r.CreatedAt),
            (ModuleRecordSortField.CreatedAt, true) => query.OrderByDescending(r => r.CreatedAt),
            (ModuleRecordSortField.UpdatedAt, false) => query.OrderBy(r => r.UpdatedAt),
            (ModuleRecordSortField.UpdatedAt, true) => query.OrderByDescending(r => r.UpdatedAt),
            (ModuleRecordSortField.Status, false) => query.OrderBy(r => r.Status),
            (ModuleRecordSortField.Status, true) => query.OrderByDescending(r => r.Status),
            (ModuleRecordSortField.CreatedBy, false) => query.OrderBy(r => r.CreatedBy),
            (ModuleRecordSortField.CreatedBy, true) => query.OrderByDescending(r => r.CreatedBy),
            (ModuleRecordSortField.ExternalId, false) => query.OrderBy(r => r.ExternalId),
            (ModuleRecordSortField.ExternalId, true) => query.OrderByDescending(r => r.ExternalId),
            _ => query.OrderBy(r => r.CreatedAt),
        };

        return ordered.ThenBy(r => r.Id);
    }
}
