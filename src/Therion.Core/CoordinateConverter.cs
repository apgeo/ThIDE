// UTIL-01 — WGS84 lat/long ↔ UTM conversion (Snyder transverse-Mercator series, USGS PP-1395).
// Pure + unit-testable; the UI uses it to turn a pasted coordinate into a Therion `fix` line.

using System;

namespace Therion.Core;

public readonly record struct UtmCoordinate(int Zone, bool North, double Easting, double Northing)
{
    /// <summary>Zone + hemisphere label, e.g. "33N".</summary>
    public string ZoneLabel => $"{Zone}{(North ? 'N' : 'S')}";
}

public static class CoordinateConverter
{
    // WGS84.
    private const double A = 6378137.0;
    private const double F = 1.0 / 298.257223563;
    private const double K0 = 0.9996;
    private static readonly double E2 = F * (2 - F);
    private static readonly double Ep2 = E2 / (1 - E2);

    public static int ZoneFor(double lonDeg)
    {
        int z = (int)Math.Floor((lonDeg + 180.0) / 6.0) + 1;
        return Math.Clamp(z, 1, 60);
    }

    private static double CentralMeridian(int zone) => zone * 6 - 183;

    public static UtmCoordinate LatLonToUtm(double latDeg, double lonDeg, int? forceZone = null)
    {
        int zone = forceZone ?? ZoneFor(lonDeg);
        double lat = latDeg * Math.PI / 180.0;
        double lon = lonDeg * Math.PI / 180.0;
        double cm = CentralMeridian(zone) * Math.PI / 180.0;

        double n = A / Math.Sqrt(1 - E2 * Sin2(lat));
        double t = Math.Pow(Math.Tan(lat), 2);
        double c = Ep2 * Math.Pow(Math.Cos(lat), 2);
        double a = Math.Cos(lat) * (lon - cm);
        double m = MeridianArc(lat);

        double easting = K0 * n * (a + (1 - t + c) * Math.Pow(a, 3) / 6
                         + (5 - 18 * t + t * t + 72 * c - 58 * Ep2) * Math.Pow(a, 5) / 120) + 500000.0;
        double northing = K0 * (m + n * Math.Tan(lat) * (a * a / 2
                          + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a, 4) / 24
                          + (61 - 58 * t + t * t + 600 * c - 330 * Ep2) * Math.Pow(a, 6) / 720));

        bool north = latDeg >= 0;
        if (!north) northing += 10000000.0;
        return new UtmCoordinate(zone, north, easting, northing);
    }

    public static (double Lat, double Lon) UtmToLatLon(UtmCoordinate utm)
    {
        double x = utm.Easting - 500000.0;
        double y = utm.North ? utm.Northing : utm.Northing - 10000000.0;
        double cm = CentralMeridian(utm.Zone) * Math.PI / 180.0;

        double m = y / K0;
        double mu = m / (A * (1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256));
        double e1 = (1 - Math.Sqrt(1 - E2)) / (1 + Math.Sqrt(1 - E2));

        double phi1 = mu
            + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
            + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu)
            + (1097 * Math.Pow(e1, 4) / 512) * Math.Sin(8 * mu);

        double c1 = Ep2 * Math.Pow(Math.Cos(phi1), 2);
        double t1 = Math.Pow(Math.Tan(phi1), 2);
        double n1 = A / Math.Sqrt(1 - E2 * Sin2(phi1));
        double r1 = A * (1 - E2) / Math.Pow(1 - E2 * Sin2(phi1), 1.5);
        double d = x / (n1 * K0);

        double lat = phi1 - (n1 * Math.Tan(phi1) / r1) * (d * d / 2
            - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * Ep2) * Math.Pow(d, 4) / 24
            + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * Ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lon = cm + (d - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6
            + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * Ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / Math.Cos(phi1);

        return (lat * 180.0 / Math.PI, lon * 180.0 / Math.PI);
    }

    private static double Sin2(double r) => Math.Pow(Math.Sin(r), 2);

    private static double MeridianArc(double lat) => A * (
        (1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256) * lat
        - (3 * E2 / 8 + 3 * E2 * E2 / 32 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(2 * lat)
        + (15 * E2 * E2 / 256 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(4 * lat)
        - (35 * E2 * E2 * E2 / 3072) * Math.Sin(6 * lat));
}
