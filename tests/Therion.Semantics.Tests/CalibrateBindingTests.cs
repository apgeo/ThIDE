// A `calibrate` in a centreline changes the readings that follow it, as Therion applies it: on the
// reading as written, before the unit conversion, (reading - zero error) * scale, shared by the
// backsight columns, and saved and restored by `group` like the other centreline state.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class CalibrateBindingTests
{
    private static ShotSymbol[] Shots(string body)
    {
        var text = "survey s\n  centreline\n" + body + "  endcentreline\nendsurvey\n";
        var ast = new ThParser().Parse("/p/a.th", text).Value!;
        return new SemanticBinder().Bind(ast).Shots.ToArray();
    }

    [Fact]
    public void A_zero_error_and_scale_apply_to_the_reading_before_the_unit_conversion()
    {
        var shot = Shots("""
                units length feet
                calibrate tape 1 2
                data normal from to length compass clino
                1 2 6 10 0

            """).Single();

        // (6 ft - 1 ft) * 2 = 10 ft = 3.048 m; converting first would give (1.8288 - 1) * 2.
        Assert.Equal(3.048, shot.Length!.Value, 9);
    }

    [Fact]
    public void Backsight_readings_use_the_foresight_calibration()
    {
        var shot = Shots("""
                calibrate compass 2
                calibrate clino 1
                data normal from to length backcompass backclino
                1 2 5 192 -4

            """).Single();

        Assert.Equal(10.0, shot.Compass!.Value, 9);   // (192 - 2) - 180
        Assert.Equal(5.0, shot.Clino!.Value, 9);      // -((-4) - 1)
    }

    [Fact]
    public void A_calibrated_bearing_below_zero_wraps_to_the_full_circle()
    {
        var shot = Shots("""
                calibrate compass 0.5
                data normal from to length compass clino
                1 2 5 0.2 0

            """).Single();

        Assert.Equal(359.7, shot.Compass!.Value, 9);
    }

    [Fact]
    public void A_plumb_keyword_is_not_calibrated()
    {
        var shot = Shots("""
                calibrate clino 2
                data normal from to length compass clino
                1 2 5 - down

            """).Single();

        Assert.Equal(-90.0, shot.Clino!.Value, 9);
    }

    [Fact]
    public void A_calibrate_inside_a_group_ends_with_the_group()
    {
        var shots = Shots("""
                data normal from to length compass clino
                group
                  calibrate length 0.5
                  1 2 5 0 0
                endgroup
                2 3 5 0 0

            """);

        Assert.Equal(4.5, shots[0].Length!.Value, 9);
        Assert.Equal(5.0, shots[1].Length!.Value, 9);
    }

    [Fact]
    public void A_piecewise_calibration_table_leaves_the_readings_as_written()
    {
        // Therion reads more than two numbers as a table of (reading, value) pairs; a zero error and a
        // scale taken from its first two numbers would be wrong, so the readings stay as written.
        var shot = Shots("""
                calibrate length 0 0 10 11
                data normal from to length compass clino
                1 2 5 0 0

            """).Single();

        Assert.Equal(5.0, shot.Length!.Value, 9);
    }

    [Fact]
    public void The_foresight_backsight_check_compares_calibrated_clinos()
    {
        // fore 6 - 1 = 5, back -4 - 1 = -5: an exact back-sight once both are calibrated. Read as
        // written they are 2 degrees apart, which the 1.5-degree tolerance used here reports.
        var text = "survey s\n  centreline\n    calibrate clino 1\n" +
                   "    data normal from to length compass clino backclino\n" +
                   "    1 2 10 0 6 -4\n  endcentreline\nendsurvey\n";
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        { ["/p/a.th"] = new ThParser().Parse("/p/a.th", text) };
        var ws = WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());

        var mismatches = ProjectDiagnostics.Analyze(ws, new ProjectDiagnosticOptions { ForeBackClinoToleranceDeg = 1.5 })
            .Where(d => d.Code == SemanticDiagnosticCodes.ForeBackMismatch);

        Assert.Empty(mismatches);
    }
}
