using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ModulePlatform.Core.Domain;
using ModulePlatform.Infrastructure.Data;

namespace ModulePlatform.Api.Endpoints;

/// <summary>
/// Generic JSON-document storage shared by every module.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a remote module can persist real data through the platform's
/// own database — <c>ModuleRecords</c> — without the platform needing a
/// bespoke table, entity or migration per module. A module's frontend reaches
/// it through <c>shell.api</c> (same-origin, auth token attached
/// automatically), never directly.
/// </para>
/// <para>
/// Isolation is enforced structurally: every route is scoped by
/// <c>{moduleName}/{collection}</c>, taken from the URL, and a module's own
/// federated code only ever calls its own module name. There is currently no
/// server-side check that the caller "owns" <c>moduleName</c> — any
/// authenticated user can write to any module's collection, same as any
/// authenticated user can call any other same-origin API today. Add a
/// per-module authorization policy here before treating this as
/// multi-tenant-safe against a hostile caller.
/// </para>
/// <para>
/// The list endpoint additionally supports <c>?status=</c>/<c>?createdBy=</c>
/// filtering and opt-in <c>?page=</c>/<c>?pageSize=</c> pagination against the
/// <see cref="ModuleRecord.Status"/>/<see cref="ModuleRecord.CreatedBy"/>
/// columns — see <see cref="ModuleRecord"/> for why those live outside the
/// opaque JSON blob. Neither is echoed back inside a DTO's <c>data</c> —
/// they're returned as their own top-level DTO fields instead, the same as
/// <c>id</c> always has been.
/// </para>
/// <para>
/// A record's id is likewise always platform-generated (see
/// <see cref="NextExternalIdAsync"/>) — <c>POST</c> never reads one from the
/// body, so a module never invents its own id-collision handling the way an
/// earlier revision of this API required callers to.
/// </para>
/// </remarks>
public static class ModuleDataEndpoints
{
    public const string DataPolicy = "moduledata.readwrite";

    public static IEndpointRouteBuilder MapModuleDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data/{moduleName}/{collection}")
            .RequireAuthorization(DataPolicy)
            .WithTags("ModuleData");

        group.MapGet("/", ListAsync)
            .WithName("ListModuleRecords")
            .WithSummary("All records in one module's collection, as parsed JSON.");

        group.MapGet("/{externalId}", GetAsync)
            .WithName("GetModuleRecord")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .DisableAntiforgery()
            .WithName("CreateModuleRecord")
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{externalId}", UpsertAsync)
            .DisableAntiforgery()
            .WithName("UpsertModuleRecord")
            .WithSummary("Create or fully replace one record.");

        group.MapDelete("/{externalId}", DeleteAsync)
            .WithName("DeleteModuleRecord")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Lists a module's collection, optionally filtered by the two indexed
    /// generic columns and/or paginated.
    /// </summary>
    /// <remarks>
    /// <paramref name="page"/>/<paramref name="pageSize"/> are opt-in: when
    /// omitted, every matching row is returned exactly as before (no breaking
    /// change for existing callers). The total count after filtering (before
    /// paging) is always reported via the <c>X-Total-Count</c> response
    /// header, so a caller can start paginating without a body-shape change.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        string moduleName, string collection, string? status, string? createdBy,
        int? page, int? pageSize, HttpContext http, ModulePlatformDbContext db, CancellationToken ct)
    {
        var query = db.ModuleRecords
            .AsNoTracking()
            .Where(r => r.ModuleName == moduleName && r.Collection == collection);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(createdBy)) query = query.Where(r => r.CreatedBy == createdBy);

        query = query.OrderBy(r => r.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        http.Response.Headers["X-Total-Count"] = totalCount.ToString();

        if (page is > 0 && pageSize is > 0)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }

        var rows = await query.ToListAsync(ct);
        return Results.Ok(rows.Select(ToDto));
    }

    private static async Task<IResult> GetAsync(
        string moduleName, string collection, string externalId,
        ModulePlatformDbContext db, CancellationToken ct)
    {
        var row = await Find(db, moduleName, collection, externalId, ct);
        return row is null ? NotFound(moduleName, collection, externalId) : Results.Ok(ToDto(row));
    }

    /// <summary>
    /// Creates a new record with a platform-generated id — the client never
    /// supplies one (see <see cref="ExtractFilterFields"/> and
    /// <see cref="NextExternalIdAsync"/>). Retries a handful of times if the
    /// generated id loses a race against a concurrent create, guarded by the
    /// same unique index that already protects <see cref="UpsertAsync"/>.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        string moduleName, string collection, HttpContext http,
        ModulePlatformDbContext db, CancellationToken ct)
    {
        var (json, error) = await ReadBodyAsync(http, ct);
        if (error is not null) return error;

        var (status, storedJson) = ExtractFilterFields(json!);
        var who = http.User.Identity?.Name;

        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var externalId = await NextExternalIdAsync(db, moduleName, collection, ct);
            var row = new ModuleRecord
            {
                ModuleName = moduleName,
                Collection = collection,
                ExternalId = externalId,
                DataJson = storedJson,
                Status = status,
                CreatedBy = who,
                UpdatedBy = who,
            };
            db.ModuleRecords.Add(row);

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/data/{moduleName}/{collection}/{externalId}", ToDto(row));
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                // Lost a race for this id against a concurrent create — drop
                // the failed attempt from the change tracker and mint a fresh one.
                db.ModuleRecords.Remove(row);
            }
        }

        return Results.Problem(
            title: "id_generation_failed",
            detail: $"Could not allocate a unique id for {moduleName}/{collection} after {maxAttempts} attempts.",
            statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> UpsertAsync(
        string moduleName, string collection, string externalId, HttpContext http,
        ModulePlatformDbContext db, CancellationToken ct)
    {
        var (json, error) = await ReadBodyAsync(http, ct);
        if (error is not null) return error;

        var (status, storedJson) = ExtractFilterFields(json!);
        var who = http.User.Identity?.Name;
        var row = await Find(db, moduleName, collection, externalId, ct);

        if (row is null)
        {
            row = new ModuleRecord
            {
                ModuleName = moduleName,
                Collection = collection,
                ExternalId = externalId,
                DataJson = storedJson,
                Status = status,
                CreatedBy = who,
                UpdatedBy = who,
            };
            db.ModuleRecords.Add(row);
        }
        else
        {
            // CreatedBy is deliberately left untouched here — it's the
            // record's original owner for edit-gating purposes, which must
            // survive whoever last edited it.
            row.DataJson = storedJson;
            row.Status = status;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = who;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(row));
    }

    private static async Task<IResult> DeleteAsync(
        string moduleName, string collection, string externalId,
        ModulePlatformDbContext db, CancellationToken ct)
    {
        var row = await Find(db, moduleName, collection, externalId, ct);
        if (row is null) return NotFound(moduleName, collection, externalId);

        db.ModuleRecords.Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /* ---------------------------------------------------------------- */

    private static Task<ModuleRecord?> Find(
        ModulePlatformDbContext db, string moduleName, string collection, string externalId, CancellationToken ct) =>
        db.ModuleRecords.FirstOrDefaultAsync(
            r => r.ModuleName == moduleName && r.Collection == collection && r.ExternalId == externalId, ct);

    private static IResult NotFound(string moduleName, string collection, string externalId) =>
        Results.Problem(
            title: "Not found",
            detail: $"No record '{externalId}' in {moduleName}/{collection}.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Reads the request body as JSON, validating only that it's a
    /// well-formed JSON object. The platform decides ids itself now (see
    /// <see cref="NextExternalIdAsync"/>) — it never reads one out of the
    /// body, so there's nothing here left to cross-check against the URL.
    /// </summary>
    private static async Task<(string? Json, IResult? Error)> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        var raw = await reader.ReadToEndAsync(ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(
                title: "invalid_json", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return (null, Results.Problem(
                title: "invalid_json", detail: "Body must be a JSON object.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (raw, null);
    }

    /// <summary>
    /// The next id for a new record in (<paramref name="moduleName"/>,
    /// <paramref name="collection"/>) — one past the highest purely-numeric
    /// <see cref="ModuleRecord.ExternalId"/> already in that scope (existing
    /// ids that aren't purely numeric, e.g. hand-seeded ones, just don't
    /// count towards the max). Not perfectly race-free on its own; the retry
    /// loop in <see cref="CreateAsync"/> around the unique index is what
    /// actually guarantees uniqueness under concurrent creates.
    /// </summary>
    private static async Task<string> NextExternalIdAsync(
        ModulePlatformDbContext db, string moduleName, string collection, CancellationToken ct)
    {
        var existingIds = await db.ModuleRecords
            .Where(r => r.ModuleName == moduleName && r.Collection == collection)
            .Select(r => r.ExternalId)
            .ToListAsync(ct);

        var next = existingIds
            .Select(id => long.TryParse(id, out var n) ? n : 0L)
            .DefaultIfEmpty(0L)
            .Max() + 1;

        return next.ToString();
    }

    /// <summary>
    /// Every field the platform itself owns and reports back as a top-level
    /// DTO property (see <see cref="ToDto"/>) — never legitimately part of a
    /// module's own JSON, so a client sending one is always either copy-paste
    /// from a previous response or an attempt to fake one of them.
    /// </summary>
    private static readonly string[] PlatformOwnedFields =
        ["id", "status", "submittedBy", "createdAt", "updatedAt", "createdBy", "updatedBy"];

    /// <summary>
    /// Pulls the conventional <c>"status"</c> string field (if present) out
    /// of a record's raw JSON so it can live in its own indexed column
    /// instead of only inside the opaque blob — and is removed from the JSON
    /// at that point, so it isn't stored twice.
    /// </summary>
    /// <remarks>
    /// Every other <see cref="PlatformOwnedFields"/> entry — <c>id</c>,
    /// <c>submittedBy</c>, and the four audit fields <c>createdAt</c>/
    /// <c>updatedAt</c>/<c>createdBy</c>/<c>updatedBy</c> — is stripped too,
    /// but never read: id is always platform-generated (see
    /// <see cref="NextExternalIdAsync"/>), ownership is always
    /// <see cref="ModuleRecord.CreatedBy"/> (the authenticated caller), and
    /// the audit timestamps/identities are always set from
    /// <see cref="DateTimeOffset.UtcNow"/> and the caller's own identity —
    /// never from anything a client can put in a request body. Silently
    /// discarding them (rather than rejecting the request) is what makes it
    /// safe to POST back a previous GET response unmodified, which is a
    /// common thing to do when testing by hand.
    /// </remarks>
    private static (string? Status, string Json) ExtractFilterFields(string raw)
    {
        if (JsonNode.Parse(raw) is not JsonObject obj)
        {
            return (null, raw);
        }

        var status = TakeStringField(obj, "status");
        foreach (var field in PlatformOwnedFields)
        {
            obj.Remove(field);
        }
        return (status, obj.ToJsonString());
    }

    private static string? TakeStringField(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var value) ||
            value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue(out string? stringValue))
        {
            return null;
        }

        obj.Remove(name);
        return stringValue;
    }

    private static object ToDto(ModuleRecord r) => new
    {
        id = r.ExternalId,
        status = r.Status,
        data = JsonDocument.Parse(r.DataJson).RootElement,
        createdAt = r.CreatedAt,
        updatedAt = r.UpdatedAt,
        createdBy = r.CreatedBy,
        updatedBy = r.UpdatedBy,
    };
}
