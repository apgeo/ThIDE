// connectivity graph: components, reachability, entrances, fixed points, dead-ends.

using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ConnectivityGraphTests
{
    private static ConnectivityGraph Graph(string source)
    {
        var model = new SemanticBinder().Bind(new ThParser().Parse("/p/a.th", source).Value!);
        return ConnectivityGraph.Build(model);
    }

    [Fact]
    public void Linear_survey_is_one_component()
    {
        var g = Graph("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                2 3 6 0 0
                3 4 7 0 0
              endcentreline
            endsurvey
            """);
        Assert.Single(g.Components);
        Assert.Equal(4, g.NodeCount);
        Assert.Equal(3, g.EdgeCount);
        Assert.True(g.AreConnected(QualifiedName.Parse("s.1"), QualifiedName.Parse("s.4")));
    }

    [Fact]
    public void Disconnected_pieces_form_separate_components()
    {
        var g = Graph("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                10 11 5 0 0
              endcentreline
            endsurvey
            """);
        Assert.Equal(2, g.Components.Length);
        Assert.False(g.AreConnected(QualifiedName.Parse("s.1"), QualifiedName.Parse("s.10")));
    }

    [Fact]
    public void Equated_stations_merge_components()
    {
        var g = Graph("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                10 11 5 0 0
                equate 2 10
              endcentreline
            endsurvey
            """);
        Assert.Single(g.Components);
        Assert.True(g.AreConnected(QualifiedName.Parse("s.1"), QualifiedName.Parse("s.11")));
    }

    [Fact]
    public void Entrance_and_fixed_stations_are_tracked()
    {
        var g = Graph("""
            survey s
              centreline
                cs UTM33
                fix 1 400000 5000000 1000
                data normal from to length compass clino
                1 2 5 0 0
                station 1 "the way in" entrance
              endcentreline
            endsurvey
            """);
        Assert.Contains(QualifiedName.Parse("s.1"), g.Entrances);
        Assert.Contains(QualifiedName.Parse("s.1"), g.FixedStations);
    }

    [Fact]
    public void Dead_ends_exclude_entrances_and_fixed()
    {
        var g = Graph("""
            survey s
              centreline
                fix 1 0 0 0
                data normal from to length compass clino
                1 2 5 0 0
                2 3 6 0 0
                station 1 "" entrance
              endcentreline
            endsurvey
            """);
        // Node 3 is a degree-1 leaf and neither entrance nor fixed → a candidate lead.
        Assert.Contains(QualifiedName.Parse("s.3"), g.DeadEnds);
        // Node 1 is fixed + entrance, so even though it's degree-1 it is NOT a dead-end.
        Assert.DoesNotContain(QualifiedName.Parse("s.1"), g.DeadEnds);
    }

    [Fact]
    public void Splay_shots_do_not_add_skeleton_edges()
    {
        var g = Graph("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                flags splay
                2 9 1 45 0
                flags not splay
              endcentreline
            endsurvey
            """);
        // The splay 2→9 must not connect 9 into the skeleton graph as a normal edge.
        Assert.Equal(1, g.EdgeCount);
    }
}
