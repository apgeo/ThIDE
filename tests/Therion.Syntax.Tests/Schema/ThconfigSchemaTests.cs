// C5 — tests for the .thconfig command schemas (spec §7): value enums, arity, and the
// corrected export-format tables.

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class ThconfigSchemaTests
{
    private static ImmutableArray<Diagnostic> Parse(string text, ParserOptions? options = null) =>
        new ThconfigParser().Parse("/cfg/a.thconfig", text + "\n", options).Diagnostics;

    private static bool Has(ImmutableArray<Diagnostic> d, string code) =>
        d.Any(x => x.Code.Value == code);

    // ---- value enums / arity ---------------------------------------------------------------

    [Theory]
    [InlineData("log extend")]
    [InlineData("sketch-warp plaquette")]
    [InlineData("sketch-warp linear")]     // src-only algorithm (book omits it)
    [InlineData("scrap-sort off")]
    [InlineData("maps-offset on")]
    [InlineData("sketch-colors 128")]
    [InlineData("setup3d 0.5")]
    [InlineData("language sk_SK")]
    [InlineData("system \"start viewer.exe\"")]
    public void Valid_thconfig_commands_are_ok(string line)
    {
        var d = Parse(line);
        Assert.False(Has(d, DiagnosticCodes.ValueTypeMismatch), Dump(d));
        Assert.False(Has(d, DiagnosticCodes.MissingRequiredArgument), Dump(d));
    }

    [Theory]
    [InlineData("log everything")]
    [InlineData("sketch-warp cubic")]
    [InlineData("scrap-sort maybe")]
    public void Invalid_enum_values_are_flagged(string line) =>
        Assert.True(Has(Parse(line), DiagnosticCodes.ValueTypeMismatch));

    [Fact]
    public void Log_without_argument_is_flagged() =>
        Assert.True(Has(Parse("log"), DiagnosticCodes.MissingRequiredArgument));

    [Fact]
    public void Trailing_comment_is_not_counted_as_arguments() =>   // corpus regression
        Assert.False(Has(Parse("language en # slo not yet supported"),
            DiagnosticCodes.TooManyArguments));

    // ---- corrected export formats (spec §7 / thexp*.h tables) ---------------------------------

    [Theory]
    [InlineData("export map -fmt th2")]
    [InlineData("export map -fmt shapefile")]
    [InlineData("export model -fmt shapefiles")]
    [InlineData("export cave-list -fmt text")]
    public void Real_formats_previously_missing_are_ok(string line) =>
        Assert.False(Has(Parse(line), DiagnosticCodes.UnknownExportFormat));

    [Theory]
    [InlineData("export model -fmt lox")]   // phantom formats the compiler rejects
    [InlineData("export model -fmt plt")]
    [InlineData("export model -fmt wrl")]
    public void Phantom_formats_are_flagged(string line) =>
        Assert.True(Has(Parse(line), DiagnosticCodes.UnknownExportFormat));

    // ---- toggles -------------------------------------------------------------------------------

    [Fact]
    public void Disabling_thconfig_section_suppresses_checks()
    {
        var opts = new ParserOptions(Validation: new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("thconfig")));
        Assert.False(Has(Parse("log everything", opts), DiagnosticCodes.ValueTypeMismatch));
    }

    private static string Dump(ImmutableArray<Diagnostic> d) =>
        string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}"));
}
