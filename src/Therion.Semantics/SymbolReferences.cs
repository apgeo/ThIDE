// "Find all references" — the read-only half of true rename. Both start from the same place: turn a
// clicked token into a symbol identity, then ask the occurrence index for that symbol's spans
// (scope-correct, @-aware, comment-free, cross-file). Rename rewrites them; this just lists them.
//
// Pure and UI-free, so the CLI, the LSP server and the editor can all use it.
// See SymbolRenamePlan and .claude/symbol-occurrence-index-design.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Semantics;

/// <summary>One place a symbol is written: its declaration, or a reference to it.</summary>
public readonly record struct SymbolReference(SourceSpan Span, bool IsDeclaration);

public static class SymbolReferences
{
    /// <summary>
    /// Turns a declaration span (as returned by go-to-definition) into the symbol's identity and its
    /// current name token. <see cref="ReferenceKind.Any"/> — what a plain station/survey token arrives
    /// as — tries station first, then survey. Null for a scrap/map or an unresolved token.
    /// </summary>
    public static (SymbolId Symbol, string Name)? ResolveDeclaration(
        WorkspaceSemanticModel workspace, ReferenceKind kind, SourceSpan declarationSpan)
    {
        if (WantsStation(kind) && workspace.FindStationByDeclaration(declarationSpan) is { } station)
            return (new SymbolId(SymbolKind.Station, station.Name), station.Name.Last);
        if (WantsSurvey(kind) && workspace.FindSurveyByDeclaration(declarationSpan) is { } survey)
            return (new SymbolId(SymbolKind.Survey, survey.Name), survey.Name.Last);
        return null;
    }

    /// <summary>
    /// Every occurrence of <paramref name="symbol"/> across the workspace, declaration included,
    /// ordered by file then position. Empty when the symbol has no occurrences.
    /// </summary>
    public static IReadOnlyList<SymbolReference> Find(WorkspaceSemanticModel workspace, SymbolId symbol)
    {
        var spans = workspace.FindOccurrences(symbol)
            .Select(o => o.Span)
            .Distinct()
            .OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StartOffset)
            .ToList();

        // OccurrenceRole can't tell us which one declares the symbol: a station introduced by a shot
        // row is a Reference like every other mention. A symbol's DeclarationSpan is the *node* that
        // declares it (the whole `survey … endsurvey` block, or the data row), so the declaring
        // occurrence is the first name token sitting inside that node.
        var declaration = DeclarationSpanOf(workspace, symbol);
        var declIndex = declaration is { } d ? spans.FindIndex(s => Contains(d, s)) : -1;

        var result = new List<SymbolReference>(spans.Count);
        for (var i = 0; i < spans.Count; i++) result.Add(new SymbolReference(spans[i], i == declIndex));
        return result;
    }

    private static SourceSpan? DeclarationSpanOf(WorkspaceSemanticModel workspace, SymbolId symbol) => symbol.Kind switch
    {
        SymbolKind.Station when workspace.StationsByQn.TryGetValue(symbol.Name.ToString(), out var st)
            => st.DeclarationSpan,
        SymbolKind.Survey when workspace.SurveysByFullName.TryGetValue(symbol.Name.ToString(), out var sv)
            => sv.DeclarationSpan,
        _ => null,
    };

    private static bool Contains(SourceSpan outer, SourceSpan inner) =>
        string.Equals(outer.FilePath, inner.FilePath, StringComparison.OrdinalIgnoreCase) &&
        inner.StartOffset >= outer.StartOffset &&
        inner.StartOffset + inner.Length <= outer.StartOffset + outer.Length;

    /// <summary>
    /// Resolves <paramref name="raw"/> (a station/survey token as written, e.g. <c>p1@upper.cave</c>)
    /// against the workspace and returns every occurrence of the symbol it denotes. Empty when the
    /// token names nothing, or names something without an occurrence index (a scrap or a map).
    /// </summary>
    public static IReadOnlyList<SymbolReference> FindAll(
        WorkspaceSemanticModel workspace, string raw, ReferenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<SymbolReference>();

        var target = new WorkspaceSymbolNavigationService(workspace).GoToDefinition(raw, kind);
        if (target is not { IsEmpty: false } declaration) return Array.Empty<SymbolReference>();
        if (ResolveDeclaration(workspace, kind, declaration) is not { } resolved)
            return Array.Empty<SymbolReference>();

        return Find(workspace, resolved.Symbol);
    }

    // A plain token arrives as Any: it may be either, so both lookups must be tried.
    private static bool WantsStation(ReferenceKind k) => k is ReferenceKind.Station or ReferenceKind.Any;
    private static bool WantsSurvey(ReferenceKind k) => k is ReferenceKind.Survey or ReferenceKind.Any;
}
