// Tests for the syntax-validation checks added to close the audit gaps:
// identifier charset, block-id matching, numeric coordinates, centreline argument enums,
// .th2 object options, and .thconfig export/layout validation.
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ValidationGapTests
{
    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Th(string src) =>
        new ThParser().Parse("p.th", src).Diagnostics;
    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Th2(string src) =>
        new Th2Parser().Parse("p.th2", src).Diagnostics;
    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Cfg(string src) =>
        new ThconfigParser().Parse("p.thconfig", src).Diagnostics;

    private static bool Has(System.Collections.Immutable.ImmutableArray<Diagnostic> ds, string code) =>
        ds.Any(d => d.Code == code);

    // ---- identifiers + block matching ----

    [Theory]
    [InlineData("survey a!b\nendsurvey")]
    [InlineData("survey alpha\nmap m&p\nendmap\nendsurvey")]
    public void Illegal_identifier_char_is_flagged(string src) =>
        Assert.True(Has(Th(src), DiagnosticCodes.IllegalIdentifier));

    [Theory]
    [InlineData("survey Veneția-Superioară\nendsurvey")]   // Unicode letters are legal
    [InlineData("survey a-b_c/d.e\nendsurvey")]             // ext-keyword punctuation is legal
    [InlineData("survey 123\nendsurvey")]                   // numeric-only is a valid keyword
    public void Legal_identifiers_are_not_flagged(string src) =>
        Assert.False(Has(Th(src), DiagnosticCodes.IllegalIdentifier));

    [Fact]
    public void Endsurvey_id_mismatch_is_flagged() =>
        Assert.True(Has(Th("survey alpha\nendsurvey beta"), DiagnosticCodes.BlockIdMismatch));

    [Fact]
    public void Endsurvey_matching_id_is_ok() =>
        Assert.False(Has(Th("survey alpha\nendsurvey alpha"), DiagnosticCodes.BlockIdMismatch));

    [Fact]
    public void Endscrap_id_mismatch_is_flagged() =>
        Assert.True(Has(Th2("scrap s1\nendscrap s2"), DiagnosticCodes.BlockIdMismatch));

    // ---- numeric coordinates ----

    [Fact]
    public void Fix_non_numeric_coordinate_is_flagged() =>
        Assert.True(Has(Th("survey a\nfix p1 abc 2 3\nendsurvey"), DiagnosticCodes.MalformedFix));

    [Fact]
    public void Fix_deg_min_sec_coordinate_is_accepted() =>
        Assert.False(Has(Th("survey a\ncs lat-long\nfix p1 25:13:43.7 45:31:16 100\nendsurvey"),
            DiagnosticCodes.MalformedFix));

    [Fact]
    public void Fix_qualified_station_keeps_full_name_no_duplicate()
    {
        // A '@'-qualified fix station must not be truncated (regression guard).
        var r = new ThParser().Parse("p.th",
            "survey a\nfix 35@sub.deep 1 2 3\nendsurvey");
        Assert.False(Has(r.Diagnostics, DiagnosticCodes.MalformedFix));
    }

    [Fact]
    public void Point_non_numeric_coordinate_is_flagged() =>
        Assert.True(Has(Th2("scrap s1\npoint x y station\nendscrap"), DiagnosticCodes.Th2MalformedPoint));

    // ---- centreline argument enums ----

    [Fact]
    public void Unknown_shot_flag_is_flagged() =>
        Assert.True(Has(Th("survey a\ncentreline\nflags bogus\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidFlag));

    [Theory]
    [InlineData("flags duplicate")]
    [InlineData("flags not splay")]
    [InlineData("flags surface approximate")]
    public void Valid_shot_flags_are_ok(string flagsLine) =>
        Assert.False(Has(Th($"survey a\ncentreline\n{flagsLine}\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidFlag));

    [Fact]
    public void Unknown_mark_type_is_flagged() =>
        Assert.True(Has(Th("survey a\ncentreline\nmark 1 2 bogus\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidMarkType));

    [Fact]
    public void Unknown_extend_spec_is_flagged() =>
        Assert.True(Has(Th("survey a\ncentreline\nextend sideways\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidExtendSpec));

    [Theory]
    [InlineData("extend left")]
    [InlineData("extend 150")]   // 0-200 percentage
    public void Valid_extend_specs_are_ok(string ext) =>
        Assert.False(Has(Th($"survey a\ncentreline\n{ext}\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidExtendSpec));

    [Fact]
    public void Unknown_station_flag_is_flagged() =>
        Assert.True(Has(Th("survey a\ncentreline\nstation 1 \"c\" bogus\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidStationFlag));

    [Theory]
    [InlineData("station 1 \"pit\" continuation attr code \"V\"")]
    [InlineData("station 2 \"\" entrance")]
    [InlineData("station 3 \"c\" air-draught:winter")]
    public void Valid_station_flags_are_ok(string st) =>
        Assert.False(Has(Th($"survey a\ncentreline\n{st}\nendcentreline\nendsurvey"),
            DiagnosticCodes.InvalidStationFlag));

    // ---- .th2 options ----

    [Fact]
    public void Unknown_th2_option_is_flagged() =>
        Assert.True(Has(Th2("scrap s1\npoint 1 2 station -bogus foo\nendscrap"),
            DiagnosticCodes.Th2UnknownOption));

    [Fact]
    public void Known_th2_options_are_ok() =>
        Assert.False(Has(Th2("scrap s1\npoint 1 2 station -name 1.0 -scale m\nendscrap"),
            DiagnosticCodes.Th2UnknownOption));

    // ---- .thconfig export + layout ----

    [Fact]
    public void Unknown_export_type_is_flagged() =>
        Assert.True(Has(Cfg("export bogustype -o x.pdf"), DiagnosticCodes.UnknownExportType));

    [Fact]
    public void Export_format_invalid_for_type_is_flagged() =>
        Assert.True(Has(Cfg("export model -fmt pdf"), DiagnosticCodes.UnknownExportFormat));

    [Theory]
    [InlineData("export model -fmt 3dmf")]   // digit-led format must coalesce, not be read as "3"
    [InlineData("export map -fmt xvi")]
    [InlineData("export model -fmt survex")]
    public void Valid_export_formats_are_ok(string ex) =>
        Assert.False(Has(Cfg(ex), DiagnosticCodes.UnknownExportFormat));

    [Fact]
    public void Unknown_layout_option_is_flagged() =>
        Assert.True(Has(Cfg("layout l1\nbogus-option 5\nendlayout"), DiagnosticCodes.UnknownLayoutOption));

    [Fact]
    public void Known_layout_options_are_ok() =>
        Assert.False(Has(Cfg("layout l1\nscale 1 100\nsymbol-color point wall blue\nendlayout"),
            DiagnosticCodes.UnknownLayoutOption));

    // ---- centreline single-column "data row" = stray/typo'd command (TH0037) ----

    [Fact]
    public void Single_token_line_in_centreline_is_flagged() =>
        Assert.True(Has(Th("centreline\n zx\nendcentreline"), DiagnosticCodes.MalformedDataRow));

    [Fact]
    public void Single_token_line_in_centreline_with_trailing_comment_is_flagged() =>
        // The exact reported shape: a bare token followed by an inline comment.
        Assert.True(Has(Th("centreline\n zx #@note\nendcentreline"), DiagnosticCodes.MalformedDataRow));

    [Theory]
    [InlineData("centreline\n1 2\nendcentreline")]                       // 2-col short/newline format
    [InlineData("centreline\ndata normal from to length compass clino\n0 1 5.0 90 0\nendcentreline")]
    [InlineData("centreline\ndata diving station depth newline tape backcompass\n0 7.3\n12 60\nendcentreline")]
    public void Multi_column_data_rows_are_not_flagged(string src) =>
        Assert.False(Has(Th(src), DiagnosticCodes.MalformedDataRow));

    [Fact]
    public void Top_level_stray_token_stays_unknown_command_not_a_data_row() =>
        // Outside a centreline a bare token is still TH0010 (unchanged), never TH0037.
        Assert.True(Has(Th("zx"), DiagnosticCodes.UnknownCommand) &&
                    !Has(Th("zx"), DiagnosticCodes.MalformedDataRow));

    // ---- Part B: per-command parameter validation ----

    [Theory]
    [InlineData("infer bogus on")]
    [InlineData("infer plumbs maybe")]
    [InlineData("infer plumbs")]
    public void Invalid_infer_spec_is_flagged(string src) =>
        Assert.True(Has(Th(src), DiagnosticCodes.InvalidInferSpec));

    [Theory]
    [InlineData("infer plumbs on")]
    [InlineData("infer equates off")]
    public void Valid_infer_spec_is_ok(string src) =>
        Assert.False(Has(Th(src), DiagnosticCodes.InvalidInferSpec));

    [Fact]
    public void Declination_without_a_number_is_flagged() =>   // the `Inc` template case
        Assert.True(Has(Th("declination Inc degrees"), DiagnosticCodes.MalformedDeclination));

    [Theory]
    [InlineData("declination 0.00 degrees")]
    [InlineData("declination -")]                              // reset
    [InlineData("declination")]                                // reset
    public void Valid_declination_is_ok(string src) =>
        Assert.False(Has(Th(src), DiagnosticCodes.MalformedDeclination));

    [Theory]
    [InlineData("sd length metres")]                           // no value
    [InlineData("sd 0.05 metres")]                             // no quantity
    public void Malformed_sd_is_flagged(string src) =>
        Assert.True(Has(Th(src), DiagnosticCodes.MalformedSd));

    [Fact]
    public void Valid_sd_is_ok() =>
        Assert.False(Has(Th("sd length 0.05 metres"), DiagnosticCodes.MalformedSd));

    [Theory]
    [InlineData("grid-angle x")]
    [InlineData("vthreshold")]
    public void Non_numeric_measurement_is_flagged(string src) =>
        Assert.True(Has(Th(src), DiagnosticCodes.MalformedMeasurement));

    [Theory]
    [InlineData("grid-angle 3.5 degrees")]
    [InlineData("vthreshold 90 deg")]
    public void Valid_measurement_is_ok(string src) =>
        Assert.False(Has(Th(src), DiagnosticCodes.MalformedMeasurement));
}
