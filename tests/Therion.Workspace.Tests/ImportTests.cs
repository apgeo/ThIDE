// (Survex/Compass) + (DEM→surface) — converters produce valid Therion that parses
// cleanly and carries the expected structure.
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

public class ImportTests
{
    private static (TherionFile File, int Errors) Parse(string th)
    {
        var r = new ThParser().Parse("imported.th", th);
        return (r.Value!, r.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Survex_imports_to_valid_therion_with_survey_and_data()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *title "Test Cave"
            *fix entrance 0 0 0
            *data normal from to tape compass clino
            1 2 10.0 100 -5
            2 3 8.5 110 0
            *equate 3 upper.1
            *begin upper
            1 2 5 0 0
            *end upper
            *end cave
            """);
        var (file, errors) = Parse(th);
        Assert.Equal(0, errors);
        Assert.Contains("survey cave", th);
        Assert.Contains("centreline", th);
        Assert.Contains("data normal from to tape compass clino", th);
        Assert.Contains("fix entrance 0 0 0", th);
        Assert.Contains("survey upper", th);
        // Two nested surveys parsed.
        Assert.True(file.Children.OfType<SurveyCommand>().Any());
    }

    [Fact]
    public void Compass_imports_block_to_survey_with_lrud_data()
    {
        var dat = "Test Cave\r\n" +
                  "SURVEY NAME: A\r\n" +
                  "SURVEY DATE: 7 1 2024  COMMENT:hi\r\n" +
                  "SURVEY TEAM:\r\n" +
                  "Alice;Bob\r\n" +
                  "DECLINATION: 2.50  FORMAT: DDDDLUDRADLN  CORRECTIONS: 0 0 0\r\n" +
                  "\r\n" +
                  "FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT FLAGS COMMENTS\r\n" +
                  "\r\n" +
                  "1 2 10.00 100.0 -5.0 1.0 2.0 -999.0 1.5\r\n" +
                  "2 3 8.50 110.0 0.0 1.0 2.0 0.5 1.5\r\n";
        var th = CompassImporter.Import(dat);
        var (_, errors) = Parse(th);
        Assert.Equal(0, errors);
        Assert.Contains("survey A", th);
        Assert.Contains("date 2024.07.01", th);
        Assert.Contains("declination 2.5 degrees", th);
        Assert.Contains("data normal from to length compass clino left up down right", th);
        Assert.Contains("1 2 10 100 -5 1 2 - 1.5", th);   // -999 down → '-'
    }

    [Fact]
    public void Dem_esri_ascii_becomes_surface_block_with_rows_reversed()
    {
        // 3 cols × 2 rows; ASC top row (north) is "10 11 12", bottom (south) is "20 21 22".
        var asc = "ncols 3\nnrows 2\nxllcorner 100\nyllcorner 200\ncellsize 5\nNODATA_value -9999\n" +
                  "10 11 12\n20 21 22\n";
        var surface = SurfaceFromDem.FromEsriAscii(asc);
        Assert.Contains("grid 100 200 5 5 3 2", surface);
        // South row must come first in Therion output.
        int south = surface.IndexOf("20 21 22");
        int north = surface.IndexOf("10 11 12");
        Assert.True(south >= 0 && north >= 0 && south < north);

        var (_, errors) = Parse("survey s\n" + surface + "endsurvey\n");
        Assert.Equal(0, errors);
    }

    [Fact]
    public void Dem_scaffold_parses_cleanly()
    {
        var (_, errors) = Parse("survey s\n" + SurfaceFromDem.Scaffold(0, 0, 10, 10, 4, 4) + "endsurvey\n");
        Assert.Equal(0, errors);
    }

    [Fact]
    public void Th2_scaffold_emits_valid_scrap_wired_to_xvi()
    {
        var th2 = Th2Scaffold.NewScrap("entrance hall", "plan", "../xvi/eh.xvi");
        var r = new Th2Parser().Parse("s.th2", th2);
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("scrap entrance_hall -projection plan", th2);   // id sanitized
        Assert.Contains("-sketch \"../xvi/eh.xvi\"", th2);
        Assert.Equal("input \"a b.th2\"", Th2Scaffold.InputLine("a b.th2"));
    }
}
