// semantic effects of the typed centreline commands:
//   * station flags/comments bind to the station symbol;
//   * metadata commands no longer pollute the station model (no fake "extend"/"mark" stations);
//   * data-row arity validation is accurate (no false positives on coalesced station names);
//   * the input coordinate system is captured.

using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class CentrelineSemanticsTests
{
    private static SemanticModel Bind(string source)
    {
        var parse = new ThParser().Parse("/p/a.th", source);
        return new SemanticBinder().Bind(parse.Value!);
    }

    [Fact]
    public void Metadata_commands_do_not_create_phantom_stations()
    {
        var model = Bind("""
            survey s
              centreline
                cs UTM33
                units length metres
                calibrate compass 35.0
                declination 3.0 degrees
                mark 1 fixed
                data normal from to length compass clino
                1 2 5.0 010 0
                extend left 1 2
                2 3 6.0 020 0
              endcentreline
            endsurvey
            """);

        var names = model.Stations.Keys.Select(k => k.ToString()).ToHashSet();
        // Only real stations s.1 s.2 s.3 — never "extend", "left", "mark", "units", "calibrate".
        foreach (var bogus in new[] { "extend", "left", "mark", "units", "calibrate", "declination", "cs" })
            Assert.DoesNotContain(names, n => n.EndsWith("." + bogus) || n == bogus);
        Assert.Equal(2, model.Shots.Length); // exactly the two real shots
    }

    [Fact]
    public void Station_command_flags_bind_to_station_symbol()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5.0 010 0
                station 1 "entrance pitch" entrance
                station 2 "ongoing lead" continuation
              endcentreline
            endsurvey
            """);

        var s1 = model.Stations[QualifiedName.Parse("s.1")];
        var s2 = model.Stations[QualifiedName.Parse("s.2")];
        Assert.True(s1.IsEntrance);
        Assert.Equal("entrance pitch", s1.Comment);
        Assert.True(s2.IsContinuation);
    }

    [Fact]
    public void Mark_fixed_tags_listed_stations()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5.0 010 0
                mark 1 2 fixed
              endcentreline
            endsurvey
            """);
        Assert.Equal("fixed", model.Stations[QualifiedName.Parse("s.1")].MarkType);
        Assert.Equal("fixed", model.Stations[QualifiedName.Parse("s.2")].MarkType);
    }

    [Fact]
    public void Input_coordinate_system_is_captured()
    {
        var model = Bind("""
            survey s
              centreline
                cs EPSG:3794
                fix 1 423000 5092000 1200
                data normal from to length compass clino
                1 2 5.0 010 0
              endcentreline
            endsurvey
            """);
        Assert.Equal("EPSG:3794", model.InputCoordinateSystem);
    }

    [Fact]
    public void Correct_rows_produce_no_arity_warnings()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5.0 010 0
                2 38a 6.0 020 0
                38a 3 7.0 030 0
              endcentreline
            endsurvey
            """);
        Assert.DoesNotContain(model.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataRowArity);
        // The alphanumeric station binds as a whole, not as "38".
        Assert.True(model.Stations.ContainsKey(QualifiedName.Parse("s.38a")));
        Assert.False(model.Stations.ContainsKey(QualifiedName.Parse("s.38")));
    }

    [Fact]
    public void Row_with_too_few_columns_warns()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5.0
              endcentreline
            endsurvey
            """);
        Assert.Contains(model.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataRowArity);
    }

    [Fact]
    public void Ignoreall_suppresses_arity_warnings()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to tape compass clino ignoreall
                1 2 16.07 009 2 some trailing note
              endcentreline
            endsurvey
            """);
        Assert.DoesNotContain(model.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataRowArity);
    }

    // ---- (extended): per-value validation of data rows -----------------------------

    [Fact]
    public void Non_numeric_measurement_values_are_flagged_as_errors_naming_the_reading()
    {
        // The user's example: each row has exactly one column that isn't a valid number.
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                HRUS2 . 4.85 14x1.4 57.3
                HRUS2 . 5.15x 101.2 49.9
                HRUS2 . 3.96 72.3 49.7zxzxzx
                HRUS2 . 2.07 13.6 z
              endcentreline
            endsurvey
            """);

        var invalid = model.Diagnostics
            .Where(d => d.Code == SemanticDiagnosticCodes.DataValueInvalid)
            .ToArray();
        Assert.Equal(4, invalid.Length);
        Assert.Contains(invalid, d => d.Message.Contains("compass") && d.Message.Contains("14x1.4"));
        Assert.Contains(invalid, d => d.Message.Contains("length") && d.Message.Contains("5.15x"));
        Assert.Contains(invalid, d => d.Message.Contains("clino") && d.Message.Contains("49.7zxzxzx"));
        Assert.Contains(invalid, d => d.Message.Contains("clino") && d.Message.Contains("'z'"));
    }

    [Fact]
    public void Valid_rows_produce_no_value_diagnostics()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 12.5 0 -5
                2 3 8.0 359.9 90
              endcentreline
            endsurvey
            """);
        Assert.DoesNotContain(model.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataValueInvalid);
        Assert.DoesNotContain(model.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataValueRange);
    }

    [Fact]
    public void Compass_over_360_in_degrees_warns_but_grads_allow_it()
    {
        var degrees = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 390 0
              endcentreline
            endsurvey
            """);
        Assert.Contains(degrees.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataValueRange);

        var grads = Bind("""
            survey s
              centreline
                units compass grads
                data normal from to length compass clino
                1 2 5 390 0
              endcentreline
            endsurvey
            """);
        Assert.DoesNotContain(grads.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataValueRange);
    }

    [Fact]
    public void Splay_marker_endpoints_set_the_splay_flag_and_create_no_phantom_station()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                1 . 3.2 120 -8
                1 - 4.0 200 12
              endcentreline
            endsurvey
            """);

        // Neither "." nor "-" becomes a real station.
        var names = model.Stations.Keys.Select(k => k.ToString()).ToHashSet();
        Assert.DoesNotContain(names, n => n.EndsWith(".") || n.EndsWith("-") || n is "." or "-");
        // Both shots are splays.
        Assert.Equal(2, model.Shots.Length);
        Assert.All(model.Shots, sh => Assert.True((sh.Flags & ShotFlags.Splay) != 0));
        // The real station is still bound.
        Assert.True(model.Stations.ContainsKey(QualifiedName.Parse("s.1")));
    }
}
