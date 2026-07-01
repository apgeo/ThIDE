// typed centreline metadata commands + data-style awareness.
// Validates that units / calibrate / declination / cs / station / mark / extend etc. are parsed
// as real commands (not mis-parsed as data rows) and that the data-style validation behaves.
// thbook v6.4.0 §"centreline".

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class CentrelineCommandTests
{
    private static CentrelineCommand ParseCentreline(string body)
    {
        var r = new ThParser().Parse("/p/a.th", $$"""
            survey s
              centreline
            {{body}}
              endcentreline
            endsurvey
            """);
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return r.Value!.Children.OfType<SurveyCommand>().Single()
            .Children.OfType<CentrelineCommand>().Single();
    }

    [Fact]
    public void Units_command_is_parsed_with_quantities_and_unit()
    {
        var u = ParseCentreline("    units length meters").Children.OfType<UnitsCommand>().Single();
        Assert.Equal("meters", u.Unit);
        Assert.Contains("length", u.Quantities);
    }

    [Fact]
    public void Units_with_factor_captures_the_factor()
    {
        var u = ParseCentreline("    units tape 0.3048 feet").Children.OfType<UnitsCommand>().Single();
        Assert.Equal(0.3048, u.Factor);
        Assert.Equal("feet", u.Unit);
        Assert.Contains("tape", u.Quantities);
    }

    [Fact]
    public void Calibrate_captures_zero_error_and_optional_scale()
    {
        var c = ParseCentreline("    calibrate tape +3.00 1.0").Children.OfType<CalibrateCommand>().Single();
        Assert.Contains("tape", c.Quantities);
        Assert.Equal(3.00, c.ZeroError);
        Assert.Equal(1.0, c.Scale);
    }

    [Fact]
    public void Declination_single_value_form_is_decoded()
    {
        var d = ParseCentreline("    declination 3.5 degrees").Children.OfType<DeclinationCommand>().Single();
        Assert.Equal(3.5, d.SingleValue);
        Assert.Equal("degrees", d.Unit);
        Assert.False(d.IsReset);
    }

    [Fact]
    public void Declination_dated_list_does_not_set_single_value()
    {
        var d = ParseCentreline("    declination 1990 2.0 2000 3.0 degrees")
            .Children.OfType<DeclinationCommand>().Single();
        Assert.Null(d.SingleValue); // ambiguous dated list — kept raw
        Assert.False(d.IsReset);
    }

    [Fact]
    public void Bare_declination_is_a_reset()
    {
        var d = ParseCentreline("    declination").Children.OfType<DeclinationCommand>().Single();
        Assert.True(d.IsReset);
    }

    [Fact]
    public void Cs_command_recovers_epsg_code_split_by_tokenizer()
    {
        var cs = ParseCentreline("    cs EPSG:3794").Children.OfType<CsCommand>().Single();
        Assert.Equal("EPSG:3794", cs.System);
    }

    [Fact]
    public void Mark_command_separates_stations_from_type()
    {
        var m = ParseCentreline("    mark 1 9 fixed").Children.OfType<MarkCommand>().Single();
        Assert.Equal("fixed", m.MarkType);
        Assert.Equal(new[] { "1", "9" }, m.Stations);
    }

    [Fact]
    public void Station_command_captures_comment_and_flags()
    {
        var s = ParseCentreline("""    station 4 "pit to explore" continuation""")
            .Children.OfType<StationCommand>().Single();
        Assert.Equal("4", s.Station);
        Assert.Equal("pit to explore", s.Comment);
        Assert.Contains("continuation", s.Flags);
    }

    [Fact]
    public void Extend_command_keeps_spec_and_stations()
    {
        var e = ParseCentreline("    extend left H27 H28").Children.OfType<ExtendCommand>().Single();
        Assert.Equal("left", e.Spec);
        Assert.Equal(new[] { "H27", "H28" }, e.Stations);
    }

    [Fact]
    public void Extend_after_data_is_not_mis_parsed_as_a_shot()
    {
        // Regression: `extend left H27 H28` after a data command previously became a DataRow
        // (from="extend", to="left"), polluting the model. It must now be an ExtendCommand.
        var cl = ParseCentreline("""
                data normal from to length compass clino
                1 2 5.0 010 0
                extend left H27 H28
            """);
        Assert.Single(cl.Children.OfType<ExtendCommand>());
        Assert.Single(cl.Children.OfType<DataRow>()); // only the real shot row
        Assert.DoesNotContain(cl.Children.OfType<DataRow>(),
            r => r.Values.Length > 0 && r.Values[0] == "extend");
    }

    [Fact]
    public void Infer_break_walls_vthreshold_parse()
    {
        var cl = ParseCentreline("""
                infer plumbs on
                break
                walls on
                vthreshold 2 degrees
            """);
        Assert.True(cl.Children.OfType<InferCommand>().Single().On);
        Assert.Equal("plumbs", cl.Children.OfType<InferCommand>().Single().What);
        Assert.Single(cl.Children.OfType<BreakCommand>());
        Assert.Equal("on", cl.Children.OfType<WallsCommand>().Single().Value);
        Assert.Single(cl.Children.OfType<VThresholdCommand>());
    }

    [Fact]
    public void Sd_and_grade_parse()
    {
        var cl = ParseCentreline("""
                sd length 0.1 metres
                grade BCRA5
            """);
        var sd = cl.Children.OfType<SdCommand>().Single();
        Assert.Equal(0.1, sd.Value);
        Assert.Contains("length", sd.Quantities);
        Assert.Contains("BCRA5", cl.Children.OfType<GradeCommand>().Single().Grades);
    }

    // ---- data-style validation -------------------------------------

    [Fact]
    public void Unknown_data_style_warns()
    {
        var r = new ThParser().Parse("/p/a.th", """
            survey s
              centreline
                data wobble from to length compass clino
              endcentreline
            endsurvey
            """);
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownDataStyle);
    }

    [Fact]
    public void Unknown_data_reading_warns()
    {
        var r = new ThParser().Parse("/p/a.th", """
            survey s
              centreline
                data normal from to length wobble clino
              endcentreline
            endsurvey
            """);
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownDataReading);
    }

    [Fact]
    public void Known_styles_and_readings_do_not_warn()
    {
        foreach (var decl in new[]
        {
            "data normal from to tape compass clino",
            "data diving from to tape compass depth",
            "data cartesian from to northing easting altitude",
            "data dimensions station left right up down",
            "data nosurvey from to",
        })
        {
            var r = new ThParser().Parse("/p/a.th", $$"""
                survey s
                  centreline
                    {{decl}}
                  endcentreline
                endsurvey
                """);
            Assert.DoesNotContain(r.Diagnostics,
                d => d.Code.Value is DiagnosticCodes.UnknownDataStyle or DiagnosticCodes.UnknownDataReading);
        }
    }

    [Fact]
    public void Station_named_with_digit_and_letter_is_one_value()
    {
        // Regression: "38a" used to tokenize as 38 + a, yielding 6 values for a 5-column row
        // and binding the station as "38". CoalesceValues must keep it as a single value.
        var cl = ParseCentreline("""
                data normal from to length compass clino
                38a 39 5.0 010 0
            """);
        var row = cl.Children.OfType<DataRow>().Single();
        Assert.Equal(5, row.Values.Length);
        Assert.Equal("38a", row.Values[0]);
    }
}
