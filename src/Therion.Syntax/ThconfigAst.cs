// LANG-02 / LANG-03 — typed .thconfig command model. thbook v6.4.0 §"Processing data" pp.50-63.
// Therion source-of-truth: therion/src/thconfig.cxx + thlayout.cxx.
//
// `source`/`input`/`load` are deliberately *not* retyped here — they stay UnknownCommand /
// InputCommand so SourceGraph's project-traversal keeps working unchanged. These nodes cover the
// commands that drive layout (LANG-02), the output coordinate system (LANG-03), and exports.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary><c>select &lt;object&gt; [OPTIONS]</c> / <c>unselect …</c> (export selection).</summary>
public sealed record SelectCommand(
    SourceSpan Span,
    string Object,
    string OptionsRaw,
    bool IsUnselect) : TherionCommand(Span, "select");

/// <summary>
/// <c>export &lt;what&gt; [-fmt &lt;format&gt;] [-output &lt;path&gt;] …</c>. Drives BUILD-01 export presets.
/// </summary>
public sealed record ExportCommand(
    SourceSpan Span,
    string ExportType,
    string OptionsRaw) : TherionCommand(Span, "export")
{
    /// <summary>The <c>-fmt</c> value (e.g. <c>lox</c>, <c>survex</c>, <c>kml</c>), if present.</summary>
    public string? Format { get; init; }

    /// <summary>The <c>-o</c>/<c>-output</c> path, if present.</summary>
    public string? Output { get; init; }
}

/// <summary><c>maps &lt;on/off&gt;</c> and <c>maps-offset &lt;on/off&gt;</c>.</summary>
public sealed record MapsCommand(
    SourceSpan Span,
    bool On,
    bool IsOffset) : TherionCommand(Span, "maps");

/// <summary>
/// <c>layout &lt;id&gt; [OPTIONS] … endlayout</c> (LANG-02). The body's simple <c>key value</c>
/// lines are captured as <see cref="Options"/>; embedded <c>code &lt;lang&gt; … endcode</c> blocks
/// (metapost / tex / postprocess) are skipped opaquely (LANG-10) and listed in <see cref="CodeBlocks"/>.
/// </summary>
public sealed record LayoutCommand(
    SourceSpan Span,
    string Id,
    string OptionsRaw,
    ImmutableArray<LayoutOption> Options,
    ImmutableArray<LayoutCodeBlock> CodeBlocks,
    bool IsTerminated) : TherionCommand(Span, "layout")
{
    /// <summary>The <c>copy &lt;id&gt;</c> base layout this one inherits from, if any.</summary>
    public string? CopyFrom { get; init; }

    /// <summary>The layout's <c>cs &lt;system&gt;</c> (location CRS for origin/grid), if set (LANG-03).</summary>
    public string? CoordinateSystem { get; init; }

    /// <summary>The <c>symbol-set &lt;standard&gt;</c> chosen by this layout, if any (LANG-09).</summary>
    public string? SymbolSet { get; init; }

    /// <summary>The <c>symbol-hide/show/assign/colour</c> directives in this layout (LANG-09).</summary>
    public ImmutableArray<LayoutSymbolDirective> SymbolDirectives { get; init; } =
        ImmutableArray<LayoutSymbolDirective>.Empty;
}

/// <summary>One <c>key [value…]</c> line inside a <c>layout</c> body.</summary>
public readonly record struct LayoutOption(SourceSpan Span, string Key, string Value);

/// <summary>An embedded <c>code &lt;lang&gt; … endcode</c> block inside a layout (kept opaque).</summary>
public readonly record struct LayoutCodeBlock(SourceSpan Span, string Language);
