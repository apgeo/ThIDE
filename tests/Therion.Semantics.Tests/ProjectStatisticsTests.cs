// PROJ-03 / PROJ-07 / PROJ-02 — project analytics (survey tree, totals, unreferenced scraps).

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ProjectStatisticsTests
{
    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = path.EndsWith(".th2", StringComparison.OrdinalIgnoreCase)
                ? new Th2Parser().Parse(path, text)
                : new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private const string NestedCave = """
        survey cave
          centreline
            data normal from to length compass clino
            1 2 10 0 0
            2 3 10 0 90
          endcentreline
          survey upper
            centreline
              data normal from to length compass clino
              u1 u2 5 0 0
            endcentreline
          endsurvey
        endsurvey
        """;

    [Fact]
    public void Survey_tree_rolls_up_counts_and_length()
    {
        var tree = ProjectStatistics.BuildSurveyTree(Ws(("/p/cave.th", NestedCave)));

        var cave = Assert.Single(tree);
        Assert.Equal("cave", cave.Name);
        Assert.Equal(5, cave.Stations);          // 1,2,3 + upper.u1,u2
        Assert.Equal(3, cave.Shots);             // 2 + 1
        Assert.Equal(25, cave.Length, 3);        // 20 + 5

        var upper = Assert.Single(cave.Children);
        Assert.Equal("upper", upper.Name);
        Assert.Equal(2, upper.Stations);
        Assert.Equal(5, upper.Length, 3);
    }

    [Fact]
    public void Totals_compute_length_depth_and_counts()
    {
        var totals = ProjectStatistics.ComputeTotals(Ws(("/p/cave.th", NestedCave)));

        Assert.Equal(2, totals.SurveyCount);
        Assert.Equal(5, totals.StationCount);
        Assert.Equal(3, totals.ShotCount);
        Assert.Equal(25, totals.TotalLength, 3);
        Assert.Equal(10, totals.VerticalRange, 3); // the 90° clino leg climbs 10 m
    }

    [Fact]
    public void Unreferenced_scraps_are_flagged()
    {
        var th = """
            survey cave
              map m1
                s1
              endmap
            endsurvey
            """;
        var th2 = """
            scrap s1
            endscrap
            scrap s2
            endscrap
            """;
        var orphans = ProjectStatistics.UnreferencedScraps(Ws(("/p/cave.th", th), ("/p/cave.th2", th2)));
        Assert.Contains("s2", orphans);
        Assert.DoesNotContain("s1", orphans);
    }
}
