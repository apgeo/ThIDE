// `units length feet` must be honoured: statistics are presented in metres.

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class LengthUnitConversionTests
{
    private static WorkspaceSemanticModel Ws(string text)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/p/s.th"] = new ThParser().Parse("/p/s.th", text),
        };
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    [Fact]
    public void Feet_are_converted_to_metres_in_total_length()
    {
        var ws = Ws("""
            survey s
              centreline
                units length feet
                data normal from to length compass clino
                1 2 100 0 0
              endcentreline
            endsurvey
            """);

        Assert.Equal(30.48, ProjectStatistics.ComputeTotals(ws).TotalLength, 2);
    }
}
