// A wrapper survey fixing a station it does not contain (`fix 1@partA ...`) anchors the real
// station, not a placeholder named after the reference.

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class AtReferencedFixTests
{
    private static WorkspaceSemanticModel Ws(string text)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/p/cave.th"] = new ThParser().Parse("/p/cave.th", text),
        };
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    [Fact]
    public void Fix_through_at_reference_anchors_the_real_station()
    {
        var set = StationPositionEstimator.Estimate(Ws("""
            survey cave
              survey partA
                centreline
                  data normal from to length compass clino
                  1 2 10 0 -90
                endcentreline
              endsurvey
              centreline
                cs UTM33
                fix 1@partA 400000 5000000 800
              endcentreline
            endsurvey
            """));

        var bottom = set.For(QualifiedName.Parse("cave.partA.2"));
        Assert.NotNull(bottom);
        Assert.NotNull(bottom!.AbsoluteAltitude);
        Assert.Equal(790.0, bottom.AbsoluteAltitude!.Value, 6);
    }

    [Fact]
    public void Entrance_through_at_reference_sets_the_depth_datum()
    {
        // A 10 m climb from 1 to 2. Without the entrance the datum is the highest point (2, depth 0);
        // with the entrance on 1 the datum is 1, so 2 sits 10 m above it.
        var set = StationPositionEstimator.Estimate(Ws("""
            survey cave
              survey partA
                centreline
                  data normal from to length compass clino
                  1 2 10 0 90
                endcentreline
              endsurvey
              centreline
                station 1@partA "" entrance
              endcentreline
            endsurvey
            """));

        Assert.Equal(-10.0, set.For(QualifiedName.Parse("cave.partA.2"))!.Depth, 6);
    }
}
