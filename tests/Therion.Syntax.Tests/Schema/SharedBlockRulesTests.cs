// C4 — tests for shared .th block validation (spec §6.7): import (schema-driven via the
// generic UnknownCommand option path), map -projection (typed check), require/revise arity.

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class SharedBlockRulesTests
{
    private static ImmutableArray<Diagnostic> Parse(string body, ParserOptions? options = null) =>
        new ThParser().Parse("/rules/a.th", body + "\n", options).Diagnostics;

    private static bool Has(ImmutableArray<Diagnostic> d, string code) =>
        d.Any(x => x.Code.Value == code);

    // ---- import (generic schema/UnknownCommand path) --------------------------------------

    [Theory]
    [InlineData("import cave.3d")]
    [InlineData("import cave.3d -surveys use")]
    [InlineData("import cave.plt -fmt plt -surveys create")]
    [InlineData("import cave.xyz -format xyz")]
    public void Valid_import_forms_are_ok(string line)
    {
        var d = Parse($"survey s\n{line}\nendsurvey");
        Assert.False(Has(d, DiagnosticCodes.MissingRequiredArgument), Dump(d));
        Assert.False(Has(d, DiagnosticCodes.OptionNotValidInContext), Dump(d));
        Assert.False(Has(d, DiagnosticCodes.ValueTypeMismatch), Dump(d));
    }

    [Fact]
    public void Import_without_file_is_flagged() =>
        Assert.True(Has(Parse("survey s\nimport\nendsurvey"), DiagnosticCodes.MissingRequiredArgument));

    [Fact]
    public void Import_invalid_surveys_mode_is_flagged() =>
        Assert.True(Has(Parse("survey s\nimport cave.3d -surveys bogus\nendsurvey"),
            DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Import_invalid_format_is_flagged() =>
        Assert.True(Has(Parse("survey s\nimport cave.3d -fmt dwg\nendsurvey"),
            DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Import_unknown_option_is_flagged() =>
        Assert.True(Has(Parse("survey s\nimport cave.3d -bogus x\nendsurvey"),
            DiagnosticCodes.OptionNotValidInContext));

    // ---- map -projection (typed) --------------------------------------------------------------

    [Theory]
    [InlineData("plan")]
    [InlineData("extended")]
    [InlineData("elevation")]
    public void Valid_map_projection_is_ok(string proj) =>
        Assert.False(Has(Parse($"map m1 -projection {proj}\n s1\nendmap"),
            DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Invalid_map_projection_is_flagged() =>
        Assert.True(Has(Parse("map m1 -projection sideview\n s1\nendmap"),
            DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Disabling_map_section_suppresses_projection_check()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("map")));
        Assert.False(Has(Parse("map m1 -projection sideview\n s1\nendmap", opts),
            DiagnosticCodes.ValueTypeMismatch));
    }

    private static string Dump(ImmutableArray<Diagnostic> d) =>
        string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}"));
}
