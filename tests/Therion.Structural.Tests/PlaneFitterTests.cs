// STRUCT-01 Phase 1 — locks the plane-fit math against analytically known planes BEFORE any UI work.
// Covers horizontal/vertical/steep/arbitrary orientations, degenerate input, noise, count-independence
// and the declination azimuth offset.

using System;
using System.Collections.Generic;
using Therion.Structural;

namespace Therion.Structural.Tests;

public class PlaneFitterTests
{
    private const double Deg = Math.PI / 180.0;

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Upward unit normal for a plane of the given dip / dip-direction (degrees).</summary>
    private static Vec3 NormalFrom(double dipDeg, double dipDirDeg)
    {
        double dip = dipDeg * Deg, az = dipDirDeg * Deg;
        return new Vec3(Math.Sin(dip) * Math.Sin(az), Math.Sin(dip) * Math.Cos(az), Math.Cos(dip));
    }

    /// <summary>A perfectly planar grid of points for a plane of the given dip / dip-direction.</summary>
    private static List<Vec3> PlanePoints(double dipDeg, double dipDirDeg, Vec3 centroid,
        int perAxis = 4, double spacing = 1.0)
    {
        var normal = NormalFrom(dipDeg, dipDirDeg);
        var reference = Math.Abs(normal.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        var u = normal.Cross(reference).Normalized();
        var w = normal.Cross(u).Normalized();

        var pts = new List<Vec3>();
        double mid = (perAxis - 1) / 2.0;
        for (int i = 0; i < perAxis; i++)
            for (int j = 0; j < perAxis; j++)
                pts.Add(centroid + u * ((i - mid) * spacing) + w * ((j - mid) * spacing));
        return pts;
    }

    /// <summary>Asserts two angles are equal modulo <paramref name="mod"/> within <paramref name="tol"/> (degrees).</summary>
    private static void AssertAngleModClose(double expected, double actual, double mod, double tol)
    {
        double d = ((expected - actual) % mod + mod) % mod;
        d = Math.Min(d, mod - d);
        Assert.True(d <= tol, $"expected {expected}° actual {actual}° (mod {mod}, Δ {d:0.###} > {tol})");
    }

    // ---- known planes --------------------------------------------------------------------------

    [Fact]
    public void HorizontalPlane_NormalUp_DipZero()
    {
        var p = new List<Vec3> { new(0, 0, 5), new(1, 0, 5), new(0, 1, 5), new(2, 1, 5), new(1, 2, 5) };

        var f = PlaneFitter.Fit(p);

        Assert.True(f.IsValid);
        Assert.Equal(0, f.Dip, 4);
        Assert.True(Math.Abs(f.Normal.Z) > 0.9999);    // normal ≈ (0,0,1)
        Assert.Equal(0, f.RmsResidual, 6);
        Assert.Equal(5, f.Centroid.Z, 6);
    }

    [Fact]
    public void VerticalPlane_StrikingNorthSouth_DipNinety()
    {
        // Plane E = const → contains the N and Z axes, normal points east.
        var p = new List<Vec3> { new(2, 0, 0), new(2, 1, 0), new(2, 2, 1), new(2, 0, 2), new(2, 3, 2) };

        var f = PlaneFitter.Fit(p);

        Assert.True(f.IsValid);
        Assert.Equal(90, f.Dip, 3);
        Assert.True(Math.Abs(f.Normal.Z) < 1e-6);
        AssertAngleModClose(0, f.Strike, 180, 0.01);    // N–S strike
    }

    [Fact]
    public void VerticalPlane_StrikingEastWest_DipNinety()
    {
        // Plane N = const → contains the E and Z axes, normal points north.
        var p = new List<Vec3> { new(0, 4, 0), new(1, 4, 0), new(2, 4, 1), new(0, 4, 2), new(3, 4, 2) };

        var f = PlaneFitter.Fit(p);

        Assert.True(f.IsValid);
        Assert.Equal(90, f.Dip, 3);
        AssertAngleModClose(90, f.Strike, 180, 0.01);   // E–W strike
    }

    [Fact]
    public void PlaneDippingEastAt45_RecoversDipDirectionAndStrike()
    {
        // Plane Z = −E  ⇒ descends towards the east at 45°; normal (1,0,1)/√2.
        var p = new List<Vec3>
        {
            new(0, 0, 0), new(0, 1, 0), new(0, 2, 0),
            new(1, 0, -1), new(1, 1, -1), new(2, 0, -2), new(2, 2, -2),
        };

        var f = PlaneFitter.Fit(p);

        Assert.True(f.IsValid);
        Assert.Equal(45, f.Dip, 3);
        AssertAngleModClose(90, f.DipDirection, 360, 0.01);   // dips due east
        AssertAngleModClose(0, f.Strike, 360, 0.01);          // strike = dipDir − 90
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(30, 120)]
    [InlineData(45, 200)]
    [InlineData(60, 305)]
    [InlineData(80, 47)]
    public void ArbitraryPlane_RoundTrips(double dip, double dipDir)
    {
        var expectedNormal = NormalFrom(dip, dipDir);
        var pts = PlanePoints(dip, dipDir, new Vec3(100, 200, 50));

        var f = PlaneFitter.Fit(pts);

        Assert.True(f.IsValid);
        Assert.Equal(dip, f.Dip, 3);
        AssertAngleModClose(dipDir, f.DipDirection, 360, 0.05);
        AssertAngleModClose(dipDir - 90, f.Strike, 360, 0.05);
        Assert.True(Math.Abs(f.Normal.Dot(expectedNormal)) > 0.9999);
        Assert.Equal(0, f.RmsResidual, 5);
    }

    // ---- degenerate input ----------------------------------------------------------------------

    [Fact]
    public void FewerThanThreePoints_IsInvalid()
    {
        var f = PlaneFitter.Fit(new List<Vec3> { new(0, 0, 0), new(1, 1, 1) });

        Assert.False(f.IsValid);
        Assert.Equal(2, f.PointCount);
        Assert.Contains("at least 3", f.ErrorReason);
    }

    [Fact]
    public void CollinearPoints_IsInvalid()
    {
        var f = PlaneFitter.Fit(new List<Vec3> { new(0, 0, 0), new(1, 1, 1), new(2, 2, 2), new(3, 3, 3) });

        Assert.False(f.IsValid);
        Assert.Contains("collinear", f.ErrorReason);
    }

    [Fact]
    public void CoincidentPoints_IsInvalid()
    {
        var f = PlaneFitter.Fit(new List<Vec3> { new(7, 7, 7), new(7, 7, 7), new(7, 7, 7) });

        Assert.False(f.IsValid);
        Assert.Contains("coincident", f.ErrorReason);
    }

    // ---- robustness ----------------------------------------------------------------------------

    [Fact]
    public void NoisyPlane_RecoversOrientation_WithPositiveResidual()
    {
        var rng = new Random(1234);
        var pts = PlanePoints(35, 150, new Vec3(0, 0, 0), perAxis: 6);
        var normal = NormalFrom(35, 150);
        // Perturb each point along the normal by up to ±2 cm.
        for (int i = 0; i < pts.Count; i++)
            pts[i] += normal * ((rng.NextDouble() - 0.5) * 0.04);

        var f = PlaneFitter.Fit(pts);

        Assert.True(f.IsValid);
        Assert.Equal(35, f.Dip, 0);                     // within ~1°
        AssertAngleModClose(150, f.DipDirection, 360, 3.0);
        Assert.InRange(f.RmsResidual, 1e-4, 0.05);
    }

    [Fact]
    public void ManyPoints_ScaleWithoutQualityLoss()
    {
        // 50 points on a steep plane — count independence (decision: fixed 3×3 solver).
        var pts = PlanePoints(72, 215, new Vec3(500, 500, 100), perAxis: 8); // 64 points
        Assert.True(pts.Count >= 50);

        var f = PlaneFitter.Fit(pts);

        Assert.True(f.IsValid);
        Assert.Equal(72, f.Dip, 3);
        AssertAngleModClose(215, f.DipDirection, 360, 0.05);
    }

    // ---- declination offset --------------------------------------------------------------------

    [Fact]
    public void WithDeclination_ShiftsAzimuthsNotDip()
    {
        var f = PlaneFitter.Fit(PlanePoints(40, 100, Vec3.Zero));
        Assert.True(f.IsValid);

        var t = f.WithDeclination(12.0);

        Assert.Equal(f.Dip, t.Dip, 9);                                  // dip invariant
        AssertAngleModClose(f.Strike + 12, t.Strike, 360, 1e-6);
        AssertAngleModClose(f.DipDirection + 12, t.DipDirection, 360, 1e-6);
        Assert.Equal(12.0, t.DeclinationApplied, 9);
    }

    [Fact]
    public void WithDeclination_Wraps_AndZeroIsNoOp()
    {
        var f = PlaneFitter.Fit(PlanePoints(40, 355, Vec3.Zero));
        Assert.True(f.IsValid);

        Assert.Same(f, f.WithDeclination(0));                           // no-op returns the same instance
        var t = f.WithDeclination(20);                                  // 355→? wraps below 360
        Assert.InRange(t.DipDirection, 0, 360);
        AssertAngleModClose(f.DipDirection + 20, t.DipDirection, 360, 1e-6);
    }

    [Theory]
    [InlineData(370, 10)]
    [InlineData(-15, 345)]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    public void NormalizeAzimuth_WrapsInto_0To360(double input, double expected)
        => Assert.Equal(expected, PlaneFitter.NormalizeAzimuth(input), 9);
}
