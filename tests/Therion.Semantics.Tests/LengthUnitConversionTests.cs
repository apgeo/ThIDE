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

    [Fact]
    public void Units_factor_and_backsight_tape_are_converted()
    {
        // `units tape 0.1 metres` = decimetres; a backtape-only leg takes the same scale.
        var ws = Ws("""
            survey s
              centreline
                units tape 0.1 metres
                data normal from to backtape compass clino
                1 2 100 0 0
              endcentreline
            endsurvey
            """);

        Assert.Equal(10.0, ProjectStatistics.ComputeTotals(ws).TotalLength, 6);
    }

    [Fact]
    public void Grads_are_converted_to_degrees()
    {
        var ws = Ws("""
            survey s
              centreline
                units compass clino grads
                data normal from to length compass clino
                1 2 10 100 -100
              endcentreline
            endsurvey
            """);

        var shot = Assert.Single(Assert.Single(ws.PerFile.Values).Shots);
        Assert.Equal(90.0, shot.Compass!.Value, 6);
        Assert.Equal(-90.0, shot.Clino!.Value, 6);
    }
}
