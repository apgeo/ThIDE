// M4 — XVI parser tests.
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class XviParserTests
{
    private const string Sample = """
        # XTherion export
        XVI 1
        SCALE 200.5
        TRANSFORM 1.0 0.0 0.0 1.0 100.0 50.0
        IMAGE scan.jpg
        CALIBRATION 0 0 10 20 100 0 510 20
        """;

    [Fact]
    public void Parses_well_formed_file()
    {
        var r = new XviParser().Parse("a.xvi", Sample);
        Assert.NotNull(r.Value);
        var f = r.Value!;
        Assert.Equal(1, f.Version);
        Assert.Equal(200.5, f.Scale, 6);
        Assert.Equal(1.0, f.Transform.A);
        Assert.Equal(100.0, f.Transform.Tx);
        Assert.Equal(50.0, f.Transform.Ty);
        Assert.Equal("scan.jpg", f.ImageRelativePath);
        Assert.Equal(2, f.CalibrationPoints.Length);
        Assert.Equal(20.0, f.CalibrationPoints[0].PixelY);
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Missing_image_emits_warning()
    {
        var r = new XviParser().Parse("a.xvi", "XVI 1\nSCALE 200\nTRANSFORM 1 0 0 1 0 0\n");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.XviMissingImage);
    }

    [Fact]
    public void Malformed_transform_emits_warning()
    {
        var r = new XviParser().Parse("a.xvi", "XVI 1\nSCALE 200\nTRANSFORM 1 0 0\nIMAGE x.jpg\n");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.XviMalformedTransform);
    }

    [Fact]
    public void Unknown_keyword_lenient_warns()
    {
        var r = new XviParser().Parse("a.xvi",
            "XVI 1\nSCALE 200\nTRANSFORM 1 0 0 1 0 0\nIMAGE x.jpg\nWHAT now\n");
        var d = Assert.Single(r.Diagnostics, x => x.Code == DiagnosticCodes.XviUnknownKeyword);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Unknown_keyword_strict_errors()
    {
        var opts = new ParserOptions { Mode = ParserMode.Strict };
        var r = new XviParser().Parse("a.xvi",
            "XVI 1\nSCALE 200\nTRANSFORM 1 0 0 1 0 0\nIMAGE x.jpg\nWHAT now\n", opts);
        var d = Assert.Single(r.Diagnostics, x => x.Code == DiagnosticCodes.XviUnknownKeyword);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }
}
