// Span-precise symbol occurrence index — the substrate for rename, find-all-references,
// highlight-occurrences, semantic highlighting, etc. See .claude/symbol-occurrence-index-design.md.
//
// Built as a byproduct of SemanticBinder's walk: every resolved identifier token is recorded as a
// SymbolOccurrence (token-precise span + logical SymbolId + role). Two views: At(offset) maps a
// caret to its symbol; Of(symbol) returns every occurrence of a symbol (rename / find-refs).

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;

namespace Therion.Semantics;

/// <summary>The kind of logical symbol an occurrence binds to.</summary>
public enum SymbolKind { Station, Survey, Scrap, Map, PointObject, LineObject, AreaObject, File, Xvi }

/// <summary>Whether an occurrence declares the symbol or references it.</summary>
public enum OccurrenceRole { Declaration, Reference }

/// <summary>Whether the occurrence's symbol was resolved per-file or is a cross-file <c>@</c> ref
/// still to be finalized at the workspace tier.</summary>
public enum ResolutionStatus { Resolved, UnresolvedCrossFile }

/// <summary>Stable identity of a logical symbol (kind + qualified name).</summary>
public readonly record struct SymbolId(SymbolKind Kind, QualifiedName Name)
{
    public override string ToString() => $"{Kind}:{Name}";
}

/// <summary>Decomposes a <c>point@survey.path</c> token into per-survey-component occurrences.</summary>
public static class SurveyRefDecomposer
{
    /// <summary>
    /// Yields each survey component's identity + token sub-span in a <c>point@inner.outer</c> ref,
    /// using the resolved station QN (top-down, last part = point) to name each component. Source
    /// components after <c>@</c> run innermost→outermost; a component is yielded only when its text
    /// matches the resolved survey path, so a rename never rewrites the wrong survey.
    /// </summary>
    public static IEnumerable<(SymbolId Survey, SourceSpan Span)> Decompose(
        string raw, SourceSpan fullSpan, QualifiedName stationQn)
    {
        int at = raw.IndexOf('@');
        if (at < 0 || at + 1 >= raw.Length) yield break;
        var comps = raw[(at + 1)..].Split('.');   // innermost survey first

        QualifiedName? survey = stationQn.HasParent ? stationQn.Parent() : null;
        int pos = 0;
        foreach (var comp in comps)
        {
            if (survey is not { } s) yield break;   // ran out of survey path
            if (string.Equals(comp, s.Last, System.StringComparison.Ordinal))
            {
                int startOffset = fullSpan.StartOffset + at + 1 + pos;
                int startCol = fullSpan.Start.Column + at + 1 + pos;
                yield return (
                    new SymbolId(SymbolKind.Survey, s),
                    new SourceSpan(fullSpan.FilePath,
                        new SourceLocation(fullSpan.Start.Line, startCol),
                        new SourceLocation(fullSpan.Start.Line, startCol + comp.Length),
                        startOffset, comp.Length));
            }
            survey = s.HasParent ? s.Parent() : null;
            pos += comp.Length + 1;
        }
    }
}

/// <summary>Helpers for turning a station reference token into a rename-ready span.</summary>
public static class StationTokenSpans
{
    /// <summary>
    /// Narrows a station token span to the point-name part — the text before <c>@</c> (survey path)
    /// or <c>:</c> (join mark) — so a rename rewrites only the name, not the qualifier.
    /// </summary>
    public static SourceSpan NarrowToPoint(SourceSpan span, string raw)
    {
        int cut = raw.Length;
        int at = raw.IndexOf('@'); if (at >= 0 && at < cut) cut = at;
        int colon = raw.IndexOf(':'); if (colon >= 0 && colon < cut) cut = colon;
        if (cut <= 0 || cut >= span.Length) return span;
        return new SourceSpan(span.FilePath, span.Start,
            new SourceLocation(span.Start.Line, span.Start.Column + cut), span.StartOffset, cut);
    }
}

/// <summary>One identifier token bound to a symbol. <see cref="Span"/> is token-precise and, for a
/// <c>point@survey</c> token, is narrowed to the <b>point</b> sub-span (so rename rewrites only the
/// name).</summary>
public readonly record struct SymbolOccurrence(
    SourceSpan Span,
    SymbolId Symbol,
    OccurrenceRole Role,
    ResolutionStatus Status = ResolutionStatus.Resolved);

/// <summary>
/// Per-file (or workspace-merged) index of symbol occurrences. Additive to <see cref="SemanticModel"/>.
/// </summary>
public sealed class OccurrenceIndex
{
    private readonly ImmutableArray<SymbolOccurrence> _byOffset;   // sorted by Span.StartOffset
    private readonly FrozenDictionary<SymbolId, ImmutableArray<SymbolOccurrence>> _bySymbol;

    public static OccurrenceIndex Empty { get; } =
        new(System.Array.Empty<SymbolOccurrence>());

    public OccurrenceIndex(IEnumerable<SymbolOccurrence> occurrences)
    {
        _byOffset = occurrences.OrderBy(o => o.Span.StartOffset).ToImmutableArray();
        _bySymbol = _byOffset
            .GroupBy(o => o.Symbol)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());
    }

    /// <summary>All occurrences of <paramref name="symbol"/> (declaration + references), stable order.</summary>
    public ImmutableArray<SymbolOccurrence> Of(SymbolId symbol) =>
        _bySymbol.TryGetValue(symbol, out var list) ? list : ImmutableArray<SymbolOccurrence>.Empty;

    /// <summary>The occurrence whose token span contains <paramref name="offset"/>, or null.</summary>
    public SymbolOccurrence? At(int offset)
    {
        int lo = 0, hi = _byOffset.Length - 1, found = -1;
        while (lo <= hi)   // largest StartOffset <= offset
        {
            int mid = (lo + hi) / 2;
            if (_byOffset[mid].Span.StartOffset <= offset) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (found < 0) return null;
        var o = _byOffset[found];
        return offset < o.Span.StartOffset + o.Span.Length ? o : null;
    }

    /// <summary>Every distinct symbol that has at least one occurrence.</summary>
    public IEnumerable<SymbolId> Symbols => _bySymbol.Keys;

    public ImmutableArray<SymbolOccurrence> All => _byOffset;
}
