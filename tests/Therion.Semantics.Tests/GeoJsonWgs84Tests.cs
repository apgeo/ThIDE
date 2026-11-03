// GeoJSON (RFC 7946) coordinates are WGS84 lon/lat by definition; a point we cannot place there
// must not be written with its raw local or projected coordinates as if they were degrees.

using Therion.Semantics;

namespace Therion.Semantics.Tests;

public class GeoJsonWgs84Tests
{
    [Fact]
    public void Local_fix_without_cs_is_not_placed_at_null_island()
    {
        var json = GisExport.Export(new[] { new GisPoint("L", 0, 0, 0, "", "fixed") }, GisFormat.GeoJson);

        Assert.DoesNotContain("\"coordinates\":[0,0", json);
    }

    [Fact]
    public void Unconvertible_projected_point_is_not_written_as_lon_lat()
    {
        var json = GisExport.Export(new[] { new GisPoint("J", -600000, -1200000, 300, "S-JTSK", "fixed") }, GisFormat.GeoJson);

        Assert.DoesNotContain("\"coordinates\":[-600000", json);
        Assert.Contains("\"geometry\":null", json);
    }

    [Fact]
    public void Converted_point_does_not_label_its_lon_lat_with_the_source_crs()
    {
        var json = GisExport.Export(new[] { new GisPoint("E1", 411000, 5052000, 1200, "UTM33", "entrance") }, GisFormat.GeoJson);

        Assert.DoesNotContain("\"cs\":\"UTM33\"", json);
    }
}
