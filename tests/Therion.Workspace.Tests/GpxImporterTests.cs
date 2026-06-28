using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// MEDIA-04 — GPX → Therion fixed-station import.
public class GpxImporterTests
{
    private const string Gpx =
        "<?xml version=\"1.0\"?>\n" +
        "<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\">\n" +
        "  <wpt lat=\"46.5\" lon=\"8.0\"><name>Entrance A</name><ele>1850.0</ele></wpt>\n" +
        "  <wpt lat=\"46.51\" lon=\"8.01\"><name>P2</name></wpt>\n" +
        "</gpx>\n";

    [Fact]
    public void Parses_waypoints_with_name_and_elevation()
    {
        var pts = GpxImporter.Parse(Gpx);
        Assert.Equal(2, pts.Count);
        Assert.Equal("Entrance_A", pts[0].Name);   // sanitised
        Assert.Equal(46.5, pts[0].Lat, 6);
        Assert.Equal(8.0, pts[0].Lon, 6);
        Assert.Equal(1850.0, pts[0].Elevation);
        Assert.Null(pts[1].Elevation);
    }

    [Fact]
    public void Emits_lat_long_cs_and_lon_lat_fix_lines()
    {
        var th = GpxImporter.ToTherion(Gpx, "trip1");
        Assert.Contains("survey trip1", th);
        Assert.Contains("cs lat-long", th);
        // Therion lon-lat order: 8 before 46.5.
        Assert.Contains("fix Entrance_A 8 46.5 1850", th);
        Assert.Contains("endsurvey", th);
    }

    [Fact]
    public void Invalid_xml_yields_no_waypoints()
        => Assert.Empty(GpxImporter.Parse("not xml"));
}
