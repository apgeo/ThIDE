// C1 — tests for the typed-node centreline rules (spec §5.2/§5.3, ThCentrelineRules).
// Each rule is exercised through a real ThParser parse (the rules run inside the parser's
// schema pass), both the flagging and the non-flagging (valid) direction, plus toggles.

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class CentrelineRulesTests
{
    private static ImmutableArray<Diagnostic> Parse(string centrelineBody, ParserOptions? options = null)
    {
        var src = $"survey s\n centreline\n{centrelineBody}\n endcentreline\nendsurvey\n";
        return new ThParser().Parse("/rules/a.th", src, options).Diagnostics;
    }

    private static bool Has(ImmutableArray<Diagnostic> diags, string code) =>
        diags.Any(d => d.Code.Value == code);

    // ---- data <style> <readings> matrix (spec §5.3) -----------------------------------

    [Theory]
    [InlineData("data normal from to length compass clino")]
    [InlineData("data normal from to tape bearing gradient")]                  // aliases
    [InlineData("data topofil from to fromcount tocount compass clino")]       // topofil ≡ normal
    [InlineData("data diving from to length bearing depth")]
    [InlineData("data cylpolar from to tape compass depthchange")]
    [InlineData("data cartesian from to easting northing altitude")]
    [InlineData("data cartesian from to dx dy dz")]
    [InlineData("data dimensions station up down left right")]
    [InlineData("data nosurvey from to")]
    [InlineData("data normal station count newline direction length bearing gradient")]
    [InlineData("data normal from to length compass clino ignore ignoreall")]
    public void Valid_data_orders_produce_no_order_diagnostics(string dataLine)
    {
        var diags = Parse(dataLine);
        Assert.False(Has(diags, DiagnosticCodes.InvalidReadingForStyle), Dump(diags));
        Assert.False(Has(diags, DiagnosticCodes.DuplicateReading), Dump(diags));
        Assert.False(Has(diags, DiagnosticCodes.IncompleteDataOrder), Dump(diags));
        Assert.False(Has(diags, DiagnosticCodes.InvalidNewlinePosition), Dump(diags));
        Assert.False(Has(diags, DiagnosticCodes.InterleavedMix), Dump(diags));
    }

    [Theory]
    [InlineData("data diving from to length bearing gradient depth")]  // gradient is normal-only
    [InlineData("data normal from to length compass clino easting")]   // easting is cartesian-only
    [InlineData("data cartesian from to length dx dy dz")]             // length not in cartesian
    public void Reading_invalid_for_style_is_flagged(string dataLine) =>
        Assert.True(Has(Parse(dataLine), DiagnosticCodes.InvalidReadingForStyle));

    [Fact]
    public void Duplicate_reading_is_flagged() =>
        Assert.True(Has(Parse("data normal from to length length compass clino"),
            DiagnosticCodes.DuplicateReading));

    [Fact]
    public void Duplicate_via_alias_is_flagged() =>  // tape ≡ length
        Assert.True(Has(Parse("data normal from to tape length compass clino"),
            DiagnosticCodes.DuplicateReading));

    [Fact]
    public void Station_mixed_with_from_to_is_flagged() =>
        Assert.True(Has(Parse("data normal from to station length compass clino"),
            DiagnosticCodes.InterleavedMix));

    [Fact]
    public void Newline_last_is_flagged() =>
        Assert.True(Has(Parse("data normal from to length compass clino newline"),
            DiagnosticCodes.InvalidNewlinePosition));

    [Fact]
    public void Incomplete_style_is_flagged() =>   // normal without any gradient reading
        Assert.True(Has(Parse("data normal from to length compass"),
            DiagnosticCodes.IncompleteDataOrder));

    // REVIEW F3: non-interleaved readings must come AFTER newline in interleaved orders
    // (thdata.cxx:1551 "non-interleaved data before newline").
    [Fact]
    public void Noninterleaved_reading_before_newline_is_flagged() =>
        Assert.True(Has(Parse("data normal station length newline bearing gradient"),
            DiagnosticCodes.InterleavedMix));

    [Fact]
    public void Interleaved_order_with_data_after_newline_is_ok() =>
        Assert.False(Has(Parse("data normal station newline length bearing gradient"),
            DiagnosticCodes.InterleavedMix));

    // REVIEW F2: THDATA_MAX_ITEMS(22) counts the style token — at most 21 readings.
    [Fact]
    public void Twenty_two_readings_are_flagged()
    {
        var order = "data normal from to length compass clino" + string.Concat(
            System.Linq.Enumerable.Repeat(" ignore", 17));   // 5 + 17 = 22 readings
        Assert.True(Has(Parse(order), DiagnosticCodes.TooManyArguments));
    }

    [Fact]
    public void Twenty_one_readings_are_ok()
    {
        var order = "data normal from to length compass clino" + string.Concat(
            System.Linq.Enumerable.Repeat(" ignore", 16));   // 5 + 16 = 21 readings
        Assert.False(Has(Parse(order), DiagnosticCodes.TooManyArguments));
    }

    // REVIEW F8: `gps` ≡ `position` (thtt_dataleg_comp) — a quantity keyword, never a data
    // reading; both spellings take the same invalid-for-style route (no TH0034).
    [Theory]
    [InlineData("data normal from to gps length compass clino")]
    [InlineData("data normal from to position length compass clino")]
    public void Position_and_its_gps_alias_are_invalid_readings(string dataLine)
    {
        var diags = Parse(dataLine);
        Assert.True(Has(diags, DiagnosticCodes.InvalidReadingForStyle), Dump(diags));
        Assert.False(Has(diags, DiagnosticCodes.UnknownDataReading), Dump(diags));
    }

    // ---- fix arity + std deviations (spec §5.2: 4–7 args, sds > 0) --------------------

    [Theory]
    [InlineData("fix 1 500 600 700")]
    [InlineData("fix 1 500 600 700 0.5")]
    [InlineData("fix 1 500 600 700 0.5 1.0")]
    [InlineData("fix 1 500 600 700 0.5 0.5 1.0")]
    public void Valid_fix_forms_are_ok(string fixLine)
    {
        var diags = Parse(fixLine);
        Assert.False(Has(diags, DiagnosticCodes.TooManyArguments), Dump(diags));
        Assert.False(Has(diags, DiagnosticCodes.ValueOutOfRange), Dump(diags));
    }

    [Fact]
    public void Fix_with_8_args_is_flagged() =>
        Assert.True(Has(Parse("fix 1 500 600 700 1 1 1 1"), DiagnosticCodes.TooManyArguments));

    [Fact]
    public void Fix_nonpositive_sd_is_flagged() =>
        Assert.True(Has(Parse("fix 1 500 600 700 0"), DiagnosticCodes.ValueOutOfRange));

    // ---- team roles / instrument quantities --------------------------------------------

    [Fact]
    public void Unknown_team_role_is_flagged() =>
        Assert.True(Has(Parse("team \"John Doe\" surveyor"), DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Book_only_explorer_role_warns_with_hint()
    {
        var d = Parse("team \"John Doe\" explorer")
            .Single(d => d.Code.Value == DiagnosticCodes.ValueTypeMismatch);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("thbook", d.Hint);
    }

    [Theory]
    [InlineData("team \"John Doe\" tape compass")]
    [InlineData("team \"John Doe\" dog")]          // assistant alias
    public void Valid_team_roles_are_ok(string teamLine) =>
        Assert.False(Has(Parse(teamLine), DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Invalid_instrument_quantity_is_flagged() =>
        Assert.True(Has(Parse("instrument altitude \"barometer\""), DiagnosticCodes.ValueTypeMismatch));

    // ---- declination / vthreshold / extend ---------------------------------------------

    [Fact]
    public void Numeric_declination_without_units_is_flagged() =>
        Assert.True(Has(Parse("declination 2.5"), DiagnosticCodes.MalformedDeclination));

    [Theory]
    [InlineData("declination 2.5 degrees")]
    [InlineData("declination -")]
    public void Valid_declinations_are_ok(string decl) =>
        Assert.False(Has(Parse(decl), DiagnosticCodes.MalformedDeclination));

    [Fact]
    public void Vthreshold_out_of_range_is_flagged() =>
        Assert.True(Has(Parse("vthreshold 95 degrees"), DiagnosticCodes.ValueOutOfRange));

    [Fact]
    public void Extend_with_4_stations_is_flagged() =>
        Assert.True(Has(Parse("extend left 1 2 3 4"), DiagnosticCodes.TooManyArguments));

    // ---- station flag semantics ---------------------------------------------------------

    [Fact]
    public void Direct_fixed_station_flag_is_flagged() =>
        Assert.True(Has(Parse("station 1 \"\" fixed"), DiagnosticCodes.InvalidStationFlag));

    [Fact]
    public void Not_fixed_station_flag_is_ok() =>
        Assert.False(Has(Parse("station 1 \"\" not fixed"), DiagnosticCodes.InvalidStationFlag));

    [Fact]
    public void Explored_without_continuation_is_flagged() =>
        Assert.True(Has(Parse("station 1 \"x\" explored 100"), DiagnosticCodes.InvalidStationFlag));

    [Fact]
    public void Explored_after_continuation_is_ok() =>
        Assert.False(Has(Parse("station 1 \"x\" continuation explored 100"),
            DiagnosticCodes.InvalidStationFlag));

    // ---- sd/units quantity-class compatibility ------------------------------------------

    [Fact]
    public void Mixed_quantity_classes_in_sd_are_flagged() =>
        Assert.True(Has(Parse("sd length compass 0.5 metres"), DiagnosticCodes.ValueTypeMismatch));

    [Theory]
    [InlineData("sd length tape 0.05 metres")]
    [InlineData("sd compass clino 1 degrees")]
    [InlineData("units length depth metres")]
    public void Consistent_quantity_classes_are_ok(string line) =>
        Assert.False(Has(Parse(line), DiagnosticCodes.ValueTypeMismatch));

    // ---- toggles -------------------------------------------------------------------------

    [Fact]
    public void Disabling_centreline_section_suppresses_all_rules()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("centreline")));
        var diags = Parse("data diving from to length bearing gradient depth", opts);
        Assert.False(Has(diags, DiagnosticCodes.InvalidReadingForStyle));
    }

    [Fact]
    public void Master_switch_suppresses_all_rules()
    {
        var diags = Parse("station 1 \"\" fixed",
            new ParserOptions(Validation: SchemaValidationOptions.Off));
        Assert.False(Has(diags, DiagnosticCodes.InvalidStationFlag));
    }

    [Fact]
    public void Strict_mode_promotes_rule_to_error()
    {
        var diags = Parse("data diving from to length bearing gradient depth",
            new ParserOptions(Mode: ParserMode.Strict));
        var d = diags.Single(d => d.Code.Value == DiagnosticCodes.InvalidReadingForStyle);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    private static string Dump(ImmutableArray<Diagnostic> diags) =>
        string.Join("; ", diags.Select(d => $"{d.Code}:{d.Message}"));
}
