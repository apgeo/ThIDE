// C7 — cross-cutting fixes: measurement-unit aliases (thtflength.h/thtfangle.h) and the
// XVIimages variable (thexpmap.cxx exporter output the previous model dropped).

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Schema;

public class CrossCuttingRulesTests
{
    private static ImmutableArray<Diagnostic> Th(string body) =>
        new ThParser().Parse("/x/a.th",
            $"survey s\n centreline\n  {body}\n endcentreline\nendsurvey\n").Diagnostics;

    private static bool Has(ImmutableArray<Diagnostic> d, string code) =>
        d.Any(x => x.Code.Value == code);

    // ---- unit aliases (the corpus's 178 TH0040 warnings were OUR missing aliases) ----------

    [Theory]
    [InlineData("units length centimeters")]   // the actual corpus false positive
    [InlineData("units length millimetres")]
    [InlineData("units length feets")]
    [InlineData("units length metric")]
    [InlineData("units compass min")]
    [InlineData("sd length 0.1 centimetres")]
    public void Source_verified_unit_aliases_are_accepted(string line)
    {
        var d = Th(line);
        Assert.False(Has(d, DiagnosticCodes.MalformedUnits),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
        Assert.False(Has(d, DiagnosticCodes.MalformedSd));
    }

    [Fact]
    public void Unknown_unit_is_still_flagged() =>
        Assert.True(Has(Th("units length cubits"), DiagnosticCodes.MalformedUnits));

    // ---- date grammar (thdate.cxx; C7) --------------------------------------------------------

    [Theory]
    [InlineData("date 2020")]
    [InlineData("date 2020.07")]
    [InlineData("date 2020.07.02")]
    [InlineData("date 2020.07.02@14:30")]
    [InlineData("date 2020.07.02@14:30:15.5")]
    [InlineData("date 1997.08.10 - 1997.08.21")]
    [InlineData("date -")]
    [InlineData("date \"2018.07.29\"")]   // corpus: quoted dates are unquoted by Therion's reader
    [InlineData("explo-date 2019.12.31")]
    public void Valid_dates_are_ok(string line)
    {
        var d = Th(line);
        Assert.False(Has(d, DiagnosticCodes.ValueTypeMismatch),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
    }

    [Theory]
    [InlineData("date 2020.13.01")]      // month 13
    [InlineData("date 2020.01.32")]      // day 32
    [InlineData("date 2020.01.01@25:00")] // hour 25
    [InlineData("date 2020.01.01.05")]   // too many components
    [InlineData("date twentytwenty")]
    public void Invalid_dates_are_flagged(string line) =>
        Assert.True(Has(Th(line), DiagnosticCodes.ValueTypeMismatch));

    // ---- typed option tails (C5.2: select/export/scrap/join) ----------------------------------

    private static ImmutableArray<Diagnostic> Cfg(string text) =>
        new ThconfigParser().Parse("/x/a.thconfig", text + "\n").Diagnostics;

    private static ImmutableArray<Diagnostic> Th2(string body) =>
        new Th2Parser().Parse("/x/a.th2", body + "\n").Diagnostics;

    [Theory]
    [InlineData("select cave1 -recursive off -map-level 2")]
    [InlineData("export map -fmt pdf -layout-debug on -layout-map-header 0 0 ne")]
    [InlineData("export model -enable walls -wall-source splays")]
    public void Valid_typed_option_tails_are_ok(string line)
    {
        var d = Cfg(line);
        Assert.False(Has(d, DiagnosticCodes.OptionNotValidInContext),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
        Assert.False(Has(d, DiagnosticCodes.ValueTypeMismatch));
    }

    [Theory]
    [InlineData("select cave1 -bogus on")]
    [InlineData("export map -layout-bogus on")]        // -layout-<key>: key must be a layout key
    [InlineData("export model -wall-source everything")]
    [InlineData("export model -enable everything")]
    public void Invalid_typed_option_tails_are_flagged(string line)
    {
        var d = Cfg(line);
        Assert.True(Has(d, DiagnosticCodes.OptionNotValidInContext) ||
                    Has(d, DiagnosticCodes.ValueTypeMismatch),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
    }

    [Theory]
    [InlineData("scrap s1 -projection plan -flip horizontal -walls auto\npoint 1 2 station -name 1\nendscrap")]
    public void Valid_scrap_options_are_ok(string body)
    {
        var d = Th2(body);
        Assert.False(Has(d, DiagnosticCodes.OptionNotValidInContext),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
    }

    [Theory]
    [InlineData("scrap s1 -flip diagonal\nendscrap")]
    [InlineData("scrap s1 -bogus x\nendscrap")]
    public void Invalid_scrap_options_are_flagged(string body)
    {
        var d = Th2(body);
        Assert.True(Has(d, DiagnosticCodes.OptionNotValidInContext) ||
                    Has(d, DiagnosticCodes.ValueTypeMismatch),
            string.Join("; ", d.Select(x => $"{x.Code}:{x.Message}")));
    }

    [Fact]
    public void Join_with_one_target_is_flagged() =>
        Assert.True(Has(Th2("scrap s1\nendscrap\njoin onlyone"),
            DiagnosticCodes.MissingRequiredArgument));

    // ---- XVIimages ---------------------------------------------------------------------------

    [Fact]
    public void XviImages_variable_is_accepted_and_kept()
    {
        const string xvi = """
            set XVIgrids {1.0 m}
            set XVIimages { {0 0 image.png} }
            set XVIstations { {10 20 a1} }
            set XVIgrid {0 0 39.37 0 0 39.37 10 10}
            """;
        var r = new XviParser().Parse("/x/a.xvi", xvi);
        Assert.False(r.Diagnostics.Any(d => d.Code.Value == DiagnosticCodes.XviUnknownVariable),
            string.Join("; ", r.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.Single(r.Value!.Images);
    }
}
