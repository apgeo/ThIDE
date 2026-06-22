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
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;
        if (ResolveReferenceWithScope(reference, kind) is not null) return true;
        // The workspace snapshot only covers saved content; also check the freshly-parsed
        // active file so newly-added (unsaved) identifiers get hyperlinks (#4).
        return _activeFile is not null && _activeFile.TryResolve(reference, out _);
    }

    /// <inheritdoc/>
    public ReferenceInfo? Describe(string reference, ReferenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (_workspace.DescribeReference(reference, kind) is { } info) return info;

        // .th2 / bare-name station fallbacks (mirror ResolveReferenceWithScope).
        if (kind is ReferenceKind.Any or ReferenceKind.Station)
        {
            var r = StationRef.Parse(reference);
            if (!r.HasSurvey)
            {
                if (_activeFilePath is { } path &&
                    path.EndsWith(".th2", System.StringComparison.OrdinalIgnoreCase) &&
                    _workspace.ResolveStationInFileScope(r.Point, path) is { } s && !s.IsEmpty)
                    return new ReferenceInfo("station", s);
                if (ResolveBareStationByName(r.Point) is { } s2)
                    return new ReferenceInfo("station", s2);
            }
        }

        // Freshly-parsed active file: catch newly-added identifiers not yet in the workspace
        // snapshot (i.e. added since the last save). GoToDefinition already checks _activeFile,
        // but Describe must return the right kind label ("station" not "file") (#4).
        if (_activeFile is not null && _activeFile.TryResolve(reference, out var freshSpan) && !freshSpan.IsEmpty)
            return new ReferenceInfo("station", freshSpan);

        // Input/load/xvi file targets.
        if (GoToDefinition(reference) is { } fs && !fs.IsEmpty)
            return new ReferenceInfo("file", fs);
        return null;
    }

    /// <summary>
    /// Index-only resolution (no per-file scans): the workspace <c>@</c> resolver plus a
    /// <c>.th2</c>-scope fallback that resolves a bare station name against the survey of
    /// the <c>.th</c> that inputs the active sketch (Plan items: .th2 station navigation).
    /// </summary>
    private SourceSpan? ResolveReferenceWithScope(string reference, ReferenceKind kind)
    {
        if (_workspace.ResolveReference(reference, kind) is { } span && !span.IsEmpty)
            return span;

        if (kind is ReferenceKind.Any or ReferenceKind.Station)
        {
            var r = StationRef.Parse(reference);
            if (!r.HasSurvey)
            {
                // In a .th2 sketch, a bare station name belongs to the survey of the .th
                // that inputs it (#4/#7).
                if (_activeFilePath is { } path &&
                    path.EndsWith(".th2", System.StringComparison.OrdinalIgnoreCase) &&
                    _workspace.ResolveStationInFileScope(r.Point, path) is { } s && !s.IsEmpty)
                    return s;

                // Otherwise resolve a bare station id by its (usually-unique) name,
                // preferring a definition in the active file (#13 station command, #17).
                if (ResolveBareStationByName(r.Point) is { } byName) return byName;
            }
        }
        return null;
    }

    private SourceSpan? ResolveBareStationByName(string point)
    {
        if (!_workspace.StationsByLastName.TryGetValue(point, out var list) || list.IsEmpty)
            return null;
        if (_activeFilePath is { } path)
            foreach (var st in list)
                if (string.Equals(st.DeclarationSpan.FilePath, path, System.StringComparison.OrdinalIgnoreCase))
                    return st.DeclarationSpan;
        return list[0].DeclarationSpan;
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
