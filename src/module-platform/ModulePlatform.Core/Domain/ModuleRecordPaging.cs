namespace ModulePlatform.Core.Domain;

/// <summary>
/// A validated <c>?page=</c>/<c>?pageSize=</c> pair, reduced to the
/// <c>Skip</c>/<c>Take</c> the query actually needs.
/// </summary>
/// <remarks>
/// Paging stays opt-in: supply neither parameter and every matching row comes
/// back, exactly as it did before pagination existed. What this type adds is a
/// ceiling on how much a single request may ask for, and a refusal to silently
/// do something other than what was asked.
/// </remarks>
public readonly record struct ModuleRecordPaging(int Skip, int? Take)
{
    /// <summary>No paging — return every matching row.</summary>
    public static ModuleRecordPaging None => new(0, null);

    /// <summary>
    /// Validates a page/page-size pair against the configured limits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An over-large <paramref name="pageSize"/> is <b>rejected</b>, not
    /// clamped. A client that asked for 1000 and silently received 200 would
    /// still compute its page count from 1000, conclude there was a single
    /// page, and hide the rest of the collection without surfacing an error
    /// anywhere. Refusing is louder and cannot lose data.
    /// </para>
    /// <para>
    /// A <paramref name="page"/> with no <paramref name="pageSize"/> falls back
    /// to <paramref name="defaultPageSize"/>, because asking for "page 3" and
    /// receiving the entire collection is never what the caller meant. The
    /// reverse — a page size with no page — deliberately does not paginate, so
    /// that a caller reading only the <c>X-Total-Count</c> header keeps working.
    /// </para>
    /// </remarks>
    public static bool TryParse(
        int? page, int? pageSize, int maxPageSize, int defaultPageSize,
        out ModuleRecordPaging paging, out string? error)
    {
        paging = None;
        error = null;

        // Guard against a misconfigured options file rather than trusting it.
        var max = maxPageSize > 0 ? maxPageSize : 200;
        var fallback = defaultPageSize > 0 ? Math.Min(defaultPageSize, max) : Math.Min(50, max);

        if (page is <= 0)
        {
            error = $"'page' must be 1 or greater (got {page}). Pages are 1-based.";
            return false;
        }

        if (pageSize is <= 0)
        {
            error = $"'pageSize' must be 1 or greater (got {pageSize}).";
            return false;
        }

        if (pageSize > max)
        {
            error =
                $"'pageSize' of {pageSize} exceeds the maximum of {max}. " +
                "Request a smaller page and read 'X-Total-Count' to page through the rest.";
            return false;
        }

        if (page is null)
        {
            // A size without a page: validated above, but not applied — the
            // caller is almost certainly after the count header.
            return true;
        }

        var size = pageSize ?? fallback;
        paging = new ModuleRecordPaging((page.Value - 1) * size, size);
        return true;
    }

    /// <summary>Applies <c>Skip</c>/<c>Take</c>, or nothing at all when unpaged.</summary>
    public IQueryable<ModuleRecord> Apply(IQueryable<ModuleRecord> query) =>
        Take is null ? query : query.Skip(Skip).Take(Take.Value);
}
