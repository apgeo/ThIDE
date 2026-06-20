// Implementation Plan §5 / §7.3 (M6 follow-up #7) — workspace-aware navigation.
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

    public WorkspaceSymbolNavigationService(WorkspaceSemanticModel workspace, SemanticModel? activeFile = null)
    {
        _workspace = workspace;
        _activeFile = activeFile;
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
