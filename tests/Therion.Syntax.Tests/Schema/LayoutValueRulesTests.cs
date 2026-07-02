// C6 — tests for layout option value validation (spec §8, LayoutValueRules) and the
// corrected LayoutKeywords table (survey-level/geospatial/color-profile were missing).

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class LayoutValueRulesTests
{
    private static ImmutableArray<Diagnostic> Parse(string optionLine, ParserOptions? options = null) =>
        new ThconfigParser().Parse("/cfg/a.thconfig",
            $"layout test\n  {optionLine}\nendlayout\n", options).Diagnostics;

    private static bool Has(ImmutableArray<Diagnostic> d, string code) =>
        d.Any(x => x.Code.Value == code);

    // ---- corrected key table -----------------------------------------------------------

    [Theory]
    [InlineData("survey-level 2")]     // was the corpus TH0062 false positive
    [InlineData("geospatial off")]
    [InlineData("color-profile rgb x.icc")]
    public void Previously_missing_keys_are_known(string line) =>
        Assert.False(Has(Parse(line), DiagnosticCodes.UnknownLayoutOption));

    [Fact]
    public void Phantom_gradient_key_is_flagged() =>
        Assert.True(Has(Parse("gradient on"), DiagnosticCodes.UnknownLayoutOption));

    // ---- value enums ----------------------------------------------------------------------

    [Theory]
    [InlineData("grid top")]
    [InlineData("grid-coords border")]
    [InlineData("legend all")]
    [InlineData("colour-legend smooth")]
    [InlineData("debug station-names")]
    [InlineData("north grid")]
    [InlineData("color-model cmyk")]
    [InlineData("units imperial")]
    [InlineData("statistics explo-length off")]
    [InlineData("color map-fg [90 80 70]")]
    [InlineData("symbol-hide group centerline")]
    [InlineData("map-header 0 100 ne")]
    [InlineData("map-header off")]
    [InlineData("opacity 70")]
    [InlineData("scale 1 500")]        // numeric keys not enum-checked
    public void Valid_layout_values_are_ok(string line)
    {
        var d = Parse(line);
        Assert.False(Has(d, DiagnosticCodes.ValueTypeMismatch), Dump(d));
        Assert.False(Has(d, DiagnosticCodes.ValueOutOfRange), Dump(d));
    }

    [Theory]
    [InlineData("grid sideways")]
    [InlineData("legend maybe")]
    [InlineData("north magnetic")]
    [InlineData("color-model hsv")]
    [InlineData("units nautical")]
    [InlineData("statistics bogus on")]
    [InlineData("color background red")]
    [InlineData("symbol-hide blob water")]
    [InlineData("map-header 0 100 northeast")]
    public void Invalid_layout_values_are_flagged(string line) =>
        Assert.True(Has(Parse(line), DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Opacity_out_of_range_is_flagged() =>
        Assert.True(Has(Parse("opacity 150"), DiagnosticCodes.ValueOutOfRange));

    // ---- toggles ------------------------------------------------------------------------------

    [Fact]
    public void Disabling_layout_section_suppresses_value_checks()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("layout")));
        Assert.False(Has(Parse("grid sideways", opts), DiagnosticCodes.ValueTypeMismatch));
    }

    private static string Dump(ImmutableArray<Diagnostic> d) =>
        string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}"));
}
