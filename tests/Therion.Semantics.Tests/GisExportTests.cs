// GIS-01 — CRS-aware export + UTM/WGS84 inverse transform.
using System.Collections.Generic;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class GisExportTests
{
    private static readonly IReadOnlyList<GisPoint> Utm = new[]
    {
        new GisPoint("E1", 411000, 5052000, 1200, "UTM33", "entrance"),
    };
    private static readonly IReadOnlyList<GisPoint> Ll = new[]
    {
        new GisPoint("E2", 45.5, 13.7, 900, "lat-long", "fixed"),  // Therion order: lat then long
    };

    [Fact]
    public void Utm_inverse_lands_in_the_right_place()
    {
        Assert.True(CoordinateTransform.TryToWgs84("UTM33", 411000, 5052000, out var ll));
        Assert.InRange(ll.Lat, 45.0, 46.5);     // zone 33, northing ~5052 km → ~45.6°N
        Assert.InRange(ll.Lon, 12.0, 16.0);     // central meridian of zone 33 is 15°E
    }

    [Fact]
    public void LatLong_uses_therion_axis_order()
    {
        Assert.True(CoordinateTransform.TryToWgs84("lat-long", 45.5, 13.7, out var ll));
        Assert.Equal(13.7, ll.Lon, 6);
        Assert.Equal(45.5, ll.Lat, 6);
    }

    [Fact]
    public void Unknown_cs_is_not_geographic()
    {
        Assert.False(CoordinateTransform.TryToWgs84("S-JTSK", 1, 2, out _));
        Assert.False(CoordinateTransform.IsGeographic("S-JTSK"));
    }

    [Fact]
    public void Csv_lists_all_points_with_raw_coords_and_cs()
    {
        var csv = GisExport.Export(Utm, GisFormat.Csv);
        Assert.Contains("name,x,y,z,cs,kind", csv);
        Assert.Contains("E1,411000,5052000,1200,UTM33,entrance", csv);
    }

    [Fact]
    public void GeoJson_reprojects_utm_to_lonlat()
    {
        var json = GisExport.Export(Utm, GisFormat.GeoJson);
        Assert.Contains("\"FeatureCollection\"", json);
        Assert.Contains("\"name\":\"E1\"", json);
        Assert.Contains("\"cs\":\"UTM33\"", json);
        Assert.DoesNotContain("411000", json);   // coordinates were reprojected, not raw
    }

    [Fact]
    public void Kml_and_Gpx_only_include_geographic_points()
    {
        var kml = GisExport.Export(Ll, GisFormat.Kml);
        Assert.Contains("<Placemark>", kml);
        Assert.Contains("13.7,45.5", kml);

        var nonGeo = new[] { new GisPoint("J", 1, 2, 3, "S-JTSK", "fixed") };
        var gpx = GisExport.Export(nonGeo, GisFormat.Gpx);
        Assert.DoesNotContain("<wpt", gpx);       // can't reproject → skipped
    }
}
