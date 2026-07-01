// Implementation Plan �3, �4.3 � .th2-specific AST.
// Granular: each shape is its own typed record; unknown options preserved as raw
// trivia so the lenient mode keeps as much information as possible.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary><c>scrap &lt;id&gt; ... endscrap</c> block.</summary>
public sealed record ScrapBlock(
    SourceSpan Span,
    string Id,
    string OptionsRaw,
    ImmutableArray<SketchReference> Sketches,
    ImmutableArray<TherionNode> Children,
    bool IsTerminated) : BlockCommand(Span, "scrap", Children, IsTerminated);

/// <summary>
/// A reference from a <c>.th2</c> scrap to an <c>.xvi</c> background sketch
/// (<c>-sketch &lt;xvi-path&gt; &lt;x&gt; &lt;y&gt;</c>).
/// </summary>
public sealed record SketchReference(
    SourceSpan Span,
    string XviPath,
    double X,
    double Y) : TherionNode(Span);

/// <summary><c>point &lt;x&gt; &lt;y&gt; &lt;type&gt; [options...]</c>.</summary>
public sealed record PointObject(
    SourceSpan Span,
    double X,
    double Y,
    string PointType,
    string OptionsRaw) : TherionNode(Span)
{
    /// <summary>The parsed <c>-option value</c> set (typed accessors via <see cref="Th2OptionList"/>).</summary>
    public Th2OptionList Options { get; init; } = Th2OptionList.Empty;

    /// <summary>Type without any inline <c>:subtype</c> (e.g. <c>station</c> from <c>station:fixed</c>).</summary>
    public string BaseType => Th2Symbols.SplitType(PointType).Base;

    /// <summary>Inline subtype written as <c>type:subtype</c>, if any.</summary>
    public string? InlineSubtype => Th2Symbols.SplitType(PointType).Subtype;

    /// <summary>Effective subtype: inline <c>type:subtype</c> wins over the <c>-subtype</c> option.</summary>
    public string? Subtype => InlineSubtype ?? Options.Subtype;

    /// <summary>The object's <c>-id</c>, if any.</summary>
    public string? Id => Options.Id;
}

/// <summary>One vertex inside a <see cref="LineObject"/>; line-point options are parsed too.</summary>
public readonly record struct LineVertex(SourceSpan Span, double X, double Y, string OptionsRaw)
{
    /// <summary>Parsed line-point options (<c>-subtype</c>, <c>-mark</c>, <c>-smooth</c>, …).</summary>
    public Th2OptionList Options { get; init; } = Th2OptionList.Empty;
}

/// <summary><c>line &lt;type&gt; ... endline</c> block.</summary>
public sealed record LineObject(
    SourceSpan Span,
    string LineType,
    string OptionsRaw,
    ImmutableArray<LineVertex> Vertices,
    bool IsTerminated) : TherionNode(Span)
{
    public Th2OptionList Options { get; init; } = Th2OptionList.Empty;
    public string BaseType => Th2Symbols.SplitType(LineType).Base;
    public string? InlineSubtype => Th2Symbols.SplitType(LineType).Subtype;
    public string? Subtype => InlineSubtype ?? Options.Subtype;
    public string? Id => Options.Id;
    public bool? Reverse => Options.Reverse;
    public string? Outline => Options.Outline;
    public string? Close => Options.Close;
}

/// <summary><c>area &lt;type&gt; ... endarea</c> block. Body lists border lines.</summary>
public sealed record AreaObject(
    SourceSpan Span,
    string AreaType,
    string OptionsRaw,
    ImmutableArray<string> BorderLineIds,
    bool IsTerminated) : TherionNode(Span)
{
    public Th2OptionList Options { get; init; } = Th2OptionList.Empty;
    public string BaseType => Th2Symbols.SplitType(AreaType).Base;
    public string? Id => Options.Id;
}
