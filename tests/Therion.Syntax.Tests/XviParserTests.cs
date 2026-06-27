// XVI parser tests — Therion `set XVI*` Tcl export format.
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class XviParserTests
{
    private const string Sample = """
        # Therion xvi export
        set XVIgrids {1.0 m}
        set XVIstations {
          {  -61.02   192.22 0}
          {  -45.28   102.46 1}
          {   85.43    11.32 5'}
        }
        set XVIshots {
          {  -61.02   192.22  -45.28  102.46}
          {  -45.28   102.46  -132.68  29.63}
        }
        set XVIgrid {-198.819 -240.846 19.685 0.0 0.0 19.685 20 25}
        """;

    [Fact]
    public void Parses_well_formed_file_with_no_diagnostics()
    {
        var r = new XviParser().Parse("a.xvi", Sample);
        Assert.NotNull(r.Value);
        var f = r.Value!;
        Assert.Empty(r.Diagnostics);
        Assert.Equal(1.0, f.GridSpacing!.Value, 6);
        Assert.Equal("m", f.GridUnits);
        Assert.Equal(3, f.Stations.Length);
        Assert.Equal("5'", f.Stations[2].Name);
        Assert.Equal(85.43, f.Stations[2].X, 2);
        Assert.Equal(2, f.Shots.Length);
        Assert.Equal(-61.02, f.Shots[0].X1, 2);
        Assert.NotNull(f.Grid);
        Assert.Equal(20, f.Grid!.Value.CountX);
        Assert.Equal(25, f.Grid!.Value.CountY);
    }

    [Fact]
    public void Parses_sketchlines()
    {
        var r = new XviParser().Parse("a.xvi",
            "set XVIsketchlines {\n  {black 1 2 3 4 5 6}\n  {red 7 8 9 10}\n}\n");
        var f = r.Value!;
        Assert.Empty(r.Diagnostics);
        Assert.Equal(2, f.SketchLines.Length);
        Assert.Equal("black", f.SketchLines[0].Colour);
        Assert.Equal(6, f.SketchLines[0].Coordinates.Length);
    }

    [Fact]
    public void Unknown_variable_warns_lenient()
    {
        var r = new XviParser().Parse("a.xvi", "set XVIbogus {1 2 3}\n");
        var d = Assert.Single(r.Diagnostics, x => x.Code == DiagnosticCodes.XviUnknownVariable);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Unknown_variable_errors_strict()
    {
        var opts = new ParserOptions { Mode = ParserMode.Strict };
        var r = new XviParser().Parse("a.xvi", "set XVIbogus {1 2 3}\n", opts);
        var d = Assert.Single(r.Diagnostics, x => x.Code == DiagnosticCodes.XviUnknownVariable);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Unterminated_block_errors()
    {
        var r = new XviParser().Parse("a.xvi", "set XVIstations {\n  {1 2 a}\n");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.XviUnterminatedBlock);
    }

    [Fact]
    public void Malformed_grid_warns()
    {
        var r = new XviParser().Parse("a.xvi", "set XVIgrid {1 2 3}\n");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.XviMalformedGrid);
    }

    [Fact]
    public void Stray_non_set_statement_warns()
    {
        var r = new XviParser().Parse("a.xvi", "puts hello\nset XVIgrids {1.0 m}\n");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.XviUnexpectedStatement);
    }
}
