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
