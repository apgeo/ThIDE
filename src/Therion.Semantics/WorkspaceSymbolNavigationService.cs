// Implementation Plan �5 / �7.3 (M6 follow-up #7) � workspace-aware navigation.
// Resolves go-to-definition / find-references across every per-file SemanticModel
// in a WorkspaceSemanticModel, plus XVI cross-file edges (sketch references).

using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Semantics;

/// <summary>
/// <see cref="ISymbolNavigationService"/> implementation that searches every
/// loaded file in a <see cref="WorkspaceSemanticModel"/> (M6 follow-up #7).
/// Falls back to the per-file <see cref="SemanticModel"/> when no workspace is loaded.
/// </summary>
public sealed class WorkspaceSymbolNavigationService : ISymbolNavigationService
{
    private readonly WorkspaceSemanticModel _workspace;
    private readonly SemanticModel? _activeFile;
    private readonly string? _activeFilePath;

    public WorkspaceSymbolNavigationService(
        WorkspaceSemanticModel workspace, SemanticModel? activeFile = null, string? activeFilePath = null)
    {
        _workspace = workspace;
        _activeFile = activeFile;
        _activeFilePath = activeFilePath;
    }

    /// <summary>
    /// Reference-aware go-to-definition: when the token carries Therion's <c>@</c>
    /// notation or the caller hints a specific <see cref="ReferenceKind"/>, resolve it
    /// cross-file via the workspace resolver first; then fall back to the plain
    /// qualified-name / file / xvi resolution.
    /// </summary>
    public SourceSpan? GoToDefinition(string reference, ReferenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (ResolveReferenceWithScope(reference, kind) is { } span) return span;
        return GoToDefinition(reference);
    }

    /// <inheritdoc/>
    public bool CanNavigate(string reference, ReferenceKind kind)
        => !string.IsNullOrWhiteSpace(reference) && ResolveReferenceWithScope(reference, kind) is not null;

    /// <summary>
    /// Index-only resolution (no per-file scans): the workspace <c>@</c> resolver plus a
    /// <c>.th2</c>-scope fallback that resolves a bare station name against the survey of
    /// the <c>.th</c> that inputs the active sketch (Plan items: .th2 station navigation).
    /// </summary>
    private SourceSpan? ResolveReferenceWithScope(string reference, ReferenceKind kind)
    {
        if (_workspace.ResolveReference(reference, kind) is { } span && !span.IsEmpty)
            return span;

        if ((kind is ReferenceKind.Any or ReferenceKind.Station) &&
            _activeFilePath is { } path && path.EndsWith(".th2", System.StringComparison.OrdinalIgnoreCase))
        {
            var r = StationRef.Parse(reference);
            if (!r.HasSurvey && _workspace.ResolveStationInFileScope(r.Point, path) is { } s && !s.IsEmpty)
                return s;
        }
        return null;
    }

    public SourceSpan? GoToDefinition(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName)) return null;

        // 1) Try the active file first (matches the single-file fast path).
        if (_activeFile is not null && _activeFile.TryResolve(qualifiedName, out var local))
            return local;

        // 2) Walk every per-file SemanticModel (stations, surveys, scraps).
        foreach (var model in _workspace.PerFile.Values)
        {
            if (model.TryResolve(qualifiedName, out var span)) return span;
        }

        // 3) File-source / input target: match against any loaded file path
        //    (full path or basename, case-insensitive) so `input foo.th` jumps
        //    to the top of foo.th.
        foreach (var path in _workspace.PerFile.Keys)
        {
            if (string.Equals(path, qualifiedName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), qualifiedName, System.StringComparison.OrdinalIgnoreCase))
            {
                return new SourceSpan(path, SourceLocation.Start, SourceLocation.Start, 0, 0);
            }
        }

        // 4) XVI sketch reference: match against indexed .xvi paths
        //    so clicking a sketch path in a .th2 jumps to the .xvi declaration.
        foreach (var xvi in _workspace.Xvi.ByPath.Values)
        {
            var name = Path.GetFileName(xvi.ResolvedXviPath);
            if (string.Equals(name, qualifiedName, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(xvi.ResolvedXviPath, qualifiedName, System.StringComparison.OrdinalIgnoreCase))
            {
                return xvi.File.Span;
            }
        }

        return null;
    }

    public ImmutableArray<SourceSpan> FindReferences(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName)) return ImmutableArray<SourceSpan>.Empty;

        var builder = ImmutableArray.CreateBuilder<SourceSpan>();
        foreach (var model in _workspace.PerFile.Values)
            builder.AddRange(model.FindReferences(qualifiedName));
        return builder.ToImmutable();
    }
}
