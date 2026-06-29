// Implementation Plan �4.3 � .th-specific AST nodes (M2).
// Granular: every option / parameter is a typed property; raw remainder kept for round-trip.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Common base for block-structured Therion commands (<c>survey ... endsurvey</c>,
/// <c>centreline ... endcentreline</c>). Holds the inner children + a flag
/// indicating whether the closing terminator was actually present.
/// </summary>
public abstract record BlockCommand(
    SourceSpan Span,
    string Keyword,
    ImmutableArray<TherionNode> Children,
    bool IsTerminated) : TherionCommand(Span, Keyword);

/// <summary><c>survey &lt;name&gt; [-title "..."] ...</c> block.</summary>
public sealed record SurveyCommand(
    SourceSpan Span,
    string Name,
    string OptionsRaw,
    ImmutableArray<TherionNode> Children,
    bool IsTerminated) : BlockCommand(Span, "survey", Children, IsTerminated);

/// <summary><c>centreline</c> / <c>centerline</c> block.</summary>
public sealed record CentrelineCommand(
    SourceSpan Span,
    string OptionsRaw,
    ImmutableArray<TherionNode> Children,
    bool IsTerminated) : BlockCommand(Span, "centreline", Children, IsTerminated);

/// <summary>
/// <c>group ... endgroup</c> � a scoping block that groups settings/commands. It is
/// context-transparent: its children are parsed with the surrounding block's context,
/// so e.g. shot rows in a group inside a centreline are still recognized as data.
/// </summary>
public sealed record GroupCommand(
    SourceSpan Span,
    ImmutableArray<TherionNode> Children,
    bool IsTerminated) : BlockCommand(Span, "group", Children, IsTerminated);

/// <summary>
/// <c>surface [-options] ... endsurface</c> � a terrain/DEM block (a <c>grid</c> header
/// plus a list of elevation values, or bitmap references). The body is not Therion
/// command syntax and can be huge, so the parser consumes it opaquely.
/// </summary>
public sealed record SurfaceCommand(
    SourceSpan Span,
    string OptionsRaw,
    bool IsTerminated) : TherionCommand(Span, "surface");

/// <summary>
/// <c>scan [-options] ... endscan</c> — a survey-level block that attaches scanned drawings /
/// 3D scan files (e.g. <c>file scan.stl</c>). thbook §"scan". The body is reference data, not
/// Therion command syntax, so the parser consumes it opaquely (like <see cref="SurfaceCommand"/>).
/// </summary>
public sealed record ScanCommand(
    SourceSpan Span,
    string OptionsRaw,
    bool IsTerminated) : TherionCommand(Span, "scan");

/// <summary>
/// <c>data &lt;style&gt; &lt;field-list&gt;</c> declaration � defines the columns
/// for subsequent <see cref="DataRow"/> entries.
/// </summary>
public sealed record DataCommand(
    SourceSpan Span,
    string Style,
    ImmutableArray<string> Fields) : TherionCommand(Span, "data");

/// <summary>
/// A single shot row inside a <see cref="CentrelineCommand"/> following a <see cref="DataCommand"/>.
/// <see cref="LeadingComment"/> is the <c># ...</c> line immediately above the row;
/// <see cref="TrailingComment"/> is an inline <c># ...</c> after the values. Both are kept
/// (without the leading <c>#</c>) so the Measurements view can surface them.
/// </summary>
public sealed record DataRow(
    SourceSpan Span,
    ImmutableArray<string> Values,
    string? LeadingComment = null,
    string? TrailingComment = null) : TherionNode(Span)
{
    /// <summary>
    /// Source span of each value in <see cref="Values"/> (parallel to it). Lets the binder point a
    /// diagnostic at the exact offending column. Empty when spans weren't captured.
    /// </summary>
    public ImmutableArray<SourceSpan> ValueSpans { get; init; } = ImmutableArray<SourceSpan>.Empty;
}

/// <summary>
/// <c>flags [not] surface|duplicate|splay|approximate ...</c> inside a centreline.
/// Toggles which flags apply to subsequent <see cref="DataRow"/>s. <see cref="Tokens"/>
/// holds the raw words after <c>flags</c> (including any <c>not</c>) so the binder can
/// fold them into the active flag state.
/// </summary>
public sealed record FlagsCommand(
    SourceSpan Span,
    ImmutableArray<string> Tokens) : TherionCommand(Span, "flags");

/// <summary><c>fix &lt;station&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; [stdev...]</c>.</summary>
public sealed record StationFix(
    SourceSpan Span,
    string Station,
    double X,
    double Y,
    double Z,
    string OptionsRaw) : TherionCommand(Span, "fix");

/// <summary><c>equate &lt;station&gt; &lt;station&gt; [...]</c>.</summary>
public sealed record EquateCommand(
    SourceSpan Span,
    ImmutableArray<string> Stations) : TherionCommand(Span, "equate");

/// <summary><c>input &lt;path&gt;</c> (in <c>.th</c>) � pulls another source file in.</summary>
public sealed record InputCommand(
    SourceSpan Span,
    string Path) : TherionCommand(Span, "input");

/// <summary><c>team "name" [role ...]</c> attribution row.</summary>
public sealed record TeamCommand(
    SourceSpan Span,
    string Name,
    string OptionsRaw) : TherionCommand(Span, "team");

/// <summary><c>date &lt;yyyy.mm.dd&gt; [-to ...]</c>.</summary>
public sealed record DateCommand(
    SourceSpan Span,
    string Value) : TherionCommand(Span, "date");

/// <summary>
/// <c>join &lt;obj1&gt; &lt;obj2&gt; [...] [-options]</c> � connects scrap point/line objects
/// (or whole scraps) by id. <see cref="Targets"/> are the non-option id tokens (each may use
/// Therion's <c>id[:mark][@scrap]</c> notation); trailing <c>-option value</c> pairs are kept raw.
/// </summary>
public sealed record JoinCommand(
    SourceSpan Span,
    ImmutableArray<string> Targets,
    string OptionsRaw) : TherionCommand(Span, "join");

/// <summary>
/// <c>map &lt;id&gt; [-title "..."] ... endmap</c> block. The body lists scrap / map
/// references; it is not Therion command syntax, so the parser consumes it opaquely
/// rather than sub-parsing it. Only the map <see cref="Id"/> is a declaration the
/// symbol model needs (references into the body are resolved from raw editor text).
/// <see cref="Members"/> are the resolved id references on the body lines (LANG-08).
/// </summary>
public sealed record MapCommand(
    SourceSpan Span,
    string Id,
    string OptionsRaw,
    bool IsTerminated) : TherionCommand(Span, "map")
{
    /// <summary>Scrap / sub-map ids referenced in the map body, each with its source span (LANG-08).</summary>
    public ImmutableArray<MapMemberRef> Members { get; init; } = ImmutableArray<MapMemberRef>.Empty;

    /// <summary>The <c>-projection</c> value (plan / extended / elevation / none), if declared.</summary>
    public string? Projection { get; init; }
}

/// <summary>A scrap or sub-map reference inside a <c>map ... endmap</c> body (LANG-08).</summary>
public readonly record struct MapMemberRef(SourceSpan Span, string Id);

// =====================================================================================
// Centreline / survey metadata commands (LANG-04, LANG-05, LANG-03, LANG-06). thbook
// v6.4.0 §"centreline" pp.17-22. These previously fell through to UnknownCommand or, worse,
// were mis-parsed as DataRows inside a centreline (polluting the station model with fake
// stations like "extend"/"mark"). Modeling them removes that pollution and feeds the units /
// declination / connectivity passes.
// =====================================================================================

/// <summary><c>units &lt;quantity list&gt; [&lt;factor&gt;] &lt;units&gt;</c>.</summary>
public sealed record UnitsCommand(
    SourceSpan Span,
    ImmutableArray<string> Quantities,
    double? Factor,
    string Unit,
    string Raw) : TherionCommand(Span, "units");

/// <summary>
/// <c>calibrate &lt;quantity list&gt; &lt;zero error&gt; [&lt;scale&gt;]</c>:
/// measured = (read − <see cref="ZeroError"/>) × <see cref="Scale"/>.
/// </summary>
public sealed record CalibrateCommand(
    SourceSpan Span,
    ImmutableArray<string> Quantities,
    double ZeroError,
    double? Scale,
    string Raw) : TherionCommand(Span, "calibrate");

/// <summary>
/// <c>declination &lt;value&gt; &lt;units&gt;</c> (or a dated list / reset form). The raw text is
/// kept verbatim; <see cref="SingleValue"/> is set only for the simple single-value form.
/// </summary>
public sealed record DeclinationCommand(
    SourceSpan Span,
    double? SingleValue,
    string? Unit,
    bool IsReset,
    string Raw) : TherionCommand(Span, "declination");

/// <summary><c>grid-angle &lt;value&gt; &lt;units&gt;</c>.</summary>
public sealed record GridAngleCommand(
    SourceSpan Span,
    double? Value,
    string? Unit) : TherionCommand(Span, "grid-angle");

/// <summary><c>sd &lt;quantity list&gt; &lt;value&gt; &lt;units&gt;</c>.</summary>
public sealed record SdCommand(
    SourceSpan Span,
    ImmutableArray<string> Quantities,
    double? Value,
    string? Unit,
    string Raw) : TherionCommand(Span, "sd");

/// <summary>
/// <c>grade &lt;grade list&gt;</c> (a reference) OR a <c>grade &lt;id&gt; [-title …] … endgrade</c>
/// block that <em>defines</em> a named grade from <c>sd</c>/<c>units</c> lines (thbook §"grade";
/// see Therion's own <c>therion-librarydata/grades.th</c>). The definition body is consumed
/// opaquely; <see cref="IsBlockDefinition"/> distinguishes the two forms.
/// </summary>
public sealed record GradeCommand(
    SourceSpan Span,
    ImmutableArray<string> Grades) : TherionCommand(Span, "grade")
{
    /// <summary>True when this is a <c>grade … endgrade</c> definition block (not a reference).</summary>
    public bool IsBlockDefinition { get; init; }
}

/// <summary><c>infer &lt;what&gt; &lt;on/off&gt;</c> (<c>plumbs</c> / <c>equates</c>).</summary>
public sealed record InferCommand(
    SourceSpan Span,
    string What,
    bool On) : TherionCommand(Span, "infer");

/// <summary>
/// <c>mark [&lt;station list&gt;] &lt;type&gt;</c> — sets station type
/// (<c>fixed</c> / <c>painted</c> / <c>temporary</c>).
/// </summary>
public sealed record MarkCommand(
    SourceSpan Span,
    ImmutableArray<string> Stations,
    string MarkType) : TherionCommand(Span, "mark");

/// <summary>
/// <c>station &lt;station&gt; &lt;comment&gt; [&lt;flags&gt;]</c> — station comment + flags
/// (<c>entrance</c>, <c>continuation</c>, <c>sink</c>, <c>spring</c>, …).
/// </summary>
public sealed record StationCommand(
    SourceSpan Span,
    string Station,
    string? Comment,
    ImmutableArray<string> Flags) : TherionCommand(Span, "station");

/// <summary><c>cs &lt;coordinate system&gt;</c> — valid in a centreline and in <c>.thconfig</c>.</summary>
public sealed record CsCommand(
    SourceSpan Span,
    string System) : TherionCommand(Span, "cs");

/// <summary>
/// <c>extend &lt;spec&gt; [&lt;station&gt; [&lt;station&gt; [&lt;station&gt;]]]</c> — extended-elevation
/// direction control. <see cref="Spec"/> is the first token (left/right/normal/reverse/vertical/
/// start/ignore/hide or a 0-200 ratio).
/// </summary>
public sealed record ExtendCommand(
    SourceSpan Span,
    string Spec,
    ImmutableArray<string> Stations) : TherionCommand(Span, "extend");

/// <summary><c>break</c> — separates interleaved-data traverses.</summary>
public sealed record BreakCommand(SourceSpan Span) : TherionCommand(Span, "break");

/// <summary><c>walls &lt;auto/on/off&gt;</c>.</summary>
public sealed record WallsCommand(SourceSpan Span, string Value) : TherionCommand(Span, "walls");

/// <summary><c>vthreshold &lt;number&gt; &lt;units&gt;</c>.</summary>
public sealed record VThresholdCommand(
    SourceSpan Span, double? Value, string? Unit) : TherionCommand(Span, "vthreshold");

/// <summary><c>station-names &lt;prefix&gt; &lt;suffix&gt;</c>.</summary>
public sealed record StationNamesCommand(
    SourceSpan Span, string Prefix, string Suffix) : TherionCommand(Span, "station-names");

/// <summary><c>instrument &lt;quantity list&gt; &lt;description&gt;</c>.</summary>
public sealed record InstrumentCommand(
    SourceSpan Span,
    ImmutableArray<string> Quantities,
    string Description) : TherionCommand(Span, "instrument");

/// <summary><c>explo-date &lt;date&gt;</c> — discovery date.</summary>
public sealed record ExploDateCommand(SourceSpan Span, string Value) : TherionCommand(Span, "explo-date");

/// <summary><c>explo-team &lt;person&gt;</c> — discovery team member.</summary>
public sealed record ExploTeamCommand(
    SourceSpan Span, string Name, string OptionsRaw) : TherionCommand(Span, "explo-team");
