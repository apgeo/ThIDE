// Known-value tests for the stereonet projection math (Wulff equal-angle / Schmidt equal-area).
// Conventions under test: lower hemisphere, north = +Y, east = +X, unit disc.

using System;
using System.Collections.Immutable;
using System.Linq;
using Therion.Structural;
using Xunit;

namespace Therion.Structural.Tests;

public class StereonetTests
{
    private const double Tol = 1e-9;

    // ---- radial law -----------------------------------------------------------------------------

    [Theory]
    [InlineData(StereonetProjection.EqualAngle)]
    [InlineData(StereonetProjection.EqualArea)]
    public void Radius_IsOneOnPrimitive_AndZeroAtCentre(StereonetProjection p)
    {
        Assert.Equal(1.0, Stereonet.Radius(0, p), 12);
        Assert.Equal(0.0, Stereonet.Radius(90, p), 12);
    }

    [Fact]
    public void Radius_KnownValues_AtPlunge30()
    {
        // Wulff: tan(45° − 15°) = tan 30°;  Schmidt: √2·sin 30° = √2/2.
        Assert.Equal(Math.Tan(30 * Math.PI / 180), Stereonet.Radius(30, StereonetProjection.EqualAngle), 12);
        Assert.Equal(Math.Sqrt(2) / 2, Stereonet.Radius(30, StereonetProjection.EqualArea), 12);
    }

    [Fact]
    public void Radius_SchmidtPlotsShallowLinesFartherOutThanWulff()
    {
        for (double p = 10; p < 90; p += 10)
            Assert.True(Stereonet.Radius(p, StereonetProjection.EqualArea) >
                        Stereonet.Radius(p, StereonetProjection.EqualAngle));
    }

    // ---- point projection -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 1)]     // north on the primitive
    [InlineData(90, 1, 0)]    // east
    [InlineData(180, 0, -1)]  // south
    [InlineData(270, -1, 0)]  // west
    public void ProjectLine_HorizontalLine_LandsOnPrimitiveCircle(double trend, double x, double y)
    {
        var pt = Stereonet.ProjectLine(trend, 0, StereonetProjection.EqualAngle);
        Assert.Equal(x, pt.X, 12);
        Assert.Equal(y, pt.Y, 12);
    }

    [Theory]
    [InlineData(StereonetProjection.EqualAngle)]
    [InlineData(StereonetProjection.EqualArea)]
    public void ProjectLine_VerticalLine_LandsOnCentre(StereonetProjection p)
    {
        var pt = Stereonet.ProjectLine(123, 90, p);
        Assert.Equal(0, pt.X, 12);
        Assert.Equal(0, pt.Y, 12);
    }

    [Theory]
    [InlineData(StereonetProjection.EqualAngle)]
    [InlineData(StereonetProjection.EqualArea)]
    public void TryInverse_RoundTripsProjectLine(StereonetProjection p)
    {
        foreach (double trend in new[] { 0.0, 37.0, 90.0, 200.5, 359.0 })
            foreach (double plunge in new[] { 0.0, 15.0, 45.0, 89.0 })
            {
                var pt = Stereonet.ProjectLine(trend, plunge, p);
                Assert.True(Stereonet.TryInverse(pt.X, pt.Y, p, out var t, out var pl));
                Assert.Equal(trend, t, 6);
                Assert.Equal(plunge, pl, 6);
            }
    }

    [Fact]
    public void TryInverse_OutsidePrimitive_ReturnsFalse() =>
        Assert.False(Stereonet.TryInverse(0.9, 0.9, StereonetProjection.EqualAngle, out _, out _));

    // ---- directions -----------------------------------------------------------------------------

    [Fact]
    public void ToTrendPlunge_KnownDirections()
    {
        Assert.Equal((90.0, 0.0), Stereonet.ToTrendPlunge(new Vec3(1, 0, 0)));
        Assert.Equal((0.0, 90.0), Stereonet.ToTrendPlunge(new Vec3(0, 0, -1)));
        var (t, p) = Stereonet.ToTrendPlunge(new Vec3(0, 1, -1));
        Assert.Equal(0.0, t, 9);
        Assert.Equal(45.0, p, 9);
    }

    [Fact]
    public void ToTrendPlunge_FlipsUpperHemisphereToAntipode()
    {
        // Up-and-north is the same undirected line as down-and-south.
        var (t, p) = Stereonet.ToTrendPlunge(new Vec3(0, 1, 1));
        Assert.Equal(180.0, t, 9);
        Assert.Equal(45.0, p, 9);
    }

    [Fact]
    public void UpwardNormal_EastDippingPlane_LeansEast()
    {
        var n = Stereonet.UpwardNormal(0, 45);   // strike N, dips east (RHR)
        Assert.Equal(Math.Sin(Math.PI / 4), n.E, 12);
        Assert.Equal(0.0, n.N, 12);
        Assert.Equal(Math.Cos(Math.PI / 4), n.Z, 12);
    }

    [Theory]
    [InlineData(0, 45, 270, 45)]     // dips east → pole plunges 45 toward west
    [InlineData(315, 60, 225, 30)]
    [InlineData(90, 0, 0, 90)]       // horizontal plane → vertical pole
    public void PoleOfPlane_KnownValues(double strike, double dip, double trend, double plunge)
    {
        var (t, p) = Stereonet.PoleOfPlane(strike, dip);
        Assert.Equal(trend, t, 9);
        Assert.Equal(plunge, p, 9);
    }

    [Theory]
    [InlineData(40, -30, 0, 40, 30)]     // down-shot plots as-is
    [InlineData(40, 30, 0, 220, 30)]     // up-shot flips to its antipode
    [InlineData(40, -30, 2, 42, 30)]     // declination shifts the azimuth
    [InlineData(350, 20, 0, 170, 20)]    // flip wraps around 360
    public void ShotToLine_KnownValues(double compass, double clino, double decl, double trend, double plunge)
    {
        var (t, p) = Stereonet.ShotToLine(compass, clino, decl);
        Assert.Equal(trend, t, 9);
        Assert.Equal(plunge, p, 9);
    }

    // ---- great circles --------------------------------------------------------------------------

    [Fact]
    public void GreatCircle3D_StaysOnLowerUnitHemisphere_WithHorizontalEndpoints()
    {
        var arc = Stereonet.GreatCircle3D(Stereonet.UpwardNormal(30, 60));
        Assert.True(arc.Length > 10);
        foreach (var v in arc)
        {
            Assert.Equal(1.0, v.Length, 9);
            Assert.True(v.Z <= 1e-9);
        }
        Assert.Equal(0.0, arc[0].Z, 9);
        Assert.Equal(0.0, arc[^1].Z, 9);
    }

    [Fact]
    public void GreatCircle3D_MidPoint_IsTheDipVector()
    {
        // Strike 0, dip 45 (dips east): the deepest arc point is the dip vector at trend 90, plunge 45.
        var arc = Stereonet.GreatCircle3D(Stereonet.UpwardNormal(0, 45), stepDeg: 2);
        var deepest = arc.OrderBy(v => v.Z).First();
        var (t, p) = Stereonet.ToTrendPlunge(deepest);
        Assert.Equal(90.0, t, 6);
        Assert.Equal(45.0, p, 6);
    }

    [Fact]
    public void GreatCircle3D_HorizontalPlane_IsTheFullPrimitiveCircle()
    {
        var arc = Stereonet.GreatCircle3D(new Vec3(0, 0, 1));
        Assert.True(arc.Length > 90);
        foreach (var v in arc)
        {
            Assert.Equal(0.0, v.Z, 12);
            Assert.Equal(1.0, v.Length, 9);
        }
    }

    [Fact]
    public void GreatCircle_VerticalNorthSouthPlane_ProjectsToTheDiameter()
    {
        var arc = Stereonet.GreatCircle(0, 90, StereonetProjection.EqualAngle);
        foreach (var pt in arc)
            Assert.Equal(0.0, pt.X, 9);
        Assert.Contains(arc, pt => Math.Abs(pt.Y - 1) < 1e-6);
        Assert.Contains(arc, pt => Math.Abs(pt.Y + 1) < 1e-6);
        Assert.Contains(arc, pt => Math.Abs(pt.Y) < 1e-6);
    }

    [Fact]
    public void GreatCircle_EastDipping45_PassesThroughKnownWulffPoint()
    {
        // The dip vector (trend 90, plunge 45) projects at x = tan(22.5°), y = 0 on a Wulff net.
        var arc = Stereonet.GreatCircle(0, 45, StereonetProjection.EqualAngle);
        double expected = Math.Tan(22.5 * Math.PI / 180);
        Assert.Contains(arc, pt => Math.Abs(pt.X - expected) < 1e-6 && Math.Abs(pt.Y) < 1e-6);
    }

    // ---- graticule ------------------------------------------------------------------------------

    [Theory]
    [InlineData(StereonetProjection.EqualAngle)]
    [InlineData(StereonetProjection.EqualArea)]
    public void Graticule_HasExpectedFamilies_AllInsideTheDisc(StereonetProjection p)
    {
        var g = Stereonet.Graticule(p);
        Assert.Equal(17, g.GreatCircles.Length);   // 8 dips × E/W + the N–S diameter
        Assert.Equal(17, g.SmallCircles.Length);   // 8 cones × N/S + the E–W diameter

        foreach (var line in g.GreatCircles.Concat(g.SmallCircles))
        {
            Assert.True(line.Length > 2);
            foreach (var pt in line)
                Assert.True(Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y) <= 1.0 + 1e-6);
        }
    }
}
