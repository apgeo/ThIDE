using System.ComponentModel;
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

/// <param name="Name">Fully-qualified station name.</param>
/// <param name="Kind">How the station was declared: shot, station, fix, or equate.</param>
/// <param name="Cs">Coordinate system in force at the fix, when fixed.</param>
public sealed record StationDto(
    string Name,
    string Kind,
    IReadOnlyList<string> Flags,
    double? X,
    double? Y,
    double? Z,
    string? Cs,
    Location? Declaration);

public sealed record StationList(IReadOnlyList<StationDto> Stations, int Total, int Offset, bool Truncated);

/// <summary>Ring R1 — the shape of the cave and the shape of the project.</summary>
[McpServerToolType]
public sealed class GraphTools(WorkspaceHost host)
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
    [Description("The project's stations with their coordinates and flags — what list_symbols cannot "
               + "tell you. Filter to entrances, or to stations fixed in absolute coordinates, to "
               + "answer 'where is this cave' without reading files.")]
    public async Task<ToolResult<StationList>> ListStations(
        [Description("Only stations carrying the 'entrance' flag.")]
        bool entrancesOnly = false,
        [Description("Only stations placed by a 'fix' command.")]
        bool fixedOnly = false,
        [Description("Only stations whose qualified name starts with this survey path, e.g. 'cave.upper'.")]
        string? surveyPrefix = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<StationList>.Failure(error);

        IEnumerable<StationSymbol> stations = snapshot!.Model.StationsByQn.Values;

        if (entrancesOnly) stations = stations.Where(s => s.IsEntrance);
        if (fixedOnly) stations = stations.Where(s => s.Kind == StationDeclarationKind.Fix);
        if (!string.IsNullOrWhiteSpace(surveyPrefix))
            stations = stations.Where(s => s.Name.ToString().StartsWith(surveyPrefix + ".", StringComparison.Ordinal));

        var ordered = stations
            .Select(s => ToDto(s, snapshot.Root))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        int start = Math.Clamp(offset, 0, ordered.Count);
        var page = ordered.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        return ToolResult<StationList>.Success(
            new StationList(page, ordered.Count, start, Truncated: start + page.Count < ordered.Count));
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

    private static StationDto ToDto(StationSymbol station, string root) => new(
        Name: station.Name.ToString(),
        Kind: station.Kind.ToString().ToLowerInvariant(),
        Flags: station.Flags.IsDefaultOrEmpty ? [] : station.Flags,
        X: station.FixX,
        Y: station.FixY,
        Z: station.FixZ,
        Cs: station.Cs,
        Declaration: Location.From(station.DeclarationSpan, root));

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
