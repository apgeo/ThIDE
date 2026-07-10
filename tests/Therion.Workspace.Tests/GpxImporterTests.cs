using System.Linq;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// GPX → Therion fixed-station import.
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
        Assert.Contains("fix Entrance_A 46.5 8 1850", th);   // cs lat-long: latitude first
        Assert.Contains("endsurvey", th);
    }

    [Fact]
    public void Invalid_xml_yields_no_waypoints()
        => Assert.Empty(GpxImporter.Parse("not xml"));

    /// <summary>
    /// The import writes a `fix` under `cs lat-long`, and the GIS export reads one back. If the two
    /// disagree about axis order, a waypoint in the Swiss Alps is exported somewhere off Somalia.
    /// </summary>
    [Fact]
    public void A_waypoint_survives_the_round_trip_to_wgs84()
    {
        var th = GpxImporter.ToTherion(Gpx);

        var fix = th.Split('\n').First(l => l.Contains("fix Entrance_A"));
        var parts = fix.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        double x = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        double y = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(Therion.Syntax.CoordinateTransform.TryToWgs84("lat-long", x, y, out var wgs84));
        Assert.Equal(46.5, wgs84.Lat, 7);
        Assert.Equal(8.0, wgs84.Lon, 7);
    }
}
