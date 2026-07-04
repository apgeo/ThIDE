// Implementation Plan �5 / �6 � workspace-level semantic snapshot.
// Combines all per-file SemanticModels with cross-file indexes (XviIndex,
// FileGraph). Immutable; rebuilt atomically (�18 � UI threading).

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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

    /// <summary>All <c>.th2</c> point/line/area objects (for the Object Browser).</summary>
    public ImmutableArray<Th2ObjectRecord> Th2Objects { get; init; } = ImmutableArray<Th2ObjectRecord>.Empty;

    /// <summary>
    /// Workspace-wide token-level station occurrences (rename / find-refs substrate): every per-file
    /// <see cref="SemanticModel.Occurrences"/> merged, plus cross-file <c>@</c>-equate references
    /// (unresolved per-file) finalized to their declaring station. Keyed by the station's
    /// <see cref="SymbolId"/>. See <c>.claude/symbol-occurrence-index-design.md</c>.
    /// </summary>
    public FrozenDictionary<SymbolId, ImmutableArray<SymbolOccurrence>> StationOccurrences { get; init; } =
        FrozenDictionary<SymbolId, ImmutableArray<SymbolOccurrence>>.Empty;

    /// <summary>Every occurrence of <paramref name="symbol"/> across the workspace (stable order).</summary>
    public ImmutableArray<SymbolOccurrence> FindOccurrences(SymbolId symbol) =>
        StationOccurrences.TryGetValue(symbol, out var list) ? list : ImmutableArray<SymbolOccurrence>.Empty;

    /// <summary>
    /// The <see cref="StationSymbol"/> declared at <paramref name="declarationSpan"/> (as returned by
    /// go-to-definition), or null. Used by rename to turn a clicked token into a symbol identity.
    /// </summary>
    public StationSymbol? FindStationByDeclaration(SourceSpan declarationSpan)
    {
        if (declarationSpan.IsEmpty) return null;
        bool Match(StationSymbol s) =>
            s.DeclarationSpan.StartOffset == declarationSpan.StartOffset &&
            string.Equals(s.DeclarationSpan.FilePath, declarationSpan.FilePath, System.StringComparison.OrdinalIgnoreCase);

        if (PerFile.TryGetValue(declarationSpan.FilePath, out var m))
            foreach (var s in m.Stations.Values)
                if (Match(s)) return s;
        // FilePath may differ in case/normalization — fall back to a full scan.
        foreach (var model in PerFile.Values)
            foreach (var s in model.Stations.Values)
                if (Match(s)) return s;
        return null;
    }

    /// <summary>The <see cref="SurveySymbol"/> declared at <paramref name="declarationSpan"/>, or null.</summary>
    public SurveySymbol? FindSurveyByDeclaration(SourceSpan declarationSpan)
    {
        if (declarationSpan.IsEmpty) return null;
        foreach (var model in PerFile.Values)
            foreach (var sv in model.Surveys.Values)
                if (sv.DeclarationSpan.StartOffset == declarationSpan.StartOffset &&
                    string.Equals(sv.DeclarationSpan.FilePath, declarationSpan.FilePath, System.StringComparison.OrdinalIgnoreCase))
                    return sv;
        return null;
    }

    /// <summary>Resolves a station reference to its declaring <see cref="StationSymbol"/> (its identity).</summary>
    public StationSymbol? ResolveStationSymbol(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var r = StationRef.Parse(raw.Trim());
        if (r.SurveyLastName is { } sl &&
            StationsBySurveyAndPoint.TryGetValue(SurveyPointKey(sl, r.Point), out var byKey)) return byKey;
        return StationsByQn.TryGetValue(r.StationQuery, out var byQn) ? byQn : null;
    }

    /// <summary>
    /// The stations declared the same physical point as <paramref name="target"/> by an <c>equate</c>
    /// command — following equate links across surveys and files — that also share its last name but
    /// live in a different survey (e.g. <c>b.1</c> for <c>a.1</c> given <c>equate 1@a 1@b</c>). Powers
    /// rename's opt-in "also rename equate-linked same-named stations". Empty when none.
    /// </summary>
    public ImmutableArray<QualifiedName> EquatedSameNameStations(QualifiedName target)
    {
        var eq = new EquateGraph();
        foreach (var model in PerFile.Values)
            foreach (var rec in model.EquateRecords)
            {
                QualifiedName? first = null;
                foreach (var raw in rec.Stations)
                {
                    if (ResolveStationSymbol(raw) is not { } s) continue;
                    eq.Add(s.Name);
                    if (first is null) first = s.Name; else eq.Union(first.Value, s.Name);
                }
            }

        foreach (var group in eq.Groups())
        {
            if (!group.Contains(target)) continue;
            var b = ImmutableArray.CreateBuilder<QualifiedName>();
            foreach (var qn in group)
                if (!qn.Equals(target) && string.Equals(qn.Last, target.Last, System.StringComparison.Ordinal))
                    b.Add(qn);
            return b.ToImmutable();
        }
        return ImmutableArray<QualifiedName>.Empty;
    }

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

    // Per-file bind cache (Group G1). Binding is a pure function of a file's parse tree, and a file
    // that hasn't been re-parsed keeps the same ParseResult instance — so we key the cache on that
    // instance's identity and reuse its SemanticModel across workspace rebuilds instead of re-binding
    // every file on every change. A re-parse yields a new ParseResult (cache miss → re-bind); dropped
    // files evict automatically (weak keys). ConditionalWeakTable uses reference identity, not the
    // record's value equality, which is exactly what we want here.
    private static readonly ConditionalWeakTable<ParseResult<TherionFile>, SemanticModel> BindCache = new();

    private static SemanticModel BindCached(ParseResult<TherionFile> parse)
        => BindCache.GetValue(parse, static p => new SemanticBinder().Bind(p.Value!));

    /// <summary>
    /// Build a workspace-level snapshot from raw <see cref="ParseResult{TherionFile}"/>
    /// per file plus their parsed XVI counterparts.
    /// </summary>
    public static WorkspaceSemanticModel Build(
        IReadOnlyDictionary<string, ParseResult<TherionFile>> parsedFiles,
        IReadOnlyCollection<XviFile> xviFiles,
        System.Func<string, bool>? fileExists = null)
    {
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
                var model = BindCached(parse);
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
            Th2Objects = idx.Th2Objects,
            StationOccurrences = BuildStationOccurrences(frozenPerFile, idx),
        };
    }

    /// <summary>
    /// Merges every file's per-file station occurrences and finalizes cross-file <c>@</c>-equate
    /// references (which the per-file binder left unresolved) against the workspace station indexes,
    /// so a rename / find-refs sees a symbol's occurrences across the whole project.
    /// </summary>
    private static FrozenDictionary<SymbolId, ImmutableArray<SymbolOccurrence>> BuildStationOccurrences(
        FrozenDictionary<string, SemanticModel> perFile, ReferenceIndexes idx)
    {
        var occ = new Dictionary<SymbolId, ImmutableArray<SymbolOccurrence>.Builder>();
        void Add(SymbolOccurrence o)
        {
            if (!occ.TryGetValue(o.Symbol, out var b))
                occ[o.Symbol] = b = ImmutableArray.CreateBuilder<SymbolOccurrence>();
            b.Add(o);
        }

        foreach (var model in perFile.Values)
        {
            foreach (var o in model.Occurrences.All) Add(o);   // per-file (already resolved)

            // Cross-file @-equate refs: resolve to the declaring station's identity (same QN the
            // declaring file indexed it under) so the link isn't orphaned by a rename.
            foreach (var uref in model.UnresolvedEquateRefs)
            {
                var r = StationRef.Parse(uref.Raw.Trim());
                StationSymbol? sym =
                    r.SurveyLastName is { } sl &&
                    idx.StationsBySurveyAndPoint.TryGetValue(SurveyPointKey(sl, r.Point), out var a) ? a
                    : idx.StationsByQn.TryGetValue(r.StationQuery, out var b2) ? b2
                    : null;
                if (sym is { } s)
                {
                    Add(new SymbolOccurrence(
                        StationTokenSpans.NarrowToPoint(uref.Span, uref.Raw),
                        new SymbolId(SymbolKind.Station, s.Name), OccurrenceRole.Reference));
                    // ...and the survey components of the cross-file @-path.
                    foreach (var (sid, sp) in SurveyRefDecomposer.Decompose(uref.Raw, uref.Span, s.Name))
                        Add(new SymbolOccurrence(sp, sid, OccurrenceRole.Reference));
                }
            }
        }

        return occ.ToFrozenDictionary(k => k.Key, v => v.Value.ToImmutable());
    }

    /// <summary>
    /// Re-validates a file's <see cref="SemanticModel.UnresolvedEquateRefs"/> against the whole
    /// workspace. A per-file binder can't see cross-file or <c>@</c>-qualified targets, so it defers
    /// here: TH_SEM_001 is emitted only for references that resolve nowhere in the project (using the
    /// same cross-file <see cref="ResolveReference"/> that powers go-to-definition).
    /// </summary>
    public ImmutableArray<Diagnostic> ValidateEquateReferences(SemanticModel fileModel)
    {
        if (fileModel.UnresolvedEquateRefs.IsDefaultOrEmpty) return ImmutableArray<Diagnostic>.Empty;
        var b = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var r in fileModel.UnresolvedEquateRefs)
            if (ResolveReference(r.Raw, ReferenceKind.Station) is null)
                b.Add(Diagnostic.Create(
                    SemanticDiagnosticCodes.UnresolvedStation,
                    DiagnosticSeverity.Warning,
                    $"Unresolved station reference '{r.Raw}'.",
                    r.Span));
        return b.ToImmutable();
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

    /// <summary>Like <see cref="ResolveReference"/> but also reports the matched kind (for hover info).</summary>
    public ReferenceInfo? DescribeReference(string raw, ReferenceKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var r = StationRef.Parse(raw.Trim());

        ReferenceInfo? Of(string name, SourceSpan? span) => span is { IsEmpty: false } s ? new ReferenceInfo(name, s) : null;

        return kind switch
        {
            ReferenceKind.Survey      => Of("survey", ResolveSurvey(r)),
            ReferenceKind.Map         => Of("map", ResolveMap(r)),
            ReferenceKind.ScrapObject => Of("scrap", ResolveScrapObject(r)),
            ReferenceKind.Station     => Of("station", ResolveStation(r)),
            _ => Of("station", ResolveStation(r)) ?? Of("survey", ResolveSurvey(r))
                 ?? Of("map", ResolveMap(r)) ?? Of("scrap", ResolveScrapObject(r)),
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
