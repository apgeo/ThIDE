// Vertical range and extents must follow equates: two sub-surveys joined by an equate are one
// piece, so their depths add up instead of being overlaid on each other.

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class StatisticsEquateFoldingTests
{
    private static WorkspaceSemanticModel Ws(string text)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/p/cave.th"] = new ThParser().Parse("/p/cave.th", text),
        };
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private const string TwoShaftsEquated = """
        survey cave
          survey a
            centreline
              data normal from to length compass clino
              1 2 50 0 -90
            endcentreline
          endsurvey
          survey b
            centreline
              data normal from to length compass clino
              1 2 50 0 -90
            endcentreline
          endsurvey
          equate 2@a 1@b
        endsurvey
        """;

    [Fact]
    public void Vertical_range_follows_equates_between_subsurveys()
    {
        var ws = Ws(TwoShaftsEquated);

        // The estimator (equate-aware) puts the bottom at depth 100.
        var set = StationPositionEstimator.Estimate(ws);
        Assert.Equal(100.0, set.For(QualifiedName.Parse("cave.b.2"))!.Depth, 3);

        Assert.Equal(100.0, ProjectStatistics.ComputeTotals(ws).VerticalRange, 3);
    }

    [Fact]
    public void Detailed_vertical_range_and_lowest_station_follow_equates()
    {
        var t = DataAnalytics.ComputeDetailedTotals(Ws(TwoShaftsEquated));

        Assert.Equal(100.0, t.VerticalRange, 3);
        Assert.Equal("cave.b.2", t.LowestStation);
    }

    [Fact]
    public void Leg_without_compass_adds_no_horizontal_extent()
    {
        // No bearing: the horizontal direction is unknown, so no N-S extent can be claimed.
        var t = DataAnalytics.ComputeDetailedTotals(Ws("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 - -30
              endcentreline
            endsurvey
            """));

        Assert.Equal(0.0, t.NorthSouthExtent, 3);
    }
}
