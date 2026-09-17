using System.ComponentModel.DataAnnotations;

namespace ModulePlatform.Core.Domain;

/// <summary>
/// A single piece of business data submitted by a module, stored as an opaque
/// JSON document.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately generic and shared by every module — it exists so a
/// remote (e.g. <c>requests</c>) can persist real data through the platform's
/// own database without the platform needing a bespoke table, entity or
/// migration per module or per team.
/// </para>
/// <para>
/// <see cref="DataJson"/> itself is opaque: the module decides its own shape
/// and versions it itself (e.g. by including a <c>"schemaVersion"</c> field),
/// the same way `mfe-contracts` events are versioned. This keeps the platform
/// decoupled from every module's business shape — exactly the same reasoning
/// that keeps modules from sharing application services with each other. The
/// one deliberate exception is <see cref="Status"/>: a conventionally-named
/// field a module MAY include in its JSON to get real, indexed, server-side
/// filtering and pagination on "workflow state" — one attribute almost every
/// module ends up needing to query by.
/// </para>
/// <para>
/// "Owning identity" is deliberately NOT a second denormalized field here —
/// use <see cref="CreatedBy"/> for that. It is already populated from the
/// authenticated caller (never trusted from the request body), so it can't be
/// spoofed the way a client-supplied "submittedBy"-style JSON field could.
/// </para>
/// <para>
/// Isolation between modules and between record kinds within a module is by
/// convention (<see cref="ModuleName"/> + <see cref="Collection"/>), enforced
/// at the API layer: a module's frontend can only address its own rows.
/// </para>
/// </remarks>
public class ModuleRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning module's registry name, e.g. <c>requests</c>. Not a foreign key
    /// on purpose — a module can write data before/without being formally
    /// installed (e.g. during development against the standalone harness).</summary>
    [MaxLength(64)]
    public required string ModuleName { get; set; }

    /// <summary>
    /// Logical bucket within the module, e.g. <c>requests</c> or
    /// <c>approvals-comments</c>. Lets one module keep more than one kind of
    /// record without needing its own table.
    /// </summary>
    [MaxLength(64)]
    public required string Collection { get; set; }

    /// <summary>
    /// The module's own natural key for this record (e.g. <c>REQ-1004</c>).
    /// Unique within (ModuleName, Collection) — the module owns ID generation.
    /// </summary>
    [MaxLength(128)]
    public required string ExternalId { get; set; }

    /// <summary>The record itself, as a JSON document. Opaque to the platform.</summary>
    public required string DataJson { get; set; }

    /// <summary>
    /// Optional workflow status, denormalized out of the record JSON into its
    /// own column so it can be filtered and sorted by the database instead of
    /// requiring every consumer to fetch and parse the whole document first.
    /// Populated automatically whenever a module's record includes a
    /// top-level string <c>"status"</c> field (and stripped out of
    /// <see cref="DataJson"/> at that point, to avoid storing it twice); a
    /// module with no such concept simply never has this set.
    /// </summary>
    [MaxLength(32)]
    public string? Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Identity of the authenticated caller that created the record. Doubles
    /// as the record's "owner" for server-side filtering/pagination (e.g.
    /// "only my records") — see the remarks above for why this is the one
    /// column used for that instead of a second, client-suppliable field.
    /// </summary>
    [MaxLength(128)]
    public string? CreatedBy { get; set; }

    [MaxLength(128)]
    public string? UpdatedBy { get; set; }
}
