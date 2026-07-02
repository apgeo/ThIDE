// C2 — tests for the .th2 point rules (spec §6.3, Th2PointRules): subtype matrix
// (TH2_008), per-type option validity (TH0066), orientation range, toggles.

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class Th2PointRulesTests
{
    private static ImmutableArray<Diagnostic> Parse(string pointLine, ParserOptions? options = null) =>
        new Th2Parser().Parse("/rules/a.th2", $"scrap s1 -projection plan\n{pointLine}\nendscrap\n", options)
            .Diagnostics;

    private static bool Has(ImmutableArray<Diagnostic> d, string code) =>
        d.Any(x => x.Code.Value == code);

    // ---- subtype matrix (TH2_008) -------------------------------------------------------

    [Theory]
    [InlineData("point 1 2 station:fixed")]
    [InlineData("point 1 2 station:natural")]
    [InlineData("point 1 2 air-draught:winter")]
    [InlineData("point 1 2 water-flow:paleo")]
    [InlineData("point 1 2 station -subtype painted")]
    [InlineData("point 1 2 u:my-custom-thing")]     // user types take anything
    public void Valid_subtypes_are_ok(string line) =>
        Assert.False(Has(Parse(line), DiagnosticCodes.Th2UnknownSubtype));

    [Theory]
    [InlineData("point 1 2 station:permanent")]     // wrong table
    [InlineData("point 1 2 air-draught:fixed")]
    [InlineData("point 1 2 stalactite:big")]        // type takes no subtype at all
    public void Invalid_subtypes_are_flagged(string line) =>
        Assert.True(Has(Parse(line), DiagnosticCodes.Th2UnknownSubtype));

    // ---- per-type option validity (TH0066) ------------------------------------------------

    [Theory]
    [InlineData("point 1 2 station -orientation 90")]  // src: -orientation not valid with station
    [InlineData("point 1 2 station -align t")]
    [InlineData("point 1 2 stalagmite -text \"x\"")]
    [InlineData("point 1 2 label -explored 100")]
    [InlineData("point 1 2 label -scrap s2")]
    [InlineData("point 1 2 bedrock -name 1")]
    [InlineData("point 1 2 label -value 5")]           // -value: altitude/height/… only
    [InlineData("point 1 2 label -dist 5")]            // -dist: extra only
    [InlineData("point 1 2 label -clip on")]           // label is in the no-clip list
    [InlineData("point 1 2 debris -x-size 5")]         // dead option in 6.4
    public void Invalid_option_for_type_is_flagged(string line) =>
        Assert.True(Has(Parse(line), DiagnosticCodes.OptionNotValidInContext));

    [Theory]
    [InlineData("point 1 2 station -name 1")]
    [InlineData("point 1 2 continuation -explored 100 -text \"lead\"")]
    [InlineData("point 1 2 section -scrap xs1")]
    [InlineData("point 1 2 height -value 12")]
    [InlineData("point 1 2 extra -dist 4 -from 3")]
    [InlineData("point 1 2 stalagmite -orientation 45 -clip on")]  // clip fine on stalagmite [src]
    [InlineData("point 1 2 u:weird -text \"anything goes on u:\"")]
    public void Valid_option_usage_is_ok(string line) =>
        Assert.False(Has(Parse(line), DiagnosticCodes.OptionNotValidInContext));

    [Fact]
    public void Orientation_out_of_range_is_flagged() =>
        Assert.True(Has(Parse("point 1 2 stalagmite -orientation 360"), DiagnosticCodes.ValueOutOfRange));

    [Fact]
    public void Newly_added_pillars_type_is_known() =>
        Assert.False(Has(Parse("point 1 2 pillars"), DiagnosticCodes.Th2UnknownPointType));

    // ---- toggles ---------------------------------------------------------------------------

    [Fact]
    public void Disabling_point_section_suppresses_rules()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("point")));
        Assert.False(Has(Parse("point 1 2 station -align t", opts),
            DiagnosticCodes.OptionNotValidInContext));
    }

    [Fact]
    public void Disabling_options_category_keeps_subtype_check()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            Categories: ValidationCategories.All & ~ValidationCategories.Options));
        var diags = Parse("point 1 2 station:bogus -align t", opts);
        Assert.True(Has(diags, DiagnosticCodes.Th2UnknownSubtype));       // Enums still on
        Assert.False(Has(diags, DiagnosticCodes.OptionNotValidInContext)); // Options off
    }
}
