using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol.Server;
using Therion.Semantics;

namespace Therion.Mcp.Tools;

/// <param name="Stations">Distinct stations in this piece, after equate-merging.</param>
/// <param name="Length">Summed length of the piece's non-splay legs, in metres.</param>
/// <param name="Grounded">
/// True when some station in the piece is fixed under a coordinate system, i.e. the piece knows
/// where on Earth it is.
/// </param>
/// <param name="IsMain">
/// The piece with the most stations. It is the reference frame everything else is measured against,
/// so it is never floating even when nothing georeferences it.
/// </param>
/// <param name="Floating">
/// This piece is adrift: not the main one, not grounded, and big enough to be a real passage rather
/// than a stray station. These are the pieces TH_SEM_015 reports.
/// </param>
/// <param name="SampleStations">A few member names, so the caller can find the piece in the source.</param>
public sealed record GraphComponent(
    int Stations,
    double Length,
    bool Grounded,
    bool HasEntrance,
    bool IsMain,
    bool Floating,
    IReadOnlyList<string> SampleStations);

/// <param name="Junctions">Stations where three or more legs meet.</param>
/// <param name="DeadEnds">Degree ≤ 1 and neither an entrance nor fixed — candidate unsurveyed leads.</param>
/// <param name="FloatingComponents">
/// How many pieces are adrift. This is the count TH_SEM_015 reports, so it excludes the main piece
/// and lone unconnected stations.
/// </param>
/// <param name="Truncated">More components exist than are listed; the counts above are complete.</param>
public sealed record SurveyGraph(
    int Stations,
    int Legs,
    int Junctions,
    int DeadEnds,
    int Entrances,
    int FixedStations,
    int ComponentCount,
    int FloatingComponents,
    IReadOnlyList<GraphComponent> Components,
    bool Truncated);

public sealed record SurveyBreakdown(string Survey, int Stations, int Shots, double Length);

/// <param name="Truncated">More surveys exist than are broken down; the totals above are complete.</param>
public sealed record SurveyStats(
    int Surveys,
    int Stations,
    int Shots,
    double TotalLength,
    double VerticalRange,
    int Entrances,
    int FixedPoints,
    IReadOnlyList<SurveyBreakdown> BySurvey,
    bool Truncated);

/// <param name="From">The including file, workspace-relative.</param>
/// <param name="To">The file it pulls in via source/input/load.</param>
public sealed record DependencyEdge(string From, string To);

/// <param name="Dot">Graphviz source of the whole graph, present only when the caller asked for it.</param>
/// <param name="DotTruncated">The Graphviz source was longer than the byte budget and was cut.</param>
public sealed record DependencyGraph(
    IReadOnlyList<DependencyEdge> Edges,
    int Total,
    int Offset,
    bool Truncated,
    string? Dot,
    bool DotTruncated);

/// <summary>
/// An estimated location for a station, from the dead-reckoning solver (no loop closure). Present only
/// when the station could be placed. Distances are metres.
/// </summary>
/// <param name="Depth">Metres below this piece's datum (its highest entrance, or highest surveyed point
/// when it has none), positive going down.</param>
/// <param name="AbsoluteAltitude">Metres above the fix datum, only when a <c>fix</c> with an elevation
/// anchors this piece; otherwise null.</param>
/// <param name="East">Metres east of the piece's local origin (its anchor).</param>
/// <param name="North">Metres north of the piece's local origin.</param>
/// <param name="Up">Metres above the piece's local origin (this is the raw Z; depth is measured from the datum).</param>
/// <param name="Component">Which connected piece (same numbering as survey_graph); the main cave is the largest.</param>
/// <param name="HorizontalReliable">False when the station was reached through a shot missing its bearing
/// or inclination — trust East/North less.</param>
/// <param name="VerticalReliable">False when reached through a shot missing its inclination — trust Depth/Up less.</param>
/// <param name="Misclosure">Worst loop (or multi-fix) disagreement in this piece, metres — the honest error
/// bar. Null when the piece has no loop to disagree.</param>
public sealed record StationPositionDto(
    double Depth,
    double? AbsoluteAltitude,
    double East,
    double North,
    double Up,
    int Component,
    bool HorizontalReliable,
    bool VerticalReliable,
    double? Misclosure);

/// <param name="Name">Fully-qualified station name.</param>
/// <param name="Kind">How the station was declared: shot, station, fix, or equate.</param>
/// <param name="Cs">Coordinate system in force at the fix, when fixed.</param>
/// <param name="Position">The station's estimated location, or null when it could not be placed.</param>
public sealed record StationDto(
    string Name,
    string Kind,
    IReadOnlyList<string> Flags,
    double? X,
    double? Y,
    double? Z,
    string? Cs,
    Location? Declaration,
    StationPositionDto? Position);

/// <param name="PositionSource">How the positions were derived: "approximate" (dead-reckoning) or "none"
/// (nothing placeable). A later batch adds "compiled" for loop-closed positions read from a build.</param>
/// <param name="PositionCaveat">A one-line warning when positions are approximate, so the answer can repeat it.</param>
public sealed record StationList(
    IReadOnlyList<StationDto> Stations,
    int Total,
    int Offset,
    bool Truncated,
    string PositionSource,
    string? PositionCaveat);

/// <summary>One survey leg (a non-splay shot between two stations), with its measurements and — when a
/// depth filter is in play — the estimated depth of each end.</summary>
/// <param name="From">The leg's from-station, fully qualified.</param>
/// <param name="To">The leg's to-station, fully qualified.</param>
/// <param name="Length">Tape length in metres, or null when the row omitted it.</param>
/// <param name="Bearing">Compass bearing in degrees (0=N, 90=E), as recorded — MAGNETIC, not corrected
/// to true north. Null when the row omitted it.</param>
/// <param name="Clino">Inclination in degrees, positive up. Null when omitted.</param>
/// <param name="Flags">Any shot flags in force (surface, duplicate, approximate); splays are excluded upstream.</param>
/// <param name="DepthFrom">Estimated depth (metres below the entrance) of the from-station, when placeable.</param>
/// <param name="DepthTo">Estimated depth of the to-station, when placeable.</param>
public sealed record LegDto(
    string From,
    string To,
    double? Length,
    double? Bearing,
    double? Clino,
    IReadOnlyList<string> Flags,
    double? DepthFrom,
    double? DepthTo,
    Location? Declaration);

/// <param name="BearingsAreMagnetic">Always true for now: bearings are as recorded, not declination-corrected.</param>
/// <param name="PositionCaveat">Present when a depth filter used the approximate positions; a warning to repeat.</param>
public sealed record LegList(
    IReadOnlyList<LegDto> Legs,
    int Total,
    int Offset,
    bool Truncated,
    bool BearingsAreMagnetic,
    string? PositionCaveat);

/// <summary>Two stations that sit close together but in different connected pieces — a likely missing equate.</summary>
/// <param name="Distance">Straight-line distance in metres, in the shared coordinate system.</param>
/// <param name="ComponentA">The connected piece each station belongs to (survey_graph numbering).</param>
/// <param name="CoordinateSystem">The projected CS both pieces are georeferenced in.</param>
public sealed record EquateCandidateDto(
    string StationA,
    string StationB,
    double Distance,
    int ComponentA,
    int ComponentB,
    string CoordinateSystem,
    Location? DeclarationA,
    Location? DeclarationB);

/// <param name="ComparablePieces">How many independently-georeferenced pieces (same metric CS) could be compared.</param>
/// <param name="Note">Why the list is empty, or the verify-before-you-join caveat when it isn't.</param>
public sealed record EquateCandidates(
    IReadOnlyList<EquateCandidateDto> Candidates,
    int Total,
    int Offset,
    bool Truncated,
    int ComparablePieces,
    string? Note);

/// <summary>An approximate spatial extent of a survey, in its connected piece's local metre frame.</summary>
/// <param name="Component">Which connected piece (survey_graph numbering); -1 when the survey's stations span several.</param>
public sealed record SurveyExtent(
    double MinEast, double MinNorth, double MinUp,
    double MaxEast, double MaxNorth, double MaxUp,
    double CentroidEast, double CentroidNorth, double CentroidUp,
    int Component);

/// <param name="Team">The surveyors (from the survey's <c>team</c> commands).</param>
/// <param name="Dates">Raw date strings from the survey's <c>date</c> commands.</param>
/// <param name="DateFrom">Earliest day any of <paramref name="Dates"/> covers (ISO yyyy-MM-dd), or null.</param>
/// <param name="DateTo">Latest day any of <paramref name="Dates"/> covers, or null.</param>
/// <param name="Extent">Approximate bbox+centroid of the survey's placed stations, or null when none are placed.</param>
public sealed record SurveyInfoDto(
    string Name,
    string? Title,
    IReadOnlyList<string> Team,
    IReadOnlyList<string> Dates,
    string? DateFrom,
    string? DateTo,
    int Stations,
    double Length,
    SurveyExtent? Extent,
    Location? Declaration);

/// <param name="PositionSource">"approximate" (dead-reckoning extents) or "none" (nothing placeable).</param>
/// <param name="PositionCaveat">When extents are present: they're approximate and per-piece-local — a warning to repeat.</param>
public sealed record SurveyInfoList(
    IReadOnlyList<SurveyInfoDto> Surveys,
    int Total,
    int Offset,
    bool Truncated,
    string PositionSource,
    string? PositionCaveat);

/// <summary>Ring R1 — the shape of the cave and the shape of the project.</summary>
[McpServerToolType]
public sealed class GraphTools(IWorkspaceHost host)
{
    /// <summary>Enough member names to identify a piece in the source without dumping a whole cave.</summary>
    private const int SampleStationsPerComponent = 5;

    [McpServerTool(Name = "survey_graph", Title = "Survey graph", ReadOnly = true, Idempotent = true)]
    [Description("The connectivity of the cave: how many stations and legs, where the junctions and "
               + "dead ends are, and — most usefully — whether it is one connected piece or several. "
               + "A piece that is adrift (not the main one, not fixed under a coordinate system, and "
               + "more than a lone station) is 'floating', the problem TH_SEM_015 reports. Equates "
               + "are merged, including the @-equates that join files, so these are real pieces.")]
    public async Task<ToolResult<SurveyGraph>> GetSurveyGraph(
        [Description("Maximum components to list; capped at 2000, defaults to 200. The counts are always complete.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<SurveyGraph>.Failure(error);

        var model = snapshot!.Model;
        var graph = ConnectivityGraph.Build(model, mergeCrossFileEquates: true);

        var grounded = GroundedRepresentatives(model, graph);
        var entrances = graph.Entrances.ToHashSet();
        var lengthByComponent = LengthByComponent(model, graph);

        // Largest first, ties broken by the smallest member name — the same order the disconnection
        // diagnostic uses to pick the main piece, so "main" means the same thing in both.
        var ordered = Enumerable.Range(0, graph.Components.Length)
            .OrderByDescending(i => graph.Components[i].Length)
            .ThenBy(i => SmallestName(graph.Components[i]), StringComparer.Ordinal)
            .ToList();

        var components = new List<GraphComponent>(ordered.Count);
        for (int rank = 0; rank < ordered.Count; rank++)
        {
            var members = graph.Components[ordered[rank]];
            bool isMain = rank == 0;
            bool isGrounded = members.Any(grounded.Contains);

            components.Add(new GraphComponent(
                Stations: members.Length,
                Length: Round(lengthByComponent.TryGetValue(ordered[rank], out var l) ? l : 0),
                Grounded: isGrounded,
                HasEntrance: members.Any(entrances.Contains),
                IsMain: isMain,
                // A lone station adrift is a stray, not a lost passage; the diagnostic ignores it too.
                Floating: !isMain && !isGrounded && members.Length >= 2,
                SampleStations: members.Take(SampleStationsPerComponent).Select(m => m.ToString()).ToList()));
        }

        int take = ToolLimits.ClampLimit(limit);

        return ToolResult<SurveyGraph>.Success(new SurveyGraph(
            Stations: graph.NodeCount,
            Legs: graph.EdgeCount,
            Junctions: graph.Components.SelectMany(c => c).Count(s => graph.Degree(s) >= 3),
            DeadEnds: graph.DeadEnds.Length,
            Entrances: graph.Entrances.Length,
            FixedStations: graph.FixedStations.Length,
            ComponentCount: components.Count,
            FloatingComponents: components.Count(c => c.Floating),
            Components: components.Take(take).ToList(),
            Truncated: components.Count > take));
    }

    /// <summary>The ordinally-smallest member name, the diagnostic's tiebreaker between equal-sized pieces.</summary>
    private static string SmallestName(System.Collections.Immutable.ImmutableArray<QualifiedName> members)
    {
        string smallest = members[0].ToString();
        for (int i = 1; i < members.Length; i++)
        {
            var candidate = members[i].ToString();
            if (string.CompareOrdinal(candidate, smallest) < 0) smallest = candidate;
        }
        return smallest;
    }

    [McpServerTool(Name = "survey_stats", Title = "Survey statistics", ReadOnly = true, Idempotent = true)]
    [Description("Project totals — surveys, stations, shots, surveyed length in metres, vertical "
               + "range, entrances, fixed points — plus the same length and counts per survey. "
               + "These are the numbers 'therion-cli stats' prints.")]
    public async Task<ToolResult<SurveyStats>> GetSurveyStats(
        [Description("Maximum surveys to break down; capped at 2000, defaults to 200. The totals are always complete.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<SurveyStats>.Failure(error);

        var model = snapshot!.Model;
        var totals = ProjectStatistics.ComputeTotals(model);

        var bySurvey = new List<SurveyBreakdown>();
        foreach (var root in ProjectStatistics.BuildSurveyTree(model)) Flatten(root, bySurvey);

        int take = ToolLimits.ClampLimit(limit);

        return ToolResult<SurveyStats>.Success(new SurveyStats(
            Surveys: totals.SurveyCount,
            Stations: totals.StationCount,
            Shots: totals.ShotCount,
            TotalLength: Round(totals.TotalLength),
            VerticalRange: Round(totals.VerticalRange),
            Entrances: totals.EntranceCount,
            FixedPoints: totals.FixedCount,
            BySurvey: bySurvey.OrderBy(s => s.Survey, StringComparer.Ordinal).Take(take).ToList(),
            Truncated: bySurvey.Count > take));
    }

    [McpServerTool(Name = "deps_graph", Title = "Dependency graph", ReadOnly = true, Idempotent = true)]
    [Description("Which file pulls in which, through source/input/load. Set dot:true to also get "
               + "Graphviz source for rendering.")]
    public async Task<ToolResult<DependencyGraph>> GetDepsGraph(
        [Description("Also return the graph as Graphviz DOT text.")]
        bool dot = false,
        [Description("Number of edges to skip, for paging.")]
        int offset = 0,
        [Description("Maximum edges to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        [Description("Byte budget for the Graphviz text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<DependencyGraph>.Failure(error);

        var all = snapshot!.Model.FileGraphEdges
            .Select(e => new DependencyEdge(
                WorkspacePaths.ToRelative(snapshot.Root, e.From),
                WorkspacePaths.ToRelative(snapshot.Root, e.To)))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        int start = Math.Clamp(offset, 0, all.Count);
        var page = all.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        // The Graphviz text describes the whole graph, not the page: a subgraph of an include tree is
        // not a graph anyone wants to render.
        string? dotText = null;
        bool dotTruncated = false;
        if (dot)
        {
            var full = ToDot(all);
            dotText = ToolLimits.Utf8Prefix(full, ToolLimits.ClampBytes(maxBytes));
            dotTruncated = dotText.Length < full.Length;
        }

        return ToolResult<DependencyGraph>.Success(new DependencyGraph(
            page, all.Count, start, Truncated: start + page.Count < all.Count, dotText, dotTruncated));
    }

    [McpServerTool(Name = "list_stations", Title = "List stations", ReadOnly = true, Idempotent = true)]
    [Description("The project's stations with their flags, fix coordinates, and an ESTIMATED position "
               + "(depth below the entrance, absolute altitude when the cave is georeferenced, and "
               + "local east/north). Filter by depth to answer 'which stations are around -500 m', or to "
               + "entrances / fixed points. IMPORTANT: positions are approximate — dead-reckoning with no "
               + "loop closure — so each carries reliability flags and a misclosure error bar; depth is "
               + "measured down from each piece's own entrance. The depth filter only considers stations "
               + "whose vertical position is reliable.")]
    public async Task<ToolResult<StationList>> ListStations(
        [Description("Only stations carrying the 'entrance' flag.")]
        bool entrancesOnly = false,
        [Description("Only stations placed by a 'fix' command.")]
        bool fixedOnly = false,
        [Description("Only stations whose qualified name starts with this survey path, e.g. 'cave.upper'.")]
        string? surveyPrefix = null,
        [Description("Only stations at least this many metres below the entrance datum (positive = deeper). "
                   + "A station 500 m below the entrance has depth 500.")]
        double? minDepth = null,
        [Description("Only stations at most this many metres below the entrance datum (positive = deeper).")]
        double? maxDepth = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<StationList>.Failure(error);

        var positions = StationPositionEstimator.Get(snapshot!.Model);

        IEnumerable<StationSymbol> stations = snapshot.Model.StationsByQn.Values;

        if (entrancesOnly) stations = stations.Where(s => s.IsEntrance);
        if (fixedOnly) stations = stations.Where(s => s.Kind == StationDeclarationKind.Fix);
        if (!string.IsNullOrWhiteSpace(surveyPrefix))
            stations = stations.Where(s => s.Name.ToString().StartsWith(surveyPrefix + ".", StringComparison.Ordinal));

        bool byDepth = minDepth is not null || maxDepth is not null;

        var ordered = stations
            .Select(s => ToDto(s, snapshot.Root, positions))
            .Where(dto => !byDepth || WithinDepth(dto.Position, minDepth, maxDepth))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        int start = Math.Clamp(offset, 0, ordered.Count);
        var page = ordered.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        bool placed = positions.Positions.Count > 0;
        return ToolResult<StationList>.Success(new StationList(
            page, ordered.Count, start,
            Truncated: start + page.Count < ordered.Count,
            PositionSource: placed ? "approximate" : "none",
            PositionCaveat: placed ? ApproximatePositionCaveat : null));
    }

    private const string ApproximatePositionCaveat =
        "Positions are approximate (dead-reckoning, no loop closure). Depth is measured down from each "
        + "piece's entrance; absolute altitude appears only where a fix anchors the piece. Check each "
        + "station's misclosure and reliability flags before quoting a figure.";

    /// <summary>A depth filter passes only stations with a placed, vertically-reliable depth in range.</summary>
    private static bool WithinDepth(StationPositionDto? position, double? minDepth, double? maxDepth) =>
        position is { VerticalReliable: true } p
        && (minDepth is not { } lo || p.Depth >= lo)
        && (maxDepth is not { } hi || p.Depth <= hi);

    [McpServerTool(Name = "list_survey_info", Title = "Survey info", ReadOnly = true, Idempotent = true)]
    [Description("Per-survey metadata: title, team (the surveyors), the survey dates, station count and "
               + "surveyed length — plus, for 'which part of the cave' questions, an approximate spatial "
               + "extent (bounding box + centroid). Filter by a date range (dateFrom/dateTo as YYYY, "
               + "YYYY.MM or YYYY.MM.DD — a survey matches when its dates OVERLAP the range) or a survey "
               + "path. Team and dates come straight from the survey's team/date commands; undated "
               + "surveys are excluded when a date filter is set.")]
    public async Task<ToolResult<SurveyInfoList>> ListSurveyInfo(
        [Description("Only surveys whose dates reach on/after this date (YYYY, YYYY.MM, or YYYY.MM.DD).")]
        string? dateFrom = null,
        [Description("Only surveys whose dates reach on/before this date.")]
        string? dateTo = null,
        [Description("Only this survey and those under it, e.g. 'cave.upper'.")]
        string? surveyPrefix = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<SurveyInfoList>.Failure(error);

        // A bad date bound is a caller mistake, not a silent no-op.
        DateOnly? from = null, to = null;
        if (dateFrom is not null)
        {
            if (TherionDate.Parse(dateFrom) is not { } fi)
                return ToolResult<SurveyInfoList>.Failure(ToolErrorCodes.InvalidArgument,
                    $"Unparseable dateFrom '{dateFrom}'. Use YYYY, YYYY.MM, or YYYY.MM.DD.");
            from = fi.Min;
        }
        if (dateTo is not null)
        {
            if (TherionDate.Parse(dateTo) is not { } ti)
                return ToolResult<SurveyInfoList>.Failure(ToolErrorCodes.InvalidArgument,
                    $"Unparseable dateTo '{dateTo}'. Use YYYY, YYYY.MM, or YYYY.MM.DD.");
            to = ti.Max;
        }
        bool byDate = from is not null || to is not null;
        var rangeFrom = from ?? DateOnly.MinValue;
        var rangeTo = to ?? DateOnly.MaxValue;

        var model = snapshot!.Model;
        var positions = StationPositionEstimator.Get(model);
        var extents = ComputeSurveyExtents(model, positions);

        var treeInfo = new Dictionary<string, (int Stations, double Length)>(StringComparer.Ordinal);
        foreach (var root in ProjectStatistics.BuildSurveyTree(model)) CollectTreeInfo(root, treeInfo);

        var list = new List<SurveyInfoDto>();
        foreach (var survey in model.SurveysByFullName.Values)
        {
            var name = survey.Name.ToString();
            if (surveyPrefix is { Length: > 0 } p
                && name != p && !name.StartsWith(p + ".", StringComparison.Ordinal)) continue;

            var span = TherionDate.Span(survey.Dates);
            if (byDate && (span is not { } sp || !sp.Overlaps(rangeFrom, rangeTo))) continue;

            var (stations, length) = treeInfo.TryGetValue(name, out var ti) ? ti : (0, 0d);
            list.Add(new SurveyInfoDto(
                Name: name,
                Title: survey.Title,
                Team: survey.Team.IsDefaultOrEmpty ? Array.Empty<string>() : survey.Team,
                Dates: survey.Dates.IsDefaultOrEmpty ? Array.Empty<string>() : survey.Dates,
                DateFrom: span?.Min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTo: span?.Max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Stations: stations,
                Length: Round(length),
                Extent: extents.TryGetValue(name, out var ext) ? ext.ToExtent() : null,
                Declaration: Location.From(survey.DeclarationSpan, snapshot.Root)));
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        int start = Math.Clamp(offset, 0, list.Count);
        var page = list.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        bool placed = positions.Positions.Count > 0;
        return ToolResult<SurveyInfoList>.Success(new SurveyInfoList(
            page, list.Count, start,
            Truncated: start + page.Count < list.Count,
            PositionSource: placed ? "approximate" : "none",
            PositionCaveat: placed ? SurveyExtentCaveat : null));
    }

    private const string SurveyExtentCaveat =
        "Spatial extent (bbox + centroid) is approximate (dead-reckoning, no loop closure) and given in "
        + "each connected piece's own local metre frame — the origin is arbitrary and the axes are about "
        + "magnetic north/east — so compare extents only between surveys in the SAME component. A "
        + "component of -1 means the survey's stations span more than one piece.";

    /// <summary>Rolled-up station count + length per survey (subtree totals), keyed by full name.</summary>
    private static void CollectTreeInfo(SurveyTreeNode node, Dictionary<string, (int, double)> map)
    {
        map[node.FullName] = (node.Stations, node.Length);
        foreach (var child in node.Children) CollectTreeInfo(child, map);
    }

    /// <summary>Accumulates each placed station's position into every enclosing survey, so a survey's
    /// extent covers its whole subtree (matching the rolled-up station count).</summary>
    private static Dictionary<string, ExtentAccum> ComputeSurveyExtents(WorkspaceSemanticModel model, PositionSet positions)
    {
        var surveyNames = new HashSet<string>(model.SurveysByFullName.Keys, StringComparer.Ordinal);
        var acc = new Dictionary<string, ExtentAccum>(StringComparer.Ordinal);
        foreach (var (qn, pos) in positions.Positions)
        {
            var cur = qn;
            while (cur.HasParent)
            {
                cur = cur.Parent();
                var name = cur.ToString();
                if (!surveyNames.Contains(name)) continue;
                if (!acc.TryGetValue(name, out var a)) acc[name] = a = new ExtentAccum();
                a.Add(pos);
            }
        }
        return acc;
    }

    private sealed class ExtentAccum
    {
        private int _count;
        private double _sumX, _sumY, _sumZ;
        private double _minX = double.MaxValue, _minY = double.MaxValue, _minZ = double.MaxValue;
        private double _maxX = double.MinValue, _maxY = double.MinValue, _maxZ = double.MinValue;
        private int _component = -1;
        private bool _mixed;

        public void Add(StationPosition p)
        {
            _count++;
            _sumX += p.X; _sumY += p.Y; _sumZ += p.Z;
            if (p.X < _minX) _minX = p.X;
            if (p.Y < _minY) _minY = p.Y;
            if (p.Z < _minZ) _minZ = p.Z;
            if (p.X > _maxX) _maxX = p.X;
            if (p.Y > _maxY) _maxY = p.Y;
            if (p.Z > _maxZ) _maxZ = p.Z;
            if (_component == -1) _component = p.ComponentId;
            else if (_component != p.ComponentId) _mixed = true;
        }

        public SurveyExtent ToExtent() => new(
            Round(_minX), Round(_minY), Round(_minZ),
            Round(_maxX), Round(_maxY), Round(_maxZ),
            Round(_sumX / _count), Round(_sumY / _count), Round(_sumZ / _count),
            _mixed ? -1 : _component);
    }

    [McpServerTool(Name = "query_legs", Title = "Query legs", ReadOnly = true, Idempotent = true)]
    [Description("Survey legs (the shots between stations) filtered by their bearing, inclination, "
               + "survey, and depth — what list_stations cannot answer. Use `direction` for a compass "
               + "point (e.g. 'NNW' = 337.5°±11.25°) or `minBearing`/`maxBearing` for an exact range "
               + "(a range that crosses north, e.g. 350→10, is allowed). Bearings are MAGNETIC, as "
               + "recorded — not corrected to true north. The depth filter keeps only legs lying "
               + "ENTIRELY within the band (both ends placed and vertically reliable); depths are "
               + "approximate (dead-reckoning). Splays are always excluded.")]
    public async Task<ToolResult<LegList>> QueryLegs(
        [Description("A 16-point compass point the bearing must fall within (±11.25°), e.g. 'N', 'NNW', "
                   + "'SE'. Overrides minBearing/maxBearing when set.")]
        string? direction = null,
        [Description("Minimum bearing in degrees (0-360). With maxBearing lower than it, the range wraps north.")]
        double? minBearing = null,
        [Description("Maximum bearing in degrees (0-360).")]
        double? maxBearing = null,
        [Description("Minimum inclination in degrees (positive up). E.g. 60 for steeply ascending legs.")]
        double? minClino = null,
        [Description("Maximum inclination in degrees. E.g. -60 for steeply descending legs.")]
        double? maxClino = null,
        [Description("Only legs whose from-station is under this survey path, e.g. 'cave.upper'.")]
        string? surveyPrefix = null,
        [Description("Only legs lying entirely at least this many metres below the entrance (positive = deeper).")]
        double? minDepth = null,
        [Description("Only legs lying entirely at most this many metres below the entrance.")]
        double? maxDepth = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<LegList>.Failure(error);

        (double Lo, double Hi)? bearingBand;
        if (direction is not null)
        {
            if (CompassPoint(direction) is not { } centre)
                return ToolResult<LegList>.Failure(ToolErrorCodes.InvalidArgument,
                    $"'{direction}' is not a compass point. Use one of N, NNE, NE, ENE, E, …, NNW.");
            bearingBand = (Normalize(centre - CompassHalfWindow), Normalize(centre + CompassHalfWindow));
        }
        else if (minBearing is not null || maxBearing is not null)
        {
            bearingBand = (Normalize(minBearing ?? 0), Normalize(maxBearing ?? 360));
        }
        else
        {
            bearingBand = null;
        }

        bool byDepth = minDepth is not null || maxDepth is not null;
        var positions = StationPositionEstimator.Get(snapshot!.Model);

        var legs = new List<LegDto>();
        foreach (var file in snapshot.Model.PerFile.Values)
            foreach (var shot in file.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                if (surveyPrefix is { Length: > 0 } prefix
                    && !shot.From.ToString().StartsWith(prefix + ".", StringComparison.Ordinal)) continue;
                if (bearingBand is { } band && !BearingInBand(shot.Compass, band)) continue;
                if (minClino is { } loc && (shot.Clino is not { } c1 || c1 < loc)) continue;
                if (maxClino is { } hic && (shot.Clino is not { } c2 || c2 > hic)) continue;

                var pFrom = positions.For(shot.From);
                var pTo = positions.For(shot.To);
                if (byDepth && !LegWithinDepth(pFrom, pTo, minDepth, maxDepth)) continue;

                legs.Add(new LegDto(
                    From: shot.From.ToString(),
                    To: shot.To.ToString(),
                    Length: shot.Length,
                    Bearing: shot.Compass,
                    Clino: shot.Clino,
                    Flags: ShotFlagNames(shot.Flags),
                    DepthFrom: pFrom is { VerticalReliable: true } df ? Round(df.Depth) : null,
                    DepthTo: pTo is { VerticalReliable: true } dt ? Round(dt.Depth) : null,
                    Declaration: Location.From(shot.Span, snapshot.Root)));
            }

        legs.Sort((a, b) =>
        {
            int byFrom = string.CompareOrdinal(a.From, b.From);
            return byFrom != 0 ? byFrom : string.CompareOrdinal(a.To, b.To);
        });

        int start = Math.Clamp(offset, 0, legs.Count);
        var page = legs.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        return ToolResult<LegList>.Success(new LegList(
            page, legs.Count, start,
            Truncated: start + page.Count < legs.Count,
            BearingsAreMagnetic: true,
            PositionCaveat: byDepth ? ApproximatePositionCaveat : null));
    }

    private const double CompassHalfWindow = 11.25;

    /// <summary>The centre bearing of a 16-point compass name, or null when unrecognized.</summary>
    private static double? CompassPoint(string name) => name.Trim().ToUpperInvariant() switch
    {
        "N" => 0, "NNE" => 22.5, "NE" => 45, "ENE" => 67.5,
        "E" => 90, "ESE" => 112.5, "SE" => 135, "SSE" => 157.5,
        "S" => 180, "SSW" => 202.5, "SW" => 225, "WSW" => 247.5,
        "W" => 270, "WNW" => 292.5, "NW" => 315, "NNW" => 337.5,
        _ => null,
    };

    private static double Normalize(double deg) => ((deg % 360) + 360) % 360;

    /// <summary>True when a (magnetic) bearing falls in the band, which may wrap past north.</summary>
    private static bool BearingInBand(double? bearing, (double Lo, double Hi) band)
    {
        if (bearing is not { } b) return false;
        b = Normalize(b);
        return band.Lo <= band.Hi
            ? b >= band.Lo && b <= band.Hi
            : b >= band.Lo || b <= band.Hi;   // wraps through 0°/360°
    }

    /// <summary>A leg passes the depth band only when BOTH ends are placed, reliable, and inside it.</summary>
    private static bool LegWithinDepth(StationPosition? from, StationPosition? to, double? minDepth, double? maxDepth)
    {
        if (from is not { VerticalReliable: true } f || to is not { VerticalReliable: true } t) return false;
        double shallow = Math.Min(f.Depth, t.Depth), deep = Math.Max(f.Depth, t.Depth);
        return (minDepth is not { } lo || shallow >= lo) && (maxDepth is not { } hi || deep <= hi);
    }

    private static IReadOnlyList<string> ShotFlagNames(ShotFlags flags)
    {
        if (flags == ShotFlags.None) return [];
        var names = new List<string>(3);
        if ((flags & ShotFlags.Surface) != 0) names.Add("surface");
        if ((flags & ShotFlags.Duplicate) != 0) names.Add("duplicate");
        if ((flags & ShotFlags.Approximate) != 0) names.Add("approximate");
        return names;
    }

    [McpServerTool(Name = "find_equate_candidates", Title = "Find missing-equate candidates", ReadOnly = true, Idempotent = true)]
    [Description("Finds pairs of stations that sit close together but in DIFFERENT connected pieces — a "
               + "likely missing 'equate' where two surveys meet underground but were never joined. This "
               + "needs the pieces to be independently georeferenced in the SAME projected (metric) "
               + "coordinate system: only then are their positions comparable across pieces. A project "
               + "without at least two such pieces gets an empty result explaining why. Positions are "
               + "APPROXIMATE (dead-reckoning), so every pair is a candidate to verify on the ground before "
               + "you add an equate — not a confirmed join.")]
    public async Task<ToolResult<EquateCandidates>> FindEquateCandidates(
        [Description("Maximum distance in metres between two stations to flag them as a candidate. Default 10.")]
        double threshold = 10.0,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        if (!(threshold > 0))
            return ToolResult<EquateCandidates>.Failure(
                ToolErrorCodes.InvalidArgument, "threshold must be a positive number of metres.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<EquateCandidates>.Failure(error);

        var model = snapshot!.Model;
        var positions = StationPositionEstimator.Get(model);
        var graph = ConnectivityGraph.Build(model, mergeCrossFileEquates: true);

        // Each comparable piece gets the offset that turns its local frame into absolute metric coordinates.
        var frames = ComponentFrames(model, graph, positions);
        if (frames.Count < 2)
            return ToolResult<EquateCandidates>.Success(new EquateCandidates(
                [], 0, 0, Truncated: false, ComparablePieces: frames.Count,
                Note: $"Need at least two independently georeferenced pieces in the same projected (metric) "
                    + $"coordinate system to compare positions across pieces; found {frames.Count}. A piece is "
                    + "georeferenced when it carries a fix in a projected CS such as UTM."));

        // Absolute-position every reliably-placed station that belongs to a comparable piece.
        var points = new List<GeoStation>();
        var seenReps = new HashSet<QualifiedName>();
        foreach (var station in model.StationsByQn.Values)
        {
            if (!seenReps.Add(graph.Representative(station.Name))) continue;
            if (positions.For(station.Name) is not { HorizontalReliable: true, VerticalReliable: true } p) continue;
            if (!frames.TryGetValue(p.ComponentId, out var frame)) continue;
            points.Add(new GeoStation(
                station.Name.ToString(), p.ComponentId, frame.Cs,
                p.X + frame.OffsetE, p.Y + frame.OffsetN, p.Z + frame.OffsetU,
                Location.From(station.DeclarationSpan, snapshot.Root)));
        }

        var candidates = NearbyCrossPiecePairs(points, threshold);
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        int start = Math.Clamp(offset, 0, candidates.Count);
        var page = candidates.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        return ToolResult<EquateCandidates>.Success(new EquateCandidates(
            page, candidates.Count, start,
            Truncated: start + page.Count < candidates.Count,
            ComparablePieces: frames.Count,
            Note: $"Compared {frames.Count} georeferenced pieces. Positions are approximate "
                + "(dead-reckoning, no loop closure), so each pair is a CANDIDATE to verify before adding "
                + "an 'equate' — not a confirmed join."));
    }

    /// <summary>Coordinates at or below this magnitude are treated as lat/long degrees, not projected metres.</summary>
    private const double GeographicDegreeLimit = 360.0;

    private readonly record struct ComponentFrame(double OffsetE, double OffsetN, double OffsetU, string Cs);

    private readonly record struct GeoStation(
        string Name, int Component, string Cs, double E, double N, double U, Location? Declaration);

    /// <summary>
    /// The local→absolute offset for each piece that has a georeferenced fix in a projected (metric) CS.
    /// A piece with only a lat/long fix, or none, is absent — its stations cannot be compared in metres.
    /// </summary>
    private static Dictionary<int, ComponentFrame> ComponentFrames(
        WorkspaceSemanticModel model, ConnectivityGraph graph, PositionSet positions)
    {
        var frames = new Dictionary<int, ComponentFrame>();
        foreach (var station in model.StationsByQn.Values)
        {
            if (station.Kind != StationDeclarationKind.Fix || string.IsNullOrWhiteSpace(station.Cs)) continue;
            if (station.FixX is not { } fx || station.FixY is not { } fy || station.FixZ is not { } fz) continue;
            // Lat/long fixes are degrees, not metres — mixing them into a metric frame would be nonsense.
            if (Math.Abs(fx) <= GeographicDegreeLimit && Math.Abs(fy) <= GeographicDegreeLimit) continue;
            if (positions.For(station.Name) is not { } p || frames.ContainsKey(p.ComponentId)) continue;
            frames[p.ComponentId] = new ComponentFrame(fx - p.X, fy - p.Y, fz - p.Z, station.Cs!);
        }
        return frames;
    }

    /// <summary>
    /// Cross-piece station pairs within <paramref name="threshold"/> metres, using a uniform spatial grid so
    /// the search is near-linear rather than O(n²). Each pair is emitted once (ordered by name) and only when
    /// the two pieces share a coordinate system.
    /// </summary>
    private static List<EquateCandidateDto> NearbyCrossPiecePairs(List<GeoStation> points, double threshold)
    {
        (long, long, long) Cell(GeoStation s) =>
            ((long)Math.Floor(s.E / threshold), (long)Math.Floor(s.N / threshold), (long)Math.Floor(s.U / threshold));

        var grid = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < points.Count; i++)
        {
            var key = Cell(points[i]);
            (grid.TryGetValue(key, out var list) ? list : grid[key] = new List<int>()).Add(i);
        }

        var results = new List<EquateCandidateDto>();
        double threshold2 = threshold * threshold;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var (cx, cy, cz) = Cell(a);
            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            for (long dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket)) continue;
                foreach (int j in bucket)
                {
                    var b = points[j];
                    if (a.Component == b.Component) continue;
                    if (!string.Equals(a.Cs, b.Cs, StringComparison.Ordinal)) continue;
                    if (string.CompareOrdinal(a.Name, b.Name) >= 0) continue;   // emit each unordered pair once
                    double de = a.E - b.E, dn = a.N - b.N, du = a.U - b.U;
                    double d2 = de * de + dn * dn + du * du;
                    if (d2 > threshold2) continue;
                    results.Add(new EquateCandidateDto(
                        a.Name, b.Name, Round(Math.Sqrt(d2)), a.Component, b.Component, a.Cs,
                        a.Declaration, b.Declaration));
                }
            }
        }
        return results;
    }

    /// <summary>
    /// Equate representatives of the stations a georeferenced <c>fix</c> anchors — the same set, and
    /// the same <c>@</c>-resolution, that TH_SEM_015 uses. A bare <c>fix</c> with no <c>cs</c> is a
    /// local placeholder, not a position on Earth, and does not ground anything.
    /// </summary>
    private static HashSet<QualifiedName> GroundedRepresentatives(WorkspaceSemanticModel model, ConnectivityGraph graph) =>
        WorkspaceEquates.GroundedStations(model).Select(graph.Representative).ToHashSet();

    /// <summary>Surveyed length of each component, from the non-splay legs whose endpoints it contains.</summary>
    private static Dictionary<int, double> LengthByComponent(WorkspaceSemanticModel model, ConnectivityGraph graph)
    {
        var componentOf = new Dictionary<QualifiedName, int>();
        for (int i = 0; i < graph.Components.Length; i++)
            foreach (var member in graph.Components[i])
                componentOf[member] = i;

        var lengths = new Dictionary<int, double>();
        foreach (var file in model.PerFile.Values)
            foreach (var shot in file.Shots)
            {
                if (DataQualityChecks.IsSplay(shot) || shot.Length is not { } length) continue;
                if (!componentOf.TryGetValue(graph.Representative(shot.From), out var component)) continue;
                lengths[component] = lengths.GetValueOrDefault(component) + length;
            }
        return lengths;
    }

    private static void Flatten(SurveyTreeNode node, List<SurveyBreakdown> into)
    {
        into.Add(new SurveyBreakdown(node.FullName, node.Stations, node.Shots, Round(node.Length)));
        foreach (var child in node.Children) Flatten(child, into);
    }

    private static StationDto ToDto(StationSymbol station, string root, PositionSet positions) => new(
        Name: station.Name.ToString(),
        Kind: station.Kind.ToString().ToLowerInvariant(),
        Flags: station.Flags.IsDefaultOrEmpty ? [] : station.Flags,
        X: station.FixX,
        Y: station.FixY,
        Z: station.FixZ,
        Cs: station.Cs,
        Declaration: Location.From(station.DeclarationSpan, root),
        Position: ToPositionDto(positions.For(station.Name)));

    private static StationPositionDto? ToPositionDto(StationPosition? p) => p is null ? null : new(
        Depth: Round(p.Depth),
        AbsoluteAltitude: p.AbsoluteAltitude is { } a ? Round(a) : null,
        East: Round(p.X),
        North: Round(p.Y),
        Up: Round(p.Z),
        Component: p.ComponentId,
        HorizontalReliable: p.HorizontalReliable,
        VerticalReliable: p.VerticalReliable,
        Misclosure: p.MisclosureHint is { } m ? Round(m) : null);

    private static string ToDot(IReadOnlyList<DependencyEdge> edges)
    {
        var dot = new StringBuilder("digraph deps {\n");
        foreach (var edge in edges)
            dot.Append("  \"").Append(edge.From).Append("\" -> \"").Append(edge.To).Append("\";\n");
        return dot.Append('}').ToString();
    }

    /// <summary>Survey lengths are metres measured with a tape; a millimetre is already generous.</summary>
    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
