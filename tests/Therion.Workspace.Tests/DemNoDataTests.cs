using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented grid. An ESRI ASCII grid marks cells without data with its NODATA_value; a Therion
// surface grid has no such marker, so the sentinel must not reach the grid as an elevation.
public class DemNoDataTests
{
    [Fact]
    public void Nodata_cells_are_not_emitted_as_elevations()
    {
        var asc = "ncols 3\nnrows 2\nxllcorner 100\nyllcorner 200\ncellsize 5\nNODATA_value -9999\n" +
                  "10 11 -9999\n20 21 22\n";

        var surface = SurfaceFromDem.FromEsriAscii(asc);

        Assert.DoesNotContain("-9999", surface);
    }

    [Fact]
    public void Nodata_cells_take_the_lowest_elevation_and_the_count_is_noted()
    {
        var asc = "ncols 3\nnrows 2\nxllcorner 100\nyllcorner 200\ncellsize 5\nNODATA_value -9999\n" +
                  "10 11 -9999.0\n20 21 22\n";

        var surface = SurfaceFromDem.FromEsriAscii(asc);

        Assert.Contains("1 cell(s) without data", surface);
        Assert.Contains("20 21 22\n    10 11 10\n", surface);
    }

    [Fact]
    public void A_grid_of_only_nodata_cells_is_rejected()
    {
        var asc = "ncols 2\nnrows 1\nxllcorner 0\nyllcorner 0\ncellsize 5\nNODATA_value -1\n-1 -1\n";

        Assert.Throws<FormatException>(() => SurfaceFromDem.FromEsriAscii(asc));
    }
}
