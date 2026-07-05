// TherionProc application directives — an editor/UX layer that lives INSIDE Therion
// comments (`#@…`), so it is fully forward/backward compatible with Therion syntax
// (Therion sees only a comment). Directives are the rough equivalent of preprocessor /
// pragma directives; the first consumer is the collapsible `#@region … #@endregion`
// block. A future batch adds `#@if/#@elif/#@else/#@endif` on the same model.
//
// Design goals (per request): negligible parse cost for the few directives per file,
// and minimal architectural weight — a flat directive list plus a light region pass,
// no general block framework yet.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Directives;

/// <summary>
/// One positional argument of a directive. <see cref="Value"/> is <c>null</c> when the
/// argument was written as <c>_</c>, <c>undefined</c>, or an empty slot between commas.
/// </summary>
public readonly record struct DirectiveArg(string? Value, SourceSpan Span)
{
    /// <summary>True when the argument has no defined value.</summary>
    public bool IsUndefined => Value is null;

    public static DirectiveArg Defined(string value, SourceSpan span) => new(value, span);
    public static DirectiveArg Undefined(SourceSpan span) => new(null, span);
}

/// <summary>
/// A single parsed <c>#@&lt;type&gt; &lt;arg&gt;…</c> directive line. <see cref="Type"/> is
/// lower-cased (directive types are case-insensitive); <see cref="RawType"/> preserves the
/// author's spelling for display.
/// </summary>
public sealed record TherionDirective(
    string Type,
    string RawType,
    ImmutableArray<DirectiveArg> Args,
    SourceSpan Span,
    int Line)
{
    /// <summary>Number of positional arguments (including explicit <c>undefined</c> slots).</summary>
    public int Count => Args.Length;

    /// <summary>The value of argument <paramref name="index"/>, or <c>null</c> if absent/undefined.</summary>
    public string? ArgValue(int index) =>
        index >= 0 && index < Args.Length ? Args[index].Value : null;
}

/// <summary>
/// A paired <c>#@region … #@endregion</c> block. <see cref="EndDirective"/> is <c>null</c>
/// (and <see cref="EndLine"/> is -1) when the region was never closed.
/// </summary>
public sealed record DirectiveRegion(
    string? Title,
    int StartLine,
    int EndLine,
    int StartOffset,
    int EndOffset,
    TherionDirective StartDirective,
    TherionDirective? EndDirective)
{
    /// <summary>True when the region has a matching <c>#@endregion</c> (and is foldable).</summary>
    public bool IsClosed => EndDirective is not null;
}

/// <summary>The result of scanning a source file for directives.</summary>
public sealed record DirectiveScanResult(
    ImmutableArray<TherionDirective> Directives,
    ImmutableArray<DirectiveRegion> Regions,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public static readonly DirectiveScanResult Empty = new(
        ImmutableArray<TherionDirective>.Empty,
        ImmutableArray<DirectiveRegion>.Empty,
        ImmutableArray<Diagnostic>.Empty);

    /// <summary>Closed regions only — the set the editor can fold.</summary>
    public System.Collections.Generic.IEnumerable<DirectiveRegion> FoldableRegions()
    {
        foreach (var r in Regions)
            if (r.IsClosed) yield return r;
    }
}
