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
    string? TrailingComment = null) : TherionNode(Span);

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
