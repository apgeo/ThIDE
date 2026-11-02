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
}
