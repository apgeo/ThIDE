// Edges of the .th checks the parser makes itself: a data order without shot endpoints (TH0036),
// an unterminated surface, which fix coordinate is malformed, grid-angle and declination forms, a
// layout without an id, and how input paths are read back from split tokens.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Tests;

public class ThValidationEdgeTests
{
    private static ParseResult<TherionFile> Parse(string src, ParserMode mode = ParserMode.Lenient) =>
        new ThParser().Parse("p.th", src, new ParserOptions(mode));

    private static ImmutableArray<Diagnostic> Diags(string src) => Parse(src).Diagnostics;

    private static T Single<T>(string src) where T : TherionNode =>
        Assert.Single(Descendants(Parse(src).Value!.Children).OfType<T>());

    private static IEnumerable<TherionNode> Descendants(ImmutableArray<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n is BlockCommand b)
                foreach (var c in Descendants(b.Children)) yield return c;
        }
    }

    // ---- TH0036: a data order must name its shot endpoints ----

    [Theory]
    [InlineData("data normal from length compass clino")]
    [InlineData("data normal to length compass clino")]
    [InlineData("data normal length compass clino")]
    public void Data_order_without_both_endpoints_is_flagged(string data)
    {
        var src = $"centreline\n{data}\nendcentreline\n";
        var d = Assert.Single(Diags(src), d => d.Code == DiagnosticCodes.MissingFromTo);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(src.IndexOf("data", StringComparison.Ordinal), d.Span.StartOffset);
        Assert.Equal(DiagnosticSeverity.Error,
            Assert.Single(Parse(src, ParserMode.Strict).Diagnostics, d => d.Code == DiagnosticCodes.MissingFromTo).Severity);
    }

    [Theory]
    [InlineData("data normal from to length compass clino")]
    [InlineData("data normal to from length compass clino")]
    [InlineData("data normal station length compass clino")]
    [InlineData("data diving from to length compass depthchange")]
    [InlineData("data normal newline length compass clino")]
    [InlineData("data cartesian station newline northing easting altitude")]
    [InlineData("data normal")]   // no readings at all: TH0036 needs a reading order to judge
    public void Data_order_with_endpoints_or_station_forms_is_not_flagged(string data) =>
        Assert.DoesNotContain(Diags($"centreline\n{data}\nendcentreline\n"), d => d.Code == DiagnosticCodes.MissingFromTo);

    // ---- unterminated surface ----

    [Fact]
    public void Unterminated_surface_is_reported_on_its_header_and_swallows_the_rest()
    {
        const string src = "survey a\n  surface\n    grid 0 0 10 10 2 2\n    1 2\n    3 4\nendsurvey\n";
        var r = Parse(src);
        var surface = Assert.Single(Descendants(r.Value!.Children).OfType<SurfaceCommand>());
        Assert.False(surface.IsTerminated);
        var d = Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedBlock && d.Message.Contains("'surface'"));
        Assert.Equal(src.IndexOf("surface", StringComparison.Ordinal), d.Span.StartOffset);
        Assert.Contains("'endsurface'", d.Message);
        // The body is opaque, so the survey's endsurvey went with it: the survey is reported too.
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedBlock && d.Message.Contains("'survey'"));
        Assert.Equal(DiagnosticSeverity.Error,
            Parse(src, ParserMode.Strict).Diagnostics.First(d => d.Message.Contains("'surface'")).Severity);
    }

    [Fact]
    public void Terminated_surface_spans_to_endsurface_and_ignores_blank_lines()
    {
        const string src = "surface -title \"t\"\n\n  1 2\n\nendsurface\nsurvey a\nendsurvey\n";
        var r = Parse(src);
        var surface = Assert.Single(r.Value!.Children.OfType<SurfaceCommand>());
        Assert.True(surface.IsTerminated);
        Assert.Equal("-title \"t\"", surface.OptionsRaw);
        Assert.Equal(src.IndexOf("endsurface", StringComparison.Ordinal) + "endsurface\n".Length,
            surface.Span.StartOffset + surface.Span.Length);
        Assert.Single(r.Value.Children.OfType<SurveyCommand>());
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedBlock);
    }

    // ---- fix ----

    [Theory]
    [InlineData("fix p1 1 abc 3", "y", "abc")]
    [InlineData("fix p1 1 2 abc", "z", "abc")]
    [InlineData("fix p1 x1 2 3", "x", "x1")]
    public void Malformed_fix_names_the_bad_axis_and_points_at_its_value(string line, string axis, string value)
    {
        var d = Assert.Single(Diags(line + "\n"), d => d.Code == DiagnosticCodes.MalformedFix);
        Assert.Contains($"'fix' {axis} coordinate '{value}'", d.Message);
        Assert.Equal(line.IndexOf(value, 6, StringComparison.Ordinal), d.Span.StartOffset);
        Assert.Equal(value.Length, d.Span.Length);
    }

    [Theory]
    [InlineData("fix p1")]
    [InlineData("fix p1 1 2")]
    public void Fix_without_all_coordinates_keeps_the_station_and_its_span(string line)
    {
        var fix = Single<StationFix>(line + "\n");
        Assert.Equal("p1", fix.Station);
        Assert.Equal(4, fix.StationSpan.StartOffset);
        Assert.Equal(2, fix.StationSpan.Length);
        Assert.Equal((0d, 0d, 0d), (fix.X, fix.Y, fix.Z));
        Assert.Single(Diags(line + "\n"), d => d.Code == DiagnosticCodes.MalformedFix && d.Message.Contains("requires"));
    }

    [Fact]
    public void Bare_fix_has_no_station()
    {
        var fix = Single<StationFix>("fix\n");
        Assert.Equal("", fix.Station);
        Assert.True(fix.StationSpan.IsEmpty);
    }

    [Fact]
    public void Missing_fix_coordinates_are_an_error_in_strict_mode() =>
        Assert.Equal(DiagnosticSeverity.Error,
            Assert.Single(Parse("fix p1\n", ParserMode.Strict).Diagnostics, d => d.Code == DiagnosticCodes.MalformedFix).Severity);

    [Fact]
    public void Fix_keeps_coordinates_and_the_standard_deviations_raw()
    {
        var fix = Single<StationFix>("fix p1 1.5 -2 300 0.1 0.2 0.3\n");
        Assert.Equal((1.5, -2d, 300d), (fix.X, fix.Y, fix.Z));
        Assert.Equal("0.1 0.2 0.3", fix.OptionsRaw);
        Assert.Equal(4, fix.StationSpan.StartOffset);
    }

    [Fact]
    public void Fix_with_exactly_three_coordinates_has_no_options() =>
        Assert.Equal("", Single<StationFix>("fix p1 1 2 3\n").OptionsRaw);

    // ---- grid-angle ----

    [Fact]
    public void Grid_angle_keeps_value_and_unit()
    {
        var g = Single<GridAngleCommand>("grid-angle 3.5 degrees\n");
        Assert.Equal(3.5, g.Value);
        Assert.Equal("degrees", g.Unit);
    }

    [Fact]
    public void Grid_angle_without_unit_has_a_null_unit()
    {
        var g = Single<GridAngleCommand>("grid-angle 3.5\n");
        Assert.Equal(3.5, g.Value);
        Assert.Null(g.Unit);
    }

    [Fact]
    public void Bare_grid_angle_is_malformed_with_no_value()
    {
        var g = Single<GridAngleCommand>("grid-angle\n");
        Assert.Null(g.Value);
        Assert.Null(g.Unit);
        Assert.Contains(Diags("grid-angle\n"), d => d.Code == DiagnosticCodes.MalformedMeasurement);
        Assert.Equal(DiagnosticSeverity.Error, Parse("grid-angle x\n", ParserMode.Strict).Diagnostics
            .First(d => d.Code == DiagnosticCodes.MalformedMeasurement).Severity);
    }

    // ---- declination ----

    [Fact]
    public void Single_value_declination_keeps_value_and_unit()
    {
        var d = Single<DeclinationCommand>("declination 2.5 degrees\n");
        Assert.Equal(2.5, d.SingleValue);
        Assert.Equal("degrees", d.Unit);
        Assert.False(d.IsReset);
        Assert.Equal("2.5 degrees", d.Raw);
    }

    [Fact]
    public void Dated_declination_list_has_no_single_value_and_is_not_malformed()
    {
        const string src = "declination 2000 1.5 deg 2010 2.25 deg\n";
        var d = Single<DeclinationCommand>(src);
        Assert.Null(d.SingleValue);
        Assert.Equal("deg", d.Unit);
        Assert.False(d.IsReset);
        Assert.DoesNotContain(Diags(src), x => x.Code == DiagnosticCodes.MalformedDeclination);
    }

    [Theory]
    [InlineData("declination\n")]
    [InlineData("declination -\n")]
    public void Declination_reset_forms(string src)
    {
        var d = Single<DeclinationCommand>(src);
        Assert.True(d.IsReset);
        Assert.Null(d.SingleValue);
        Assert.Null(d.Unit);
    }

    [Fact]
    public void Declination_without_a_number_is_an_error_in_strict_mode() =>
        Assert.Equal(DiagnosticSeverity.Error, Parse("declination Inc degrees\n", ParserMode.Strict).Diagnostics
            .First(d => d.Code == DiagnosticCodes.MalformedDeclination).Severity);

    // ---- layout ----

    [Fact]
    public void Layout_without_id_parses_with_an_empty_id_and_its_body()
    {
        var layout = Single<LayoutCommand>("layout\n  scale 1 500\nendlayout\n");
        Assert.Equal("", layout.Id);
        Assert.Equal("", layout.OptionsRaw);
        Assert.True(layout.IsTerminated);
        Assert.Contains(layout.Options, o => o.Key == "scale");
    }

    [Fact]
    public void Layout_keeps_its_id_and_header_options()
    {
        var layout = Single<LayoutCommand>("layout base -doc \"x\"\n  scale 1 500\nendlayout\n");
        Assert.Equal("base", layout.Id);
        Assert.Equal("-doc \"x\"", layout.OptionsRaw);
    }

    [Fact]
    public void Unterminated_layout_is_reported_on_its_header()
    {
        const string src = "survey a\nlayout base\n  scale 1 500\n";
        var r = Parse(src);
        Assert.False(Assert.Single(Descendants(r.Value!.Children).OfType<LayoutCommand>()).IsTerminated);
        var d = Assert.Single(r.Diagnostics, d => d.Message.Contains("'layout'"));
        Assert.Equal(DiagnosticCodes.UnterminatedBlock, d.Code.Value);
        Assert.Equal(src.IndexOf("layout", StringComparison.Ordinal), d.Span.StartOffset);
    }

    // ---- input paths ----

    [Theory]
    [InlineData("input 20150926_ovi/20150926_ps1.th", "20150926_ovi/20150926_ps1.th")]   // split at digit/letter, glued
    [InlineData("input a1b2/c3.th extra", "a1b2/c3.th")]                                // a space ends the path
    [InlineData("input part.th   # a comment", "part.th")]
    [InlineData("input \"dir with space/x.th\"", "dir with space/x.th")]
    [InlineData("load 2b.th", "2b.th")]
    [InlineData("input", "")]
    public void Input_path_is_rejoined_from_touching_tokens_only(string line, string path) =>
        Assert.Equal(path, Single<InputCommand>(line + "\n").Path);
}
