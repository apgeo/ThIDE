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
