// GIS-01 — minimal coordinate transforms needed to export survey points to lon/lat formats
// (KML / GPX). We don't ship a full proj library, so we cover the cases that matter for caving:
//   - lat-long / long-lat (WGS84): pass-through with the right axis order.
//   - UTM<zone>[N|S] and EPSG:326xx/327xx (WGS84 UTM): closed-form inverse projection.
// Anything else returns false (the caller exports raw coordinates / CSV / GeoJSON instead).
//
// UTM inverse uses the standard USGS/Snyder series (WGS84 ellipsoid). Accurate to ~1 m, which is
// well within "preview-quality" for placing entrances on a web map.

using System;
using System.Globalization;

namespace Therion.Syntax;

/// <summary>Lon/lat (WGS84, degrees) result of a coordinate transform.</summary>
public readonly record struct LonLat(double Lon, double Lat);

/// <summary>Best-effort coordinate transforms to WGS84 lon/lat for GIS export (GIS-01).</summary>
public static class CoordinateTransform
{
    private const double A = 6378137.0;                 // WGS84 semi-major axis
    private const double F = 1.0 / 298.257223563;       // WGS84 flattening
    private const double K0 = 0.9996;                   // UTM scale factor

    /// <summary>
    /// Transforms <paramref name="x"/>/<paramref name="y"/> in coordinate system <paramref name="cs"/>
    /// to WGS84 lon/lat. Returns false when <paramref name="cs"/> is not one we can convert.
    /// </summary>
    public static bool TryToWgs84(string? cs, double x, double y, out LonLat result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(cs)) return false;
        var s = cs.Trim();

        if (s.Equals("lat-long", StringComparison.OrdinalIgnoreCase))
        { result = new LonLat(y, x); return true; }       // Therion order: latitude then longitude
        if (s.Equals("long-lat", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("wgs84", StringComparison.OrdinalIgnoreCase))
        { result = new LonLat(x, y); return true; }

        if (TryUtmZone(s, out int zone, out bool north))
        {
            result = InverseUtm(x, y, zone, north);
            return true;
        }
        return false;
    }

    /// <summary>True if the export coordinates for <paramref name="cs"/> can be placed on a web map.</summary>
    public static bool IsGeographic(string? cs) =>
        TryToWgs84(cs ?? string.Empty, 0, 0, out _) || TryUtmZone((cs ?? string.Empty).Trim(), out _, out _);

    // Parse "UTM33", "UTM33N", "UTM33S", "EPSG:32633", "EPSG:32733".
    private static bool TryUtmZone(string cs, out int zone, out bool north)
    {
        zone = 0; north = true;
        if (cs.StartsWith("UTM", StringComparison.OrdinalIgnoreCase))
        {
            var rest = cs.Substring(3);
            char last = rest.Length > 0 ? rest[^1] : '\0';
            if (last is 'N' or 'n' or 'S' or 's') { north = last is 'N' or 'n'; rest = rest[..^1]; }
            return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out zone)
                   && zone is >= 1 and <= 60;
        }
        if (cs.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cs.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
        {
            if (code is >= 32601 and <= 32660) { zone = code - 32600; north = true; return true; }
            if (code is >= 32701 and <= 32760) { zone = code - 32700; north = false; return true; }
        }
        return false;
    }

    private static LonLat InverseUtm(double easting, double northing, int zone, bool north)
    {
        double e2 = F * (2 - F);
        double e1 = (1 - Math.Sqrt(1 - e2)) / (1 + Math.Sqrt(1 - e2));
        double x = easting - 500000.0;
        double y = north ? northing : northing - 10000000.0;

        double m = y / K0;
        double mu = m / (A * (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256));
        double phi1 = mu
            + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
            + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu)
            + (1097 * Math.Pow(e1, 4) / 512) * Math.Sin(8 * mu);

        double ep2 = e2 / (1 - e2);
        double cosPhi = Math.Cos(phi1), sinPhi = Math.Sin(phi1), tanPhi = Math.Tan(phi1);
        double c1 = ep2 * cosPhi * cosPhi;
        double t1 = tanPhi * tanPhi;
        double n1 = A / Math.Sqrt(1 - e2 * sinPhi * sinPhi);
        double r1 = A * (1 - e2) / Math.Pow(1 - e2 * sinPhi * sinPhi, 1.5);
        double d = x / (n1 * K0);

        double lat = phi1 - (n1 * tanPhi / r1) *
            (d * d / 2
             - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * ep2) * Math.Pow(d, 4) / 24
             + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lon0 = (zone * 6 - 183) * Math.PI / 180.0;
        double lon = lon0 + (d
            - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6
            + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / cosPhi;

        return new LonLat(lon * 180.0 / Math.PI, lat * 180.0 / Math.PI);
    }
}
