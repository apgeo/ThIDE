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
}
