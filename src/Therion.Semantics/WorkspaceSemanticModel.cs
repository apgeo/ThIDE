// Implementation Plan �5 / �6 � workspace-level semantic snapshot.
// Combines all per-file SemanticModels with cross-file indexes (XviIndex,
// FileGraph). Immutable; rebuilt atomically (�18 � UI threading).

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>
/// Cross-file semantic snapshot for an entire workspace.
/// Aggregates per-file <see cref="SemanticModel"/> instances and the
/// workspace-wide <see cref="XviIndex"/> + FileGraph edges.
/// </summary>
public sealed class WorkspaceSemanticModel
{
    public FrozenDictionary<string, SemanticModel> PerFile { get; }
    public XviIndex Xvi { get; }
    /// <summary>All cross-file edges (`.thconfig source`, `.th input`/`load`, `.th2 sketch`).</summary>
    public ImmutableArray<(string From, string To)> FileGraphEdges { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    // ---- cross-file reference indexes (Plan: @-notation resolution) ----------
    // Built from PerFile (.th surveys/stations/maps) and the .th2 ASTs (scrap +
    // scrap-object ids). Keyed case-sensitively (Therion names are).

    /// <summary>Surveys keyed by full top-down dotted name (e.g. <c>cave.upper</c>).</summary>
    public FrozenDictionary<string, SurveySymbol> SurveysByFullName { get; init; } =
        FrozenDictionary<string, SurveySymbol>.Empty;

    /// <summary>Surveys grouped by their last name component (survey names are effectively unique).</summary>
    public FrozenDictionary<string, ImmutableArray<SurveySymbol>> SurveysByLastName { get; init; } =
        FrozenDictionary<string, ImmutableArray<SurveySymbol>>.Empty;

    /// <summary>Stations keyed by full top-down dotted QN (first definition wins).</summary>
    public FrozenDictionary<string, StationSymbol> StationsByQn { get; init; } =
        FrozenDictionary<string, StationSymbol>.Empty;

    /// <summary>Stations keyed by <c>&lt;immediate-survey-last&gt;\0&lt;point&gt;</c> (resolves <c>point@survey</c>).</summary>
    public FrozenDictionary<string, StationSymbol> StationsBySurveyAndPoint { get; init; } =
        FrozenDictionary<string, StationSymbol>.Empty;

    /// <summary>
    /// Stations grouped by their last (point) name component. Used to resolve a bare
    /// station id (e.g. clicking <c>PCB2</c> on a <c>station</c> line, or a <c>-name</c>
    /// in a .th2) when no survey qualifier is available — station names are usually unique.
    /// </summary>
    public FrozenDictionary<string, ImmutableArray<StationSymbol>> StationsByLastName { get; init; } =
        FrozenDictionary<string, ImmutableArray<StationSymbol>>.Empty;

    /// <summary>Maps keyed by id (the <c>map</c> half of <c>map@survey</c>).</summary>
    public FrozenDictionary<string, MapSymbol> MapsById { get; init; } =
        FrozenDictionary<string, MapSymbol>.Empty;

    /// <summary>Scraps keyed by id (a <c>join</c> can target a whole scrap).</summary>
    public FrozenDictionary<string, ScrapSymbol> ScrapsById { get; init; } =
        FrozenDictionary<string, ScrapSymbol>.Empty;

    /// <summary>Scrap point/line objects keyed by their <c>-id</c> (the usual <c>join</c> target).</summary>
    public FrozenDictionary<string, ScrapObjectSymbol> ScrapObjectsById { get; init; } =
        FrozenDictionary<string, ScrapObjectSymbol>.Empty;

    public WorkspaceSemanticModel(
        FrozenDictionary<string, SemanticModel> perFile,
        XviIndex xvi,
        ImmutableArray<(string From, string To)> edges,
        ImmutableArray<Diagnostic> diagnostics)
    {
        PerFile = perFile;
        Xvi = xvi;
        FileGraphEdges = edges;
        Diagnostics = diagnostics;
    }

    public static WorkspaceSemanticModel Empty { get; } = new(
        FrozenDictionary<string, SemanticModel>.Empty,
        XviIndex.Empty,
        ImmutableArray<(string, string)>.Empty,
        ImmutableArray<Diagnostic>.Empty);

    /// <summary>
    /// Build a workspace-level snapshot from raw <see cref="ParseResult{TherionFile}"/>
    /// per file plus their parsed XVI counterparts.
    /// </summary>
    public static WorkspaceSemanticModel Build(
        IReadOnlyDictionary<string, ParseResult<TherionFile>> parsedFiles,
        IReadOnlyCollection<XviFile> xviFiles,
        System.Func<string, bool>? fileExists = null)
    {
        var binder = new SemanticBinder();
        var perFile = new Dictionary<string, SemanticModel>(System.StringComparer.OrdinalIgnoreCase);
        var allDiags = ImmutableArray.CreateBuilder<Diagnostic>();
        var graphEdges = ImmutableArray.CreateBuilder<(string, string)>();
        var th2Files = new List<TherionFile>();

        foreach (var (path, parse) in parsedFiles)
        {
            allDiags.AddRange(parse.Diagnostics);
            if (parse.Value is null) continue;

            // Collect .th2 trees for the XVI index.
            if (path.EndsWith(".th2", System.StringComparison.OrdinalIgnoreCase))
                th2Files.Add(parse.Value);

            // Bind per-file semantics (.th only � others have no station model yet).
            if (path.EndsWith(".th", System.StringComparison.OrdinalIgnoreCase))
            {
                var model = binder.Bind(parse.Value);
                perFile[path] = model;
                allDiags.AddRange(model.Diagnostics);
            }

            // Source / input edges from .thconfig and .th files.
            CollectSourceEdges(path, parse.Value, graphEdges);
        }

        var xvi = XviIndex.Build(xviFiles, th2Files, fileExists);
        allDiags.AddRange(xvi.Diagnostics);
        foreach (var edge in xvi.FileGraphEdges) graphEdges.Add(edge);

        var frozenPerFile = perFile.ToFrozenDictionary(System.StringComparer.OrdinalIgnoreCase);
        var idx = ReferenceIndexBuilder.Build(frozenPerFile.Values, th2Files);

        return new WorkspaceSemanticModel(
            frozenPerFile,
            xvi,
            graphEdges.ToImmutable(),
            allDiags.ToImmutable())
        {
            SurveysByFullName = idx.SurveysByFullName,
            SurveysByLastName = idx.SurveysByLastName,
            StationsByQn = idx.StationsByQn,
            StationsBySurveyAndPoint = idx.StationsBySurveyAndPoint,
            StationsByLastName = idx.StationsByLastName,
            MapsById = idx.MapsById,
            ScrapsById = idx.ScrapsById,
            ScrapObjectsById = idx.ScrapObjectsById,
        };
    }

    // ---- cross-file reference resolution (@-notation) ------------------------

    /// <summary>
    /// Resolves a textual reference (<c>point@survey</c>, <c>map@survey</c>, a bare
    /// survey name, or a <c>join</c> id) to its declaration span, applying Therion's
    /// <c>@</c> rule. <paramref name="kind"/> disambiguates which half of the token to
    /// chase; <see cref="ReferenceKind.Any"/> tries each kind in turn.
    /// </summary>
    public SourceSpan? ResolveReference(string raw, ReferenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var r = StationRef.Parse(raw.Trim());
        return kind switch
        {
            ReferenceKind.Survey      => ResolveSurvey(r),
            ReferenceKind.Station     => ResolveStation(r),
            ReferenceKind.Map         => ResolveMap(r),
            ReferenceKind.ScrapObject => ResolveScrapObject(r),
            _ => ResolveStation(r) ?? ResolveSurvey(r) ?? ResolveMap(r) ?? ResolveScrapObject(r),
        };
    }

    private SourceSpan? ResolveSurvey(StationRef r)
    {
        // The survey is the @-part if present, else the bare token itself.
        var path = r.HasSurvey ? r.SurveyPathTopDown : ImmutableArray.Create(r.Point);
        if (path.IsDefaultOrEmpty || string.IsNullOrEmpty(path[^1])) return null;

        if (SurveysByFullName.TryGetValue(string.Join('.', path), out var exact))
            return exact.DeclarationSpan;
        if (SurveysByLastName.TryGetValue(path[^1], out var list) && list.Length > 0)
            return list[0].DeclarationSpan;
        return null;
    }

    private SourceSpan? ResolveStation(StationRef r)
    {
        if (r.SurveyLastName is { } surveyLast &&
            StationsBySurveyAndPoint.TryGetValue(SurveyPointKey(surveyLast, r.Point), out var byKey))
            return byKey.DeclarationSpan;
        if (StationsByQn.TryGetValue(r.StationQuery, out var byQn))
            return byQn.DeclarationSpan;
        return null;
    }

    private SourceSpan? ResolveMap(StationRef r)
        => MapsById.TryGetValue(r.Point, out var m) ? m.DeclarationSpan : null;

    private SourceSpan? ResolveScrapObject(StationRef r)
    {
        var id = r.PointWithoutMark;
        if (ScrapObjectsById.TryGetValue(id, out var so)) return so.DeclarationSpan;
        if (ScrapsById.TryGetValue(id, out var sc)) return sc.DeclarationSpan;
        return null;
    }

    /// <summary>
    /// Resolves a bare station name written inside a <c>.th2</c> file (e.g. a
    /// <c>point ... station -name 14</c>) to its definition in the <c>.th</c> survey
    /// that <c>input</c>s the sketch. Walks the file graph to find the parent <c>.th</c>,
    /// then looks the station up in each of that file's surveys.
    /// </summary>
    public SourceSpan? ResolveStationInFileScope(string stationName, string sketchPath)
    {
        if (string.IsNullOrEmpty(stationName) || string.IsNullOrEmpty(sketchPath)) return null;
        var full = System.IO.Path.GetFullPath(sketchPath);

        foreach (var (from, to) in FileGraphEdges)
        {
            if (!string.Equals(to, full, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!PerFile.TryGetValue(from, out var model)) continue;
            foreach (var sv in model.Surveys.Values)
            {
                if (StationsBySurveyAndPoint.TryGetValue(
                        SurveyPointKey(sv.Name.Last, stationName), out var st))
                    return st.DeclarationSpan;
            }
        }

        // Fallback: a station with this (usually-unique) name anywhere in the project.
        if (StationsByLastName.TryGetValue(stationName, out var byName) && !byName.IsEmpty)
            return byName[0].DeclarationSpan;
        return null;
    }

    /// <summary>Composite key for <see cref="StationsBySurveyAndPoint"/> (space-joined; names contain no spaces).</summary>
    internal static string SurveyPointKey(string surveyLastName, string point)
        => string.Concat(surveyLastName, " ", point);

    private static void CollectSourceEdges(
        string parentPath, TherionFile file,
        ImmutableArray<(string, string)>.Builder edges)
    {
        foreach (var dep in SourceGraph.Dependencies(file, parentPath))
            edges.Add((parentPath, dep));
    }
}
