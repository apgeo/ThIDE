// Stereographic projection math for the structural-geology stereonet (Wulff / Schmidt).
//
// Lower-hemisphere, north = +Y, east = +X, unit disc (R = 1). Pure functions over the same
// E/N/Up frame as Vec3; angles in degrees, azimuths clockwise from north in [0, 360).
//   equal-angle (Wulff):  r = tan(45° − plunge/2)   — preserves angles
//   equal-area (Schmidt): r = √2·sin(45° − plunge/2) — preserves area
// Both map plunge 0 → r = 1 (primitive circle) and plunge 90 → r = 0 (centre).

using System;
using System.Collections.Immutable;

namespace Therion.Structural;

/// <summary>Which azimuthal projection the stereonet uses.</summary>
public enum StereonetProjection
{
    /// <summary>Equal-angle (Wulff net): preserves angular relationships.</summary>
    EqualAngle,
    /// <summary>Equal-area (Schmidt net): preserves area, standard for density work.</summary>
    EqualArea,
}

/// <summary>A point on the unit stereonet disc: X east, Y north, centre = vertical.</summary>
public readonly record struct StereonetPoint(double X, double Y);

/// <summary>The net's reference grid: great circles + small circles, pre-projected polylines.</summary>
public sealed record StereonetGraticule(
    ImmutableArray<ImmutableArray<StereonetPoint>> GreatCircles,
    ImmutableArray<ImmutableArray<StereonetPoint>> SmallCircles);

/// <summary>Lower-hemisphere stereographic projection (Wulff / Schmidt) of lines and planes.</summary>
public static class Stereonet
{
    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    /// <summary>Radial distance from the disc centre for a line plunging <paramref name="plungeDeg"/>.</summary>
    public static double Radius(double plungeDeg, StereonetProjection projection)
    {
        double half = (45.0 - plungeDeg / 2.0) * DegToRad;
        return projection == StereonetProjection.EqualAngle
            ? Math.Tan(half)
            : Math.Sqrt(2.0) * Math.Sin(half);
    }

    /// <summary>Projects a line (trend/plunge) to unit-disc coordinates.</summary>
    public static StereonetPoint ProjectLine(double trendDeg, double plungeDeg, StereonetProjection projection)
    {
        double r = Radius(plungeDeg, projection);
        double t = trendDeg * DegToRad;
        return new StereonetPoint(r * Math.Sin(t), r * Math.Cos(t));
    }

    /// <summary>
    /// Inverse projection for a cursor readout: disc coordinates → trend/plunge. False when the
    /// point lies outside the primitive circle (small tolerance for edge clicks).
    /// </summary>
    public static bool TryInverse(double x, double y, StereonetProjection projection,
        out double trendDeg, out double plungeDeg)
    {
        trendDeg = 0;
        plungeDeg = 0;
        double r = Math.Sqrt(x * x + y * y);
        if (r > 1.0 + 1e-9) return false;
        r = Math.Min(r, 1.0);
        double halfDeg = projection == StereonetProjection.EqualAngle
            ? Math.Atan(r) * RadToDeg
            : Math.Asin(Math.Min(1.0, r / Math.Sqrt(2.0))) * RadToDeg;
        plungeDeg = 90.0 - 2.0 * halfDeg;
        trendDeg = r < 1e-12 ? 0.0 : PlaneFitter.NormalizeAzimuth(Math.Atan2(x, y) * RadToDeg);
        return true;
    }

    /// <summary>
    /// Trend/plunge of a direction vector. Upper-hemisphere input is flipped to its antipode
    /// (lines are undirected), so the result always plots on the lower hemisphere.
    /// </summary>
    public static (double Trend, double Plunge) ToTrendPlunge(Vec3 direction)
    {
        var v = direction.Normalized();
        if (v == Vec3.Zero) return (0, 90);
        if (v.Z > 0) v = -1.0 * v;
        double plunge = Math.Asin(Math.Clamp(-v.Z, 0.0, 1.0)) * RadToDeg;
        double trend = (Math.Abs(v.E) < 1e-12 && Math.Abs(v.N) < 1e-12)
            ? 0.0
            : PlaneFitter.NormalizeAzimuth(Math.Atan2(v.E, v.N) * RadToDeg);
        return (trend, plunge);
    }

    /// <summary>Upward unit normal of a plane given strike/dip (right-hand rule).</summary>
    public static Vec3 UpwardNormal(double strikeDeg, double dipDeg)
    {
        double dipDir = (strikeDeg + 90.0) * DegToRad;
        double d = dipDeg * DegToRad;
        return new Vec3(Math.Sin(dipDir) * Math.Sin(d), Math.Cos(dipDir) * Math.Sin(d), Math.Cos(d));
    }

    /// <summary>Pole to a plane (the downward normal) as trend/plunge.</summary>
    public static (double Trend, double Plunge) PoleOfPlane(double strikeDeg, double dipDeg) =>
        (PlaneFitter.NormalizeAzimuth(strikeDeg + 270.0), 90.0 - dipDeg);

    /// <summary>
    /// A shot direction (azimuth/clino, degrees) as an undirected line in trend/plunge, with an
    /// optional declination added to the azimuth. Up-pointing shots flip to their antipode.
    /// </summary>
    public static (double Trend, double Plunge) ShotToLine(double compassDeg, double clinoDeg, double declinationDeg = 0)
    {
        double az = compassDeg + declinationDeg;
        return clinoDeg >= 0
            ? (PlaneFitter.NormalizeAzimuth(az + 180.0), Math.Min(clinoDeg, 90.0))
            : (PlaneFitter.NormalizeAzimuth(az), Math.Min(-clinoDeg, 90.0));
    }

    /// <summary>
    /// The lower-hemisphere half of a plane's great circle as a 3-D unit-vector polyline
    /// (E/N/Up frame, Z ≤ 0), from the plane's normal. A horizontal plane yields the full
    /// horizontal circle. Used directly by the 3-D inset and, projected, by the 2-D net.
    /// </summary>
    public static ImmutableArray<Vec3> GreatCircle3D(Vec3 normal, double stepDeg = 2)
    {
        var n = normal.Normalized();
        if (n == Vec3.Zero) return ImmutableArray<Vec3>.Empty;
        if (n.Z < 0) n = -1.0 * n;

        var b = ImmutableArray.CreateBuilder<Vec3>();
        double horizontal = Math.Sqrt(n.E * n.E + n.N * n.N);
        if (horizontal < 1e-9)
        {
            // Horizontal plane: its great circle is the whole primitive (horizontal) circle.
            for (double a = 0; a <= 360.0 + 1e-9; a += stepDeg)
                b.Add(new Vec3(Math.Sin(a * DegToRad), Math.Cos(a * DegToRad), 0));
            return b.ToImmutable();
        }

        // u = horizontal strike direction, d = down-dip in-plane direction (d.Z ≤ 0).
        var u = new Vec3(n.N, -n.E, 0).Normalized();
        var d = n.Cross(u);
        for (double a = 0; a <= 180.0 + 1e-9; a += stepDeg)
        {
            double rad = Math.Min(a, 180.0) * DegToRad;
            b.Add(Math.Cos(rad) * u + Math.Sin(rad) * d);
        }
        return b.ToImmutable();
    }

    /// <summary>A plane's great circle projected to the unit disc, from strike/dip.</summary>
    public static ImmutableArray<StereonetPoint> GreatCircle(double strikeDeg, double dipDeg,
        StereonetProjection projection, double stepDeg = 2) =>
        Project(GreatCircle3D(UpwardNormal(strikeDeg, dipDeg), stepDeg), projection);

    /// <summary>Projects a lower-hemisphere unit-vector polyline to the disc, point by point.</summary>
    public static ImmutableArray<StereonetPoint> Project(ImmutableArray<Vec3> polyline, StereonetProjection projection)
    {
        var b = ImmutableArray.CreateBuilder<StereonetPoint>(polyline.Length);
        foreach (var v in polyline)
        {
            var (trend, plunge) = ToTrendPlunge(v);
            b.Add(ProjectLine(trend, plunge, projection));
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// The net's reference grid. Great circles: N–S-striking planes dipping east and west every
    /// <paramref name="stepDeg"/>. Small circles: cones about the N–S horizontal axis every
    /// <paramref name="stepDeg"/>, clipped to the lower hemisphere (the 90° member is the E–W
    /// diameter). All polylines pre-projected and sampled at <paramref name="sampleStepDeg"/>.
    /// </summary>
    public static StereonetGraticule Graticule(StereonetProjection projection,
        double stepDeg = 10, double sampleStepDeg = 2)
    {
        var greats = ImmutableArray.CreateBuilder<ImmutableArray<StereonetPoint>>();
        for (double dip = stepDeg; dip < 90.0 - 1e-9; dip += stepDeg)
        {
            greats.Add(GreatCircle(0, dip, projection, sampleStepDeg));     // dipping east
            greats.Add(GreatCircle(180, dip, projection, sampleStepDeg));   // dipping west
        }
        greats.Add(GreatCircle(0, 90, projection, sampleStepDeg));          // N–S diameter

        var smalls = ImmutableArray.CreateBuilder<ImmutableArray<StereonetPoint>>();
        for (double rho = stepDeg; rho < 90.0 - 1e-9; rho += stepDeg)
        {
            smalls.Add(SmallCircle(new Vec3(0, 1, 0), rho, projection, sampleStepDeg));  // about north
            smalls.Add(SmallCircle(new Vec3(0, -1, 0), rho, projection, sampleStepDeg)); // about south
        }
        smalls.Add(SmallCircle(new Vec3(0, 1, 0), 90, projection, sampleStepDeg));       // E–W diameter

        return new StereonetGraticule(greats.ToImmutable(), smalls.ToImmutable());
    }

    /// <summary>
    /// The lower-hemisphere arc of a cone of half-apical angle <paramref name="rhoDeg"/> about a
    /// horizontal <paramref name="axis"/>, projected to the disc.
    /// </summary>
    internal static ImmutableArray<StereonetPoint> SmallCircle(Vec3 axis, double rhoDeg,
        StereonetProjection projection, double sampleStepDeg = 2)
    {
        var a = axis.Normalized();
        var east = new Vec3(a.N, -a.E, 0).Normalized();   // horizontal, perpendicular to the axis
        var down = new Vec3(0, 0, -1);
        double rho = rhoDeg * DegToRad;

        var b = ImmutableArray.CreateBuilder<Vec3>();
        for (double phi = 0; phi <= 180.0 + 1e-9; phi += sampleStepDeg)
        {
            double p = Math.Min(phi, 180.0) * DegToRad;
            b.Add(Math.Cos(rho) * a + Math.Sin(rho) * (Math.Cos(p) * east + Math.Sin(p) * down));
        }
        return Project(b.ToImmutable(), projection);
    }
}
