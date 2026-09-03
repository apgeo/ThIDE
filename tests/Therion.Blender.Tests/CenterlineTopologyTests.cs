// Known-answer tests for the network shape of a centerline: de-duplicated adjacency and
// degree, junction and dead-end classification, per-leg length/azimuth/dip, shortest paths,
// the counters for what the build lost, and the reduced graph the measures are defined over.

using Therion.Blender;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class CenterlineTopologyTests
{
    // ---- adjacency, degree, classification ----

    [Fact]
    public void A_passage_surveyed_twice_is_one_neighbour_not_two()
    {
        var model = Model(
            [P(0, 0, 0), P(10, 0, 0), P(20, 0, 0)],
            [(0, 1), (1, 0), (1, 2)]);

        var graph = CenterlineGraph.Build(model);

        // Both lists are published and they disagree on purpose: three legs were used, but
        // only two passages exist, and degree is a statement about passages.
        Assert.Equal(3, graph.Edges.Count);
        Assert.Equal(2, graph.DistinctEdges.Count);
        Assert.Equal([(0, 1), (1, 2)], graph.DistinctEdges);
        Assert.Equal([0, 2], graph.Neighbours(1));
        Assert.Equal(2, graph.Degree(1));
        Assert.Equal(1, graph.Degree(0));
        Assert.Equal(0, graph.Degree(42));
    }

    [Fact]
    public void Junctions_are_where_three_passages_meet_and_extremities_are_where_one_stops()
    {
        var graph = CenterlineGraph.Build(YModel());

        Assert.Equal([0], graph.Junctions);
        Assert.Equal([1, 2, 3], graph.Extremities);
    }

    // ---- per-leg shape ----

    [Fact]
    public void A_leg_carries_its_length_bearing_and_inclination()
    {
        var model = Model(
            [P(0, 0, 0), P(3, 4, 0), P(0, 10, 10), P(0, 0, 7)],
            [(0, 1), (0, 2), (0, 3)]);
        var graph = CenterlineGraph.Build(model);

        var flat = graph.EdgeGeometry(0, 1);
        Assert.Equal(5.0, flat.Length, 12);
        Assert.Equal(5.0, flat.PlanLength, 12);
        Assert.Equal(36.8698976458, flat.AzimuthDegrees, 8); // atan2(3, 4)
        Assert.Equal(0.0, flat.DipDegrees, 12);

        var climbing = graph.EdgeGeometry(0, 2);
        Assert.Equal(0.0, climbing.AzimuthDegrees, 12); // due north
        Assert.Equal(45.0, climbing.DipDegrees, 12);
        Assert.Equal(-45.0, graph.EdgeGeometry(2, 0).DipDegrees, 12);

        // A pitch points nowhere on the compass, and calling it north would drop it into
        // whichever orientation bin happens to begin at zero.
        var pitch = graph.EdgeGeometry(0, 3);
        Assert.True(double.IsNaN(pitch.AzimuthDegrees));
        Assert.Equal(0.0, pitch.PlanLength, 12);
        Assert.Equal(90.0, pitch.DipDegrees, 12);
        Assert.Equal(-90.0, graph.EdgeGeometry(3, 0).DipDegrees, 12);

        Assert.Equal(graph.DistinctEdges.Count, graph.EdgeGeometries().Count);
    }

    [Fact]
    public void Bearings_run_clockwise_from_north_over_the_whole_circle()
    {
        var model = Model(
            [P(0, 0, 0), P(0, 1, 0), P(1, 0, 0), P(0, -1, 0), P(-1, 0, 0)],
            [(0, 1), (0, 2), (0, 3), (0, 4)]);
        var graph = CenterlineGraph.Build(model);

        Assert.Equal(0.0, graph.EdgeGeometry(0, 1).AzimuthDegrees, 9);
        Assert.Equal(90.0, graph.EdgeGeometry(0, 2).AzimuthDegrees, 9);
        Assert.Equal(180.0, graph.EdgeGeometry(0, 3).AzimuthDegrees, 9);
        Assert.Equal(270.0, graph.EdgeGeometry(0, 4).AzimuthDegrees, 9);
    }

    // ---- shortest paths ----

    [Fact]
    public void Shortest_paths_answer_in_metres_or_in_legs_and_stop_at_the_component_edge()
    {
        var model = Model(
            [P(0, 0, 0), P(10, 0, 0), P(30, 0, 0), P(0, 100, 0), P(1, 100, 0)],
            [(0, 1), (1, 2), (3, 4)]);
        var graph = CenterlineGraph.Build(model);

        var metres = graph.ShortestPathLengths(0);
        Assert.Equal(0.0, metres[0], 12);
        Assert.Equal(10.0, metres[1], 12);
        Assert.Equal(30.0, metres[2], 12);
        Assert.DoesNotContain(3, metres.Keys); // a separate part of the cave is not "far", it is unreachable

        var legs = graph.ShortestPathLengths(0, weighted: false);
        Assert.Equal(2.0, legs[2], 12);
    }

    // ---- what the build lost ----

    [Fact]
    public void A_clean_survey_admits_to_losing_nothing()
    {
        var graph = CenterlineGraph.Build(YModel());

        Assert.Equal(0, graph.DroppedShotCount);
        Assert.Equal(0, graph.MergedStationCount);
    }

    [Fact]
    public void Stations_at_one_point_become_one_node_and_the_legs_that_collapse_are_counted()
    {
        var model = new CaveModel
        {
            Stations =
            [
                Station(0, "a", P(0, 0, 0)),
                Station(1, "b", P(10, 0, 0)),
                Station(2, "b_again", P(10, 0, 0)), // same point as b
            ],
            Shots =
            [
                Leg(P(0, 0, 0), P(10, 0, 0)),   // a-b, kept
                Leg(P(10, 0, 0), P(10, 0, 0)),  // b-b_again, collapsed by the merge
                Leg(P(0, 0, 0), P(99, 99, 99)), // stands at no station
                Splay(P(0, 0, 0), P(1, 1, 1)),  // never network, so never a loss
            ],
        };

        var graph = CenterlineGraph.Build(model);

        Assert.Equal(1, graph.MergedStationCount);
        Assert.Equal(2, graph.DroppedShotCount);
        Assert.Single(graph.Edges);
    }

    // ---- the reduced graph ----

    [Fact]
    public void A_chain_of_stations_reduces_to_one_branch_carrying_the_whole_walk()
    {
        var model = Model(
            [P(0, 0, 0), P(10, 0, 0), P(20, 0, 0), P(30, 0, 0)],
            [(0, 1), (1, 2), (2, 3)]);

        var reduced = CenterlineGraph.Build(model).Reduce();

        Assert.Single(reduced.Branches);
        Assert.Equal([0, 3], reduced.Nodes);
        Assert.Equal([0, 1, 2, 3], reduced.Branches[0].StationIndices);
        Assert.Equal(30.0, reduced.Branches[0].Length, 12);
        Assert.Equal(30.0, reduced.Branches[0].ChordLength, 12);
        Assert.Equal(1.0, reduced.Branches[0].Tortuosity!.Value, 12);
        Assert.False(reduced.Branches[0].IsLoop);
        Assert.Equal(1, reduced.ComponentCount);
        Assert.Equal(0, reduced.CyclomaticNumber);
    }

    [Fact]
    public void A_branch_that_wanders_is_longer_than_the_line_between_its_ends()
    {
        // Right-angled dogleg: 3 + 4 walked, 5 straight.
        var model = Model(
            [P(0, 0, 0), P(3, 0, 0), P(3, 4, 0)],
            [(0, 1), (1, 2)]);

        var branch = CenterlineGraph.Build(model).Reduce().Branches[0];

        Assert.Equal(7.0, branch.Length, 12);
        Assert.Equal(5.0, branch.ChordLength, 12);
        Assert.Equal(1.4, branch.Tortuosity!.Value, 12);
    }

    [Fact]
    public void A_junction_with_three_legs_reduces_to_three_branches_and_four_nodes()
    {
        var reduced = CenterlineGraph.Build(YModel()).Reduce();

        Assert.Equal(3, reduced.Branches.Count);
        Assert.Equal([0, 1, 2, 3], reduced.Nodes);
        Assert.Equal(3, reduced.Degree(0));
        Assert.Equal(1, reduced.Degree(1));
        Assert.Equal(0, reduced.CyclomaticNumber);
    }

    [Fact]
    public void A_closed_loop_with_no_junction_survives_as_one_branch_that_returns_to_itself()
    {
        // A square: every station has two legs, so nothing anchors the contraction.
        var model = Model(
            [P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0)],
            [(0, 1), (1, 2), (2, 3), (3, 0)]);

        var reduced = CenterlineGraph.Build(model).Reduce();

        Assert.Single(reduced.Branches);
        var loop = reduced.Branches[0];
        Assert.True(loop.IsLoop);
        Assert.Equal(40.0, loop.Length, 12);
        Assert.Equal(0.0, loop.ChordLength, 12);
        Assert.Null(loop.Tortuosity); // there is no straight line between a point and itself
        Assert.Equal([0], reduced.Nodes);
        Assert.Equal(2, reduced.Degree(0)); // both ends of the loop meet there
        Assert.Equal(1, reduced.CyclomaticNumber);
    }

    [Fact]
    public void Two_junctions_joined_by_three_separate_passages_keep_all_three()
    {
        // Folding parallel passages into one edge would destroy the loops the reduction exists
        // to expose, so the branches are kept as a list rather than as a simple graph.
        var model = Model(
            [P(0, 0, 0), P(0, 30, 0), P(-10, 15, 0), P(0, 15, 0), P(10, 15, 0)],
            [(0, 2), (2, 1), (0, 3), (3, 1), (0, 4), (4, 1)]);

        var reduced = CenterlineGraph.Build(model).Reduce();

        Assert.Equal(3, reduced.Branches.Count);
        Assert.Equal([0, 1], reduced.Nodes);
        Assert.Equal(3, reduced.Degree(0));
        Assert.Equal(3, reduced.Degree(1));
        Assert.Equal(2, reduced.CyclomaticNumber);
    }

    [Fact]
    public void A_loop_hanging_off_a_junction_is_one_branch_and_the_tail_is_another()
    {
        //  4 - 0 < 1 - 2 > back to 0
        var model = Model(
            [P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0), P(-20, 0, 0)],
            [(0, 1), (1, 2), (2, 3), (3, 0), (0, 4)]);

        var reduced = CenterlineGraph.Build(model).Reduce();

        Assert.Equal(2, reduced.Branches.Count);
        Assert.Single(reduced.Branches, b => b.IsLoop && b.First == 0);
        Assert.Equal([0, 4], reduced.Nodes);
        Assert.Equal(3, reduced.Degree(0)); // two loop ends and the tail
        Assert.Equal(1, reduced.Degree(4));
        Assert.Equal(1, reduced.CyclomaticNumber);
    }

    [Fact]
    public void A_maze_and_a_branchwork_of_the_same_size_do_not_reduce_alike()
    {
        // The separation the metrics rest on: a grid of loops keeps its junctions, a tree of
        // dead ends does not gain any, and the two disagree on every count that follows.
        var maze = CenterlineGraph.Build(GridModel(3, 3)).Reduce();
        var branchwork = CenterlineGraph.Build(CombModel(4, 2)).Reduce();

        Assert.Equal(4, maze.CyclomaticNumber);   // four square cells
        Assert.Equal(0, branchwork.CyclomaticNumber);
        Assert.True(MeanDegree(maze) > MeanDegree(branchwork));
        Assert.DoesNotContain(maze.Nodes, n => maze.Degree(n) == 1); // a maze has no dead end
        Assert.Contains(branchwork.Nodes, n => branchwork.Degree(n) == 1);
    }

    [Fact]
    public void Separate_parts_of_a_cave_stay_separate_through_the_reduction()
    {
        var model = Model(
            [P(0, 0, 0), P(10, 0, 0), P(0, 500, 0), P(10, 500, 0)],
            [(0, 1), (2, 3)]);

        var reduced = CenterlineGraph.Build(model).Reduce();

        Assert.Equal(2, reduced.Branches.Count);
        Assert.Equal(2, reduced.ComponentCount);
        Assert.Equal(0, reduced.CyclomaticNumber); // branches 2 − nodes 4 + components 2
    }

    [Fact]
    public void The_reduction_is_the_same_every_time_it_is_built()
    {
        var model = GridModel(3, 3);
        var first = CenterlineGraph.Build(model).Reduce();
        var second = CenterlineGraph.Build(model).Reduce();

        Assert.Equal(
            first.Branches.Select(b => string.Join(',', b.StationIndices)),
            second.Branches.Select(b => string.Join(',', b.StationIndices)));
    }

    // ---- fixtures ----

    private static double MeanDegree(CenterlineReducedGraph g) =>
        g.Nodes.Select(g.Degree).Average();

    /// <summary>A junction with three legs radiating from it.</summary>
    private static CaveModel YModel() => Model(
        [P(0, 0, 0), P(10, 0, 0), P(-10, 0, 0), P(0, 10, 0)],
        [(0, 1), (0, 2), (0, 3)]);

    /// <summary>A rectangular grid of passages: every interior station is a junction and every
    /// cell is a loop.</summary>
    private static CaveModel GridModel(int columns, int rows)
    {
        var points = new List<CaveVector3>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                points.Add(P(c * 10, r * 10, 0));

        var legs = new List<(int, int)>();
        int At(int c, int r) => (r * columns) + c;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                if (c + 1 < columns) legs.Add((At(c, r), At(c + 1, r)));
                if (r + 1 < rows) legs.Add((At(c, r), At(c, r + 1)));
            }
        return Model(points, legs);
    }

    /// <summary>A trunk passage with side passages off it and no loop anywhere.</summary>
    private static CaveModel CombModel(int trunkStations, int teethPerNode)
    {
        var points = new List<CaveVector3>();
        for (int i = 0; i < trunkStations; i++) points.Add(P(i * 10, 0, 0));

        var legs = new List<(int, int)>();
        for (int i = 0; i + 1 < trunkStations; i++) legs.Add((i, i + 1));

        for (int i = 1; i + 1 < trunkStations; i++)
            for (int t = 0; t < teethPerNode; t++)
            {
                points.Add(P(i * 10, (t + 1) * 10 * (t % 2 == 0 ? 1 : -1), 0));
                legs.Add((i, points.Count - 1));
            }
        return Model(points, legs);
    }

    private static CaveVector3 P(double x, double y, double z) => new(x, y, z);

    private static CaveStation Station(uint id, string name, CaveVector3 position) =>
        new() { Id = id, Name = name, Position = position };

    private static CaveShot Leg(CaveVector3 from, CaveVector3 to) =>
        new() { FromPosition = from, ToPosition = to, Flags = CaveShotFlags.None };

    private static CaveShot Splay(CaveVector3 from, CaveVector3 to) =>
        new() { FromPosition = from, ToPosition = to, Flags = CaveShotFlags.Splay };

    private static CaveModel Model(
        IReadOnlyList<CaveVector3> points, IReadOnlyList<(int From, int To)> legs)
    {
        var stations = new List<CaveStation>();
        for (int i = 0; i < points.Count; i++) stations.Add(Station((uint)i, $"s{i}", points[i]));
        var shots = new List<CaveShot>();
        foreach (var (from, to) in legs) shots.Add(Leg(points[from], points[to]));
        return new CaveModel { Stations = stations, Shots = shots };
    }
}
