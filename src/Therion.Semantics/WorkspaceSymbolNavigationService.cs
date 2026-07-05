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
        if (_workspace.StationsByLastName.TryGetValue(point, out var list) && !list.IsEmpty)
        {
            if (_activeFilePath is { } path)
                foreach (var st in list)
                    if (string.Equals(st.DeclarationSpan.FilePath, path, System.StringComparison.OrdinalIgnoreCase))
                        return st.DeclarationSpan;
            return list[0].DeclarationSpan;
        }
        // Workspace index only covers saved content; scan the active-file's fresh parse
        // for stations whose bare (last-component) name matches — catches newly-typed
        // unsaved identifiers that the workspace hasn't seen yet (#4).
        if (_activeFile is not null)
        {
            foreach (var kvp in _activeFile.Stations)
            {
                var qn = kvp.Key.ToString();
                int dot = qn.LastIndexOf('.');
                var lastName = dot >= 0 ? qn[(dot + 1)..] : qn;
                if (string.Equals(lastName, point, System.StringComparison.OrdinalIgnoreCase))
                    return kvp.Value.DeclarationSpan;
            }
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

    /// <inheritdoc/>
    public ImmutableArray<AggregationReference> FindAggregations(string reference, ReferenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(reference)) return ImmutableArray<AggregationReference>.Empty;
        var b = ImmutableArray.CreateBuilder<AggregationReference>();

        // equate commands that tie this station (or a station qualified by this survey) to others.
        if (kind is ReferenceKind.Any or ReferenceKind.Station or ReferenceKind.Survey)
            CollectEquateAggregations(reference, kind, b);

        // map commands that compose this scrap / sub-map.
        if (kind is ReferenceKind.Any or ReferenceKind.Map or ReferenceKind.ScrapObject)
            CollectMapAggregations(reference, b);

        return b.ToImmutable();
    }

    /// <summary>
    /// Finds every <c>equate</c> that references the station <paramref name="reference"/> resolves
    /// to. The target is resolved with the same scope-aware logic go-to-definition uses (bare names
    /// against the active file, <c>@</c>-qualified ones cross-file). When the caret is on the survey
    /// half of a <c>point@survey</c> token (<see cref="ReferenceKind.Survey"/>), matches equate
    /// members qualified by that survey instead.
    /// </summary>
    private void CollectEquateAggregations(
        string reference, ReferenceKind kind, ImmutableArray<AggregationReference>.Builder b)
    {
        // Resolve the identifier to a station identity (handles bare, in-survey names too).
        var targetStation = kind == ReferenceKind.Survey ? null : ResolveStationIdentity(reference);

        // Survey fallback: only when the caret is explicitly on a survey (the @-half of point@survey),
        // to avoid a bare station name spuriously matching a same-named survey.
        string? surveyLast = null;
        if (kind == ReferenceKind.Survey)
        {
            var r = StationRef.Parse(reference);
            surveyLast = r.HasSurvey ? r.SurveyLastName
                       : (string.IsNullOrEmpty(r.Point) ? reference : r.Point);
        }
        if (targetStation is null && surveyLast is null) return;

        foreach (var model in _workspace.PerFile.Values)
            foreach (var rec in model.EquateRecords)
                if (EquateReferences(rec, targetStation, surveyLast))
                    b.Add(new AggregationReference("equate", rec.Span));
    }

    /// <summary>
    /// Resolves a station reference to its <see cref="StationSymbol"/> identity: a direct index
    /// lookup for <c>@</c>-qualified / fully-qualified names, else a bare last-name match preferring
    /// the active file. Unlike a declaration-span round-trip this is unambiguous when two stations
    /// share a data-row span (both the <c>from</c> and <c>to</c> of one shot).
    /// </summary>
    private StationSymbol? ResolveStationIdentity(string reference)
    {
        if (_workspace.ResolveStationSymbol(reference) is { } direct) return direct;

        var r = StationRef.Parse(reference);
        if (!r.HasSurvey && !string.IsNullOrEmpty(r.Point) &&
            _workspace.StationsByLastName.TryGetValue(r.Point, out var list) && !list.IsEmpty)
        {
            if (_activeFilePath is { } path)
                foreach (var st in list)
                    if (string.Equals(st.DeclarationSpan.FilePath, path, System.StringComparison.OrdinalIgnoreCase))
                        return st;
            return list[0];
        }
        return null;
    }

    /// <summary>True when the equate <paramref name="rec"/> ties in the target station / survey.</summary>
    private bool EquateReferences(EquateRecord rec, StationSymbol? target, string? surveyLast)
    {
        foreach (var memberRaw in rec.Stations)
        {
            // (a) an @-qualified / globally-resolvable member that is the same station.
            if (target is not null &&
                _workspace.ResolveStationSymbol(memberRaw) is { } s && s.Name.Equals(target.Name))
                return true;

            // (c) survey half of point@survey: a member qualified by that survey.
            if (surveyLast is not null)
            {
                var mr = StationRef.Parse(memberRaw);
                if (mr.HasSurvey &&
                    string.Equals(mr.SurveyLastName, surveyLast, System.StringComparison.Ordinal))
                    return true;
            }
        }

        // (b) a bare in-survey member the per-file binder already resolved to this station: one of
        // the target's recorded reference spans falls inside this equate command's span. (Bare names
        // can't reference cross-file, so this same-file check is exact, not a heuristic.)
        if (target is not null && !target.References.IsDefaultOrEmpty)
            foreach (var refSpan in target.References)
                if (string.Equals(refSpan.FilePath, rec.Span.FilePath, System.StringComparison.OrdinalIgnoreCase) &&
                    refSpan.StartOffset >= rec.Span.StartOffset &&
                    refSpan.StartOffset < rec.Span.StartOffset + rec.Span.Length)
                    return true;

        return false;
    }

    /// <summary>Finds every <c>map</c> whose body composes the scrap / sub-map <paramref name="reference"/>.</summary>
    private void CollectMapAggregations(string reference, ImmutableArray<AggregationReference>.Builder b)
    {
        // The map/scrap id is the point half (strip any @survey qualifier).
        var parsed = StationRef.Parse(reference);
        var id = string.IsNullOrEmpty(parsed.Point) ? reference : parsed.Point;

        foreach (var model in _workspace.PerFile.Values)
            foreach (var map in model.Maps.Values)
                if (!map.Members.IsDefaultOrEmpty &&
                    map.Members.Contains(id, System.StringComparer.Ordinal))
                    b.Add(new AggregationReference("map", map.DeclarationSpan));
    }
}
