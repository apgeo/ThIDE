// C3 — tests for the .th2 line/area rules (spec §6.4–6.5, Th2LineRules) and the
// corrected type tables (LineTypes +11 real types, AreaTypes ±).

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class Th2LineRulesTests
{
    private static ImmutableArray<Diagnostic> Parse(string body, ParserOptions? options = null) =>
        new Th2Parser().Parse("/rules/a.th2", $"scrap s1 -projection plan\n{body}\nendscrap\n", options)
            .Diagnostics;

    private static bool Has(ImmutableArray<Diagnostic> d, string code) =>
        d.Any(x => x.Code.Value == code);

    // ---- corrected type tables ------------------------------------------------------------

    [Theory]
    [InlineData("pit-chimney")]
    [InlineData("walkway")]
    [InlineData("map-connection")]
    [InlineData("pitch")]
    [InlineData("rope-ladder")]
    [InlineData("low-ceiling")]
    public void Previously_missing_line_types_are_known(string type) =>
        Assert.False(Has(Parse($"line {type}\n 1 2\n 3 4\nendline"), DiagnosticCodes.Th2UnknownLineType));

    [Fact]
    public void Area_dimensions_type_is_known() =>
        Assert.False(Has(Parse("area dimensions\n l1\nendarea"), DiagnosticCodes.Th2UnknownAreaType));

    // ---- subtype matrix (TH2_008) -----------------------------------------------------------

    [Theory]
    [InlineData("line wall:blocks")]
    [InlineData("line wall:presumed")]
    [InlineData("line border:temporary")]
    [InlineData("line survey:cave")]
    [InlineData("line water-flow:conjectural")]
    [InlineData("line u:my-line")]
    public void Valid_line_subtypes_are_ok(string head) =>
        Assert.False(Has(Parse($"{head}\n 1 2\n 3 4\nendline"), DiagnosticCodes.Th2UnknownSubtype));

    [Theory]
    [InlineData("line wall:cave")]        // survey's subtype
    [InlineData("line contour:invisible")] // type takes no subtype
    public void Invalid_line_subtypes_are_flagged(string head) =>
        Assert.True(Has(Parse($"{head}\n 1 2\n 3 4\nendline"), DiagnosticCodes.Th2UnknownSubtype));

    // ---- per-type option validity (TH0066) ----------------------------------------------------

    [Theory]
    [InlineData("line wall -text \"x\"")]          // text: label only
    [InlineData("line wall -gradient center")]      // gradient: contour only
    [InlineData("line contour -border on")]         // border: slope only
    [InlineData("line wall -anchors on")]           // anchors: rope only
    [InlineData("line wall -size 5")]               // size: slope only
    [InlineData("line slope -r-size 5")]            // r-size: dead in 6.4
    [InlineData("line border -height 10")]          // height: pit / wall:pit
    public void Invalid_line_option_for_type_is_flagged(string head) =>
        Assert.True(Has(Parse($"{head}\n 1 2\n 3 4\nendline"), DiagnosticCodes.OptionNotValidInContext));

    [Theory]
    [InlineData("line label -text \"x\"")]
    [InlineData("line contour -gradient center")]
    [InlineData("line slope -border on -size 10")]
    [InlineData("line rope -anchors on -rebelays off")]
    [InlineData("line pit -height 25")]
    [InlineData("line wall:pit -height 25")]
    [InlineData("line arrow -head both")]
    [InlineData("line section -direction begin")]
    [InlineData("line wall -outline out -reverse on -close auto")]
    public void Valid_line_option_usage_is_ok(string head)
    {
        var d = Parse($"{head}\n 1 2\n 3 4\nendline");
        Assert.False(Has(d, DiagnosticCodes.OptionNotValidInContext),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
    }

    [Fact]
    public void Invalid_gradient_value_is_flagged() =>
        Assert.True(Has(Parse("line contour -gradient sideways\n 1 2\n 3 4\nendline"),
            DiagnosticCodes.ValueTypeMismatch));

    // ---- area rules -----------------------------------------------------------------------------

    [Fact]
    public void Scale_on_area_is_flagged() =>
        Assert.True(Has(Parse("area water -scale 20\n l1\nendarea"),
            DiagnosticCodes.OptionNotValidInContext));

    // ---- toggles ---------------------------------------------------------------------------------

    [Fact]
    public void Disabling_line_section_suppresses_rules()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("line")));
        Assert.False(Has(Parse("line wall -text \"x\"\n 1 2\n 3 4\nendline", opts),
            DiagnosticCodes.OptionNotValidInContext));
    }
}
