// World Magnetic Model / IGRF spherical-harmonic synthesis for magnetic declination.
//
// The math is standard (Schmidt semi-normalised associated Legendre functions over a geodetic →
// geocentric converted position). Coefficients come from a NOAA WMM.COF-format file (public domain,
// downloaded by the user / bundled), so the engine here is data-driven and unit-testable with a
// controlled coefficient set (an axial dipole must yield zero declination everywhere; an equatorial
// term must not).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Therion.Core;

/// <summary>Magnetic field elements at a point/time (angles in degrees, intensities in nT).</summary>
public readonly record struct MagneticResult(
    double Declination, double Inclination, double HorizontalIntensity, double TotalIntensity,
    double North, double East, double Down);

public sealed class GeoMagneticModel
{
    // Geomagnetic reference radius (km) and WGS84 ellipsoid (km).
    private const double Re = 6371.2;
    private const double WgsA = 6378.137;
    private const double WgsB = 6356.7523142;

    private readonly int _nMax;
    private readonly double[,] _g, _h, _gd, _hd;   // [n, m] main + secular-variation (nT, nT/yr)
    private readonly double[,] _schmidt;           // Schmidt factor S^m_n

    public double Epoch { get; }
    public string Name { get; }

    private GeoMagneticModel(int nMax, double[,] g, double[,] h, double[,] gd, double[,] hd, double epoch, string name)
    {
        _nMax = nMax; _g = g; _h = h; _gd = gd; _hd = hd; Epoch = epoch; Name = name;
        _schmidt = BuildSchmidt(nMax);
    }

    /// <summary>Parses a NOAA WMM.COF-format string (header line, then "n m g h gd hd" rows).</summary>
    public static GeoMagneticModel FromCof(string text)
    {
        double epoch = 0; string name = "model";
        var rows = new List<(int n, int m, double g, double h, double gd, double hd)>();
        int nMax = 0;
        bool headerRead = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (!headerRead)
            {
                // Header: <epoch> <name> <date>
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out epoch))
                {
                    name = parts.Length > 1 ? parts[1] : "model";
                    headerRead = true;
                    continue;
                }
                continue;
            }

            if (line.StartsWith("9999") || parts.Length < 6) break;   // terminator
            if (!int.TryParse(parts[0], out var n) || !int.TryParse(parts[1], out var m)) continue;
            rows.Add((n, m,
                D(parts[2]), D(parts[3]), D(parts[4]), D(parts[5])));
            if (n > nMax) nMax = n;
        }
        if (!headerRead || nMax == 0) throw new FormatException("Not a recognisable WMM/IGRF .COF file.");

        var g = new double[nMax + 1, nMax + 1];
        var h = new double[nMax + 1, nMax + 1];
        var gd = new double[nMax + 1, nMax + 1];
        var hd = new double[nMax + 1, nMax + 1];
        foreach (var (n, m, gnm, hnm, dgnm, dhnm) in rows)
        {
            if (n > nMax || m > n) continue;
            g[n, m] = gnm; h[n, m] = hnm; gd[n, m] = dgnm; hd[n, m] = dhnm;
        }
        return new GeoMagneticModel(nMax, g, h, gd, hd, epoch, name);

        static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Builds a model directly from coefficient arrays (used by tests).</summary>
    public static GeoMagneticModel FromCoefficients(int nMax, double[,] g, double[,] h, double epoch = 2025.0) =>
        new(nMax, g, h, new double[nMax + 1, nMax + 1], new double[nMax + 1, nMax + 1], epoch, "test");

    /// <summary>Computes the field at a geodetic position and decimal year.</summary>
    public MagneticResult Calculate(double latDeg, double lonDeg, double altKm, double decimalYear)
    {
        double dt = decimalYear - Epoch;
        double lat = latDeg * Math.PI / 180.0;
        double lon = lonDeg * Math.PI / 180.0;

        // Geodetic → geocentric (r, colatitude θ, and the ψ rotation back to geodetic axes).
        double sp = Math.Sin(lat), cp = Math.Cos(lat);
        double a2 = WgsA * WgsA, b2 = WgsB * WgsB;
        double rho = Math.Sqrt(a2 * cp * cp + b2 * sp * sp);
        double r = Math.Sqrt(altKm * altKm + 2 * altKm * rho + (a2 * a2 * cp * cp + b2 * b2 * sp * sp) / (rho * rho));
        double cd = (altKm + rho) / r;                          // cos ψ
        double sd = (a2 - b2) * sp * cp / (rho * r);            // sin ψ
        double spc = sp * cd - cp * sd;                         // sin(geocentric lat)
        double cpc = cp * cd + sp * sd;                         // cos(geocentric lat)

        double ct = spc;                                        // cos θ  (θ = colatitude)
        double st = cpc;                                        // sin θ
        if (Math.Abs(st) < 1e-8) st = 1e-8;                     // pole guard

        // Schmidt-normalised Ferrers functions P^m_n(cosθ) and dP/dθ.
        var (p, dp) = LegendreSchmidt(ct, st);

        double ar = Re / r;
        double x = 0, y = 0, z = 0;   // geocentric north / east / down
        for (int n = 1; n <= _nMax; n++)
        {
            double arn2 = Math.Pow(ar, n + 2);
            for (int m = 0; m <= n; m++)
            {
                double g = _g[n, m] + dt * _gd[n, m];
                double h = _h[n, m] + dt * _hd[n, m];
                double cosml = Math.Cos(m * lon), sinml = Math.Sin(m * lon);
                double gc = g * cosml + h * sinml;
                x += arn2 * gc * dp[n, m];
                y += arn2 * m * (g * sinml - h * cosml) * p[n, m] / st;
                z -= arn2 * (n + 1) * gc * p[n, m];
            }
        }

        // Rotate geocentric (x,z) back to the geodetic frame.
        double xtemp = x;
        x = x * cd + z * sd;
        z = z * cd - xtemp * sd;

        double hIntensity = Math.Sqrt(x * x + y * y);
        double f = Math.Sqrt(hIntensity * hIntensity + z * z);
        double decl = Math.Atan2(y, x) * 180.0 / Math.PI;
        double incl = Math.Atan2(z, hIntensity) * 180.0 / Math.PI;
        return new MagneticResult(decl, incl, hIntensity, f, x, y, z);
    }

    /// <summary>Convenience: just the declination in degrees (east positive).</summary>
    public double Declination(double latDeg, double lonDeg, double altKm, double decimalYear) =>
        Calculate(latDeg, lonDeg, altKm, decimalYear).Declination;

    // Ferrers associated Legendre P^m_n(cosθ) and dP/dθ, then Schmidt-normalised.
    private (double[,] P, double[,] dP) LegendreSchmidt(double ct, double st)
    {
        int n = _nMax;
        var pf = new double[n + 1, n + 1];   // unnormalised Ferrers
        pf[0, 0] = 1.0;
        for (int mm = 1; mm <= n; mm++)
            pf[mm, mm] = (2 * mm - 1) * st * pf[mm - 1, mm - 1];     // P^m_m = (2m-1)!! sin^m θ
        for (int mm = 0; mm <= n; mm++)
            for (int nn = mm + 1; nn <= n; nn++)
            {
                double prev2 = nn - 2 >= mm ? pf[nn - 2, mm] : 0.0;
                pf[nn, mm] = (ct * (2 * nn - 1) * pf[nn - 1, mm] - (nn + mm - 1) * prev2) / (nn - mm);
            }

        var p = new double[n + 1, n + 1];
        var dp = new double[n + 1, n + 1];
        for (int nn = 0; nn <= n; nn++)
            for (int mm = 0; mm <= nn; mm++)
            {
                double s = _schmidt[nn, mm];
                p[nn, mm] = s * pf[nn, mm];
                // sinθ dP/dθ = n cosθ P^m_n − (n+m) P^m_{n−1}
                double pm1 = nn - 1 >= mm ? pf[nn - 1, mm] : 0.0;
                dp[nn, mm] = s * (nn * ct * pf[nn, mm] - (nn + mm) * pm1) / st;
            }
        return (p, dp);
    }

    private static double[,] BuildSchmidt(int nMax)
    {
        var s = new double[nMax + 1, nMax + 1];
        for (int n = 0; n <= nMax; n++)
            for (int m = 0; m <= n; m++)
            {
                double ratio = 1.0;                       // (n-m)! / (n+m)!
                for (int i = n - m + 1; i <= n + m; i++) ratio /= i;
                s[n, m] = Math.Sqrt((m == 0 ? 1.0 : 2.0) * ratio);
            }
        return s;
    }
}
