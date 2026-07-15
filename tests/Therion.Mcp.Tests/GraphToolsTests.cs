using Therion.Core;
using Therion.Mcp.Tools;
using Therion.Semantics;

namespace Therion.Mcp.Tests;

public class GraphToolsTests
{
    [Fact]
    public async Task Graph_tools_need_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new GraphTools(host);

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.GetSurveyGraph()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.GetSurveyStats()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.GetDepsGraph()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ListStations()).Error!.Code);
    }

    [Fact]
    public async Task Connected_project_is_one_component()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetSurveyGraph();

        Assert.True(result.Ok);
        Assert.Equal(1, result.Data!.ComponentCount);
        Assert.Equal(0, result.Data.FloatingComponents);
    }

    /// <summary>
    /// The pieces survey_graph calls floating must be the pieces TH_SEM_015 reports. The disconnected
    /// fixture has one grounded piece and one adrift; the diagnostic fires exactly once.
    /// </summary>
    [Fact]
    public async Task Floating_components_agree_with_the_disconnection_diagnostic()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var host = new WorkspaceHost();
        var snapshot = await host.LoadAsync(fixture.Thconfig);

        var graph = (await new GraphTools(host).GetSurveyGraph()).Data!;

        Assert.Equal(2, graph.ComponentCount);
        Assert.Equal(1, graph.FloatingComponents);
        Assert.Equal(DisconnectionCount(snapshot.Model), graph.FloatingComponents);
    }

    /// <summary>
    /// A station connected to nothing is a stray, not a lost passage. TH_SEM_015 skips components of
    /// fewer than two stations, and so must the tool — on a real 395-file project this was the
    /// difference between "one floating piece" and "none".
    /// </summary>
    [Fact]
    public async Task A_lone_unconnected_station_is_not_floating()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        File.AppendAllText(fixture.Thconfig, "\nsource caves/stray.th\n");
        File.WriteAllText(fixture.PathTo("caves", "stray.th"), """
            survey stray
              fix lonely 0 0 0
            endsurvey
            """);

        var host = new WorkspaceHost();
        var snapshot = await host.LoadAsync(fixture.Thconfig);
        var graph = (await new GraphTools(host).GetSurveyGraph()).Data!;

        Assert.Equal(3, graph.ComponentCount);
        Assert.Equal(1, graph.FloatingComponents);
        Assert.Equal(DisconnectionCount(snapshot.Model), graph.FloatingComponents);

        var stray = Assert.Single(graph.Components, c => c.SampleStations.Contains("stray.lonely"));
        Assert.False(stray.Floating);
    }

    /// <summary>The biggest piece is the frame everything else is measured against; it never floats.</summary>
    [Fact]
    public async Task The_main_component_is_the_largest_and_is_never_floating()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetSurveyGraph();

        var main = Assert.Single(result.Data!.Components, c => c.IsMain);
        Assert.Equal(result.Data.Components.Max(c => c.Stations), main.Stations);
        Assert.False(main.Floating);
        Assert.Same(main, result.Data.Components[0]);
    }

    [Fact]
    public async Task Grounded_component_is_the_one_fixed_under_a_coordinate_system()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetSurveyGraph();

        var grounded = Assert.Single(result.Data!.Components, c => c.Grounded);
        Assert.Contains("upper.1", grounded.SampleStations);

        var floating = Assert.Single(result.Data.Components, c => c.Floating);
        Assert.Contains("island.x", floating.SampleStations);
        Assert.Equal(2, floating.Stations);
    }

    private static int DisconnectionCount(WorkspaceSemanticModel model) =>
        ProjectDiagnostics.Analyze(model, null, File.Exists)
            .Count(d => d.Code.Value == SemanticDiagnosticCodes.DisconnectedSurvey);

    /// <summary>Component lengths must partition the project's surveyed length, not double-count it.</summary>
    [Fact]
    public async Task Component_lengths_sum_to_the_project_length()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var graph = await tools.GetSurveyGraph();
        var stats = await tools.GetSurveyStats();

        Assert.Equal(stats.Data!.TotalLength, graph.Data!.Components.Sum(c => c.Length), precision: 3);
    }

    /// <summary>A cross-file `equate` makes one cave out of two files; the graph must not see two.</summary>
    [Fact]
    public async Task Cross_file_equates_merge_the_pieces()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetSurveyGraph();

        Assert.Equal(1, result.Data!.ComponentCount);
    }

    /// <summary>These are the numbers `therion-cli stats` prints for the same project.</summary>
    [Fact]
    public async Task Survey_stats_match_the_project_totals()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetSurveyStats();

        Assert.True(result.Ok);
        var stats = result.Data!;
        Assert.Equal(2, stats.Surveys);
        Assert.Equal(5, stats.Stations);
        Assert.Equal(3, stats.Shots);
        Assert.Equal(29.5, stats.TotalLength, precision: 3);
        Assert.Equal(1, stats.FixedPoints);
    }

    [Fact]
    public async Task Survey_stats_break_down_by_survey()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetSurveyStats();

        var island = Assert.Single(result.Data!.BySurvey, s => s.Survey == "island");
        Assert.Equal(7.0, island.Length, precision: 3);
        Assert.Equal(1, island.Shots);
    }

    [Fact]
    public async Task Deps_graph_reports_workspace_relative_edges()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDepsGraph();

        Assert.True(result.Ok);
        Assert.Null(result.Data!.Dot);
        Assert.Equal(2, result.Data.Edges.Count);
        Assert.All(result.Data.Edges, e => Assert.Equal("project.thconfig", e.From));
        Assert.Contains(result.Data.Edges, e => e.To == "caves/upper.th");
    }

    [Fact]
    public async Task Deps_graph_emits_dot_on_request()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDepsGraph(dot: true);

        Assert.StartsWith("digraph deps {", result.Data!.Dot);
        Assert.Contains("\"project.thconfig\" -> \"caves/upper.th\";", result.Data.Dot);
    }

    [Fact]
    public async Task List_stations_carries_coordinates_and_the_coordinate_system()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListStations(fixedOnly: true);

        var station = Assert.Single(result.Data!.Stations);
        Assert.Equal("upper.1", station.Name);
        Assert.Equal("fix", station.Kind);
        Assert.Equal(400000, station.X);
        Assert.Equal(800, station.Z);
        Assert.Equal("UTM33", station.Cs);
        Assert.Equal("caves/upper.th", station.Declaration!.File);
    }

    [Fact]
    public async Task List_stations_filters_by_survey_prefix_and_pages()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var island = await tools.ListStations(surveyPrefix: "island");
        var firstOnly = await tools.ListStations(surveyPrefix: "island", limit: 1);

        Assert.Equal(2, island.Data!.Total);
        Assert.All(island.Data.Stations, s => Assert.StartsWith("island.", s.Name));
        Assert.Single(firstOnly.Data!.Stations);
        Assert.True(firstOnly.Data.Truncated);
    }

    [Fact]
    public async Task List_stations_entrances_only_is_empty_when_nothing_is_flagged()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListStations(entrancesOnly: true);

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Stations);
    }

    [Fact]
    public async Task List_stations_reports_estimated_depth_from_the_entrance()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListStations();

        Assert.Equal("approximate", result.Data!.PositionSource);
        Assert.NotNull(result.Data.PositionCaveat);
        double DepthOf(string name) =>
            result.Data.Stations.Single(s => s.Name == name).Position!.Depth;
        Assert.Equal(0.0, DepthOf("deep.1"), 3);
        Assert.Equal(50.0, DepthOf("deep.2"), 3);
        Assert.Equal(100.0, DepthOf("deep.3"), 3);
        Assert.Equal(100.0, DepthOf("deep.4"), 3);
        Assert.True(result.Data.Stations.Single(s => s.Name == "deep.3").Position!.VerticalReliable);
    }

    [Fact]
    public async Task List_stations_filters_by_depth()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var deep = await tools.ListStations(minDepth: 75);
        var middle = await tools.ListStations(minDepth: 40, maxDepth: 60);

        Assert.Equal(new[] { "deep.3", "deep.4" }, deep.Data!.Stations.Select(s => s.Name).ToArray());
        Assert.Equal("deep.2", Assert.Single(middle.Data!.Stations).Name);
    }

    [Fact]
    public async Task List_stations_reports_absolute_altitude_from_a_georeferenced_fix()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListStations(surveyPrefix: "upper");

        var fixedStation = result.Data!.Stations.Single(s => s.Name == "upper.1");
        Assert.Equal(800.0, fixedStation.Position!.AbsoluteAltitude!.Value, 3);
        // Horizontal fix coordinates are not folded into the local frame (metres, anchor-local).
        Assert.Equal(0.0, fixedStation.Position.East, 3);
    }

    [Fact]
    public async Task Query_legs_lists_every_leg_and_marks_bearings_magnetic()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.QueryLegs();

        Assert.Equal(3, result.Data!.Total);
        Assert.True(result.Data.BearingsAreMagnetic);
        Assert.Null(result.Data.PositionCaveat);   // no depth filter → no caveat
    }

    [Fact]
    public async Task Query_legs_filters_by_compass_direction()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var north = await tools.QueryLegs(direction: "N");
        var east = await tools.QueryLegs(direction: "E");

        Assert.Equal(2, north.Data!.Legs.Count);   // the two plumbs, bearing 0
        Assert.All(north.Data.Legs, l => Assert.Equal(0.0, l.Bearing!.Value, 3));
        Assert.Equal("deep.3", Assert.Single(east.Data!.Legs).From);
    }

    [Fact]
    public async Task Query_legs_bearing_range_wraps_through_north()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.QueryLegs(minBearing: 350, maxBearing: 10);

        Assert.Equal(2, result.Data!.Legs.Count);   // bearing 0 falls in the wrapped band
    }

    [Fact]
    public async Task Query_legs_filters_by_depth_band_and_carries_the_caveat()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.QueryLegs(minDepth: 60);

        var leg = Assert.Single(result.Data!.Legs);   // only deep.3→deep.4 lies entirely below 60 m
        Assert.Equal("deep.3", leg.From);
        Assert.Equal("deep.4", leg.To);
        Assert.NotNull(result.Data.PositionCaveat);
    }

    [Fact]
    public async Task Query_legs_filters_by_inclination()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var descending = await tools.QueryLegs(maxClino: -45);

        Assert.Equal(2, descending.Data!.Legs.Count);
        Assert.All(descending.Data.Legs, l => Assert.True(l.Clino!.Value <= -45));
    }

    [Fact]
    public async Task Query_legs_rejects_an_unknown_direction()
    {
        using var fixture = FixtureWorkspace.CreateVertical();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.QueryLegs(direction: "sideways");

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public async Task Find_equate_candidates_flags_coincident_stations_in_different_pieces()
    {
        using var fixture = FixtureWorkspace.CreateTwinGeoreferenced();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindEquateCandidates();

        Assert.Equal(2, result.Data!.ComparablePieces);
        var candidate = Assert.Single(result.Data.Candidates);
        Assert.Equal("twin.a.a1", candidate.StationA);
        Assert.Equal("twin.b.b1", candidate.StationB);
        Assert.True(candidate.Distance < 0.5);           // they land on the same absolute point
        Assert.NotEqual(candidate.ComponentA, candidate.ComponentB);
        Assert.Equal("UTM33", candidate.CoordinateSystem);
    }

    [Fact]
    public async Task Find_equate_candidates_respects_the_threshold()
    {
        using var fixture = FixtureWorkspace.CreateTwinGeoreferenced();
        var tools = await LoadedToolsAsync(fixture);

        // a1/b1 coincide (0 m); the next-closest cross pairs (a0↔b1, a1↔b0) are 50 m; a0↔b0 is 100 m.
        var tiny = await tools.FindEquateCandidates(threshold: 0.5);
        var wide = await tools.FindEquateCandidates(threshold: 60);

        Assert.Single(tiny.Data!.Candidates);            // just the coincident pair
        Assert.True(wide.Data!.Candidates.Count >= 2);   // the 50 m cross pairs come in too
    }

    [Fact]
    public async Task Find_equate_candidates_needs_two_georeferenced_pieces()
    {
        // 'disconnected' has one georeferenced piece (upper, UTM33) and one ungrounded (island).
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindEquateCandidates();

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Candidates);
        Assert.Equal(1, result.Data.ComparablePieces);
        Assert.NotNull(result.Data.Note);
    }

    [Fact]
    public async Task Find_equate_candidates_rejects_a_nonpositive_threshold()
    {
        using var fixture = FixtureWorkspace.CreateTwinGeoreferenced();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindEquateCandidates(threshold: 0);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    private static async Task<GraphTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new GraphTools(host);
    }
}
