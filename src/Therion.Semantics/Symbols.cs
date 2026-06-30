// Implementation Plan �5.1 � symbol records produced by the binder.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Kind of declaration that introduced a station into the model.</summary>
public enum StationDeclarationKind
{
    /// <summary>Mentioned as the <c>from</c>/<c>to</c> of a data row.</summary>
    Shot,
    /// <summary>Introduced by a <c>fix</c> command.</summary>
    Fix,
    /// <summary>Referenced by an <c>equate</c> command.</summary>
    Equate,
}

/// <summary>A station symbol: a unique fully-qualified station name.</summary>
public sealed record StationSymbol(
    QualifiedName Name,
    SourceSpan DeclarationSpan,
    StationDeclarationKind Kind,
    ImmutableArray<SourceSpan> References)
{
    /// <summary>The station's <c>station &lt;name&gt; "comment"</c> text, if declared (LANG-04).</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Station flags from a <c>station … &lt;flags&gt;</c> command (entrance, continuation, sink,
    /// spring, doline, dig, arch, overhang, …). Feeds entrance/leads features (LANG-06).
    /// </summary>
    public ImmutableArray<string> Flags { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The <c>mark</c> type assigned to this station (fixed / painted / temporary), if any.</summary>
    public string? MarkType { get; init; }

    /// <summary>Fixed coordinates from a <c>fix</c> command (DATA-06), in the active input <see cref="Cs"/>.</summary>
    public double? FixX { get; init; }
    public double? FixY { get; init; }
    public double? FixZ { get; init; }

    /// <summary>The coordinate system in force when this station was fixed (<c>cs</c>), if any.</summary>
    public string? Cs { get; init; }

    /// <summary>True if this station carries the <c>entrance</c> flag.</summary>
    public bool IsEntrance => HasFlag("entrance");

    /// <summary>True if this station carries the <c>continuation</c> flag (an unexplored lead).</summary>
    public bool IsContinuation => HasFlag("continuation");

    private bool HasFlag(string flag)
    {
        foreach (var f in Flags)
            if (string.Equals(f, flag, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>A survey symbol (hierarchical scope).</summary>
public sealed record SurveySymbol(
    QualifiedName Name,
    SourceSpan DeclarationSpan,
    QualifiedName? Parent,
    ImmutableArray<QualifiedName> Children)
{
    /// <summary>The survey's <c>-title "..."</c>, if declared.</summary>
    public string? Title { get; init; }

    /// <summary>Team member names from <c>team</c> commands directly in this survey (DATA-05).</summary>
    public ImmutableArray<string> Team { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Dates from <c>date</c> commands directly in this survey (DATA-05/08; raw text).</summary>
    public ImmutableArray<string> Dates { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>An <c>equate</c> relationship: the stations declared equivalent and its source span (DATA-03).</summary>
public sealed record EquateRecord(
    ImmutableArray<string> Stations,
    SourceSpan Span);

/// <summary>
/// A single <c>equate</c> station reference that the per-file binder could not resolve from this
/// file alone (e.g. a cross-file or <c>@</c>-qualified target). Re-validated at the workspace level,
/// which has cross-file + <c>@</c> visibility; only then is TH_SEM_001 emitted. <see cref="Hint"/> is
/// a "did you mean" suggestion computed against this file's stations (used for the standalone case).
/// </summary>
public sealed record EquateRef(string Raw, SourceSpan Span, string? Hint = null);

/// <summary>
/// A summary of a <c>.th2</c> drawing object (point/line/area) for the Object Browser (DATA-03):
/// its kind, type[:subtype], enclosing scrap and source location.
/// </summary>
public sealed record Th2ObjectRecord(
    string Kind,      // "point" | "line" | "area"
    string Type,
    string ScrapId,
    SourceSpan Span);

/// <summary>
/// Centreline shot flags (Therion <c>flags surface|duplicate|splay|approximate</c>).
/// Stateful in source: a <c>flags</c> command toggles these for subsequent shots until
/// changed by another <c>flags [not] ...</c>.
/// </summary>
[System.Flags]
public enum ShotFlags
{
    None        = 0,
    Surface     = 1 << 0,
    Duplicate   = 1 << 1,
    Splay       = 1 << 2,
    Approximate = 1 << 3,
}

/// <summary>A "shot" leg between two stations (from one data row).</summary>
public sealed record ShotSymbol(
    QualifiedName From,
    QualifiedName To,
    double? Length,
    double? Compass,
    double? Clino,
    SourceSpan Span)
{
    /// <summary>Effective flags in force at this shot (folded from preceding <c>flags</c> commands).</summary>
    public ShotFlags Flags { get; init; }

    /// <summary>
    /// Comment attached to the shot: the inline <c># ...</c> after the values and/or the
    /// <c># ...</c> line directly above it, joined with " | ". Null when none.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// AST node this shot was lowered from � required to round-trip inline edits
    /// through <see cref="IModelEditService.ReplaceNode"/> (Plan �7.3 / M6 #8).
    /// Null on Empty/synthetic shots.
    /// </summary>
    public DataRow? SourceRow { get; init; }

    /// <summary>
    /// The <c>data</c> declaration that defines column positions for <see cref="SourceRow"/>.
    /// Used to map edited fields (length/compass/clino) back to their slot.
    /// </summary>
    public DataCommand? FieldDefinition { get; init; }
}

/// <summary>A <c>scrap</c> symbol (.th2): id + declaration span (Plan �7.3 / M6 #7).</summary>
public sealed record ScrapSymbol(
    string Id,
    SourceSpan DeclarationSpan);

/// <summary>A <c>map &lt;id&gt;</c> declaration (.th). Referenced cross-file as <c>map@survey</c>.</summary>
public sealed record MapSymbol(
    string Id,
    SourceSpan DeclarationSpan)
{
    /// <summary>The map's <c>-title "..."</c>, if declared.</summary>
    public string? Title { get; init; }

    /// <summary>The map's <c>-projection</c> (plan / extended / elevation / none), if declared.</summary>
    public string? Projection { get; init; }

    /// <summary>Ids of the scraps / sub-maps composed by this map's body (LANG-08).</summary>
    public ImmutableArray<string> Members { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// A point/line object id inside a <c>.th2</c> scrap (<c>-id name</c>). The target of a
/// <c>join</c>. <see cref="ScrapId"/> records the enclosing scrap (may be empty).
/// </summary>
public sealed record ScrapObjectSymbol(
    string Id,
    SourceSpan DeclarationSpan,
    string ScrapId);
