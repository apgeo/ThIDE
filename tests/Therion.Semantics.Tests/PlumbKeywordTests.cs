// A clino written as the Therion/Survex plumb keyword (`down`, `up`, `-v`, ...) is a vertical leg,
// not a missing reading.

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class PlumbKeywordTests
{
    private static WorkspaceSemanticModel Ws(string text)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/p/s.th"] = new ThParser().Parse("/p/s.th", text),
        };
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private const string Pitch = """
        survey s
          centreline
            data normal from to length compass clino
            1 2 30 - down
            station 1 "" entrance
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Down_keyword_is_a_plumb_for_the_position_estimate()
    {
        var set = StationPositionEstimator.Estimate(Ws(Pitch));
        var bottom = set.For(QualifiedName.Parse("s.2"));

        Assert.NotNull(bottom);
        Assert.Equal(30.0, bottom!.Depth, 6);
        Assert.True(bottom.VerticalReliable);
    }

    [Fact]
    public void Down_keyword_counts_in_the_vertical_range()
    {
        Assert.Equal(30.0, ProjectStatistics.ComputeTotals(Ws(Pitch)).VerticalRange, 6);
    }

    [Fact]
    public void Down_keyword_is_not_a_missing_clino()
    {
        var q = DataAnalytics.DataQuality(Ws(Pitch));

        Assert.Equal(0, q.MissingClino);
        Assert.Equal(0, q.MissingCompass);   // a plumb needs no bearing
    }
}
