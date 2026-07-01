// STRUCT-01 Phase 2 — end-to-end facade: detect → fit → declination, plus Recompute on a subset and
// the effect of including the (off-plane) origin point.

using System.Collections.Generic;
using System.Linq;
using Therion.Semantics;
using Therion.Structural;
using static Therion.Structural.Tests.TestModel;

namespace Therion.Structural.Tests;

public class StructuralAnalysisTests
{
    // Shots from one geo station to a grid of points lying on a plane of the given dip / dip-direction.
    private static SemanticModel GeoPlaneModel(double dip, double dipDir, Vec3 center, int perAxis = 4)
    {
        var pts = PlanePoints(dip, dipDir, center, perAxis);
        var shots = pts.Select((p, i) => Shot("geo1", "p" + i, p)).ToArray();
        return Model(shots);
    }

    [Fact]
    public void Analyze_RecoversKnownPlane()
    {
        var model = GeoPlaneModel(30, 120, new Vec3(10, 20, 5));

        var result = StructuralAnalysis.Analyze(model, new StructuralOptions());

        var plane = Assert.Single(result.Planes);
        Assert.True(plane.IsValid, plane.ErrorReason);
        Assert.Equal(30, plane.Dip, 2);
        Assert.Equal(120, plane.DipDirection, 1);
        Assert.Equal(0, plane.DeclinationApplied, 9);
        Assert.NotEmpty(result.CaveLegs);   // the geo shots double as legs → a cave line exists
    }

    [Fact]
    public void Recompute_OnThreePointSubset_IsValid()
    {
        var model = GeoPlaneModel(40, 200, new Vec3(0, 0, 0));
        var result = StructuralAnalysis.Analyze(model, new StructuralOptions());
        var batch = result.Batches[0];

        // Three non-collinear points from the 4×4 grid (indices 0/6/9 span both in-plane axes).
        var pts = batch.Measurements.Where(m => !m.IsOrigin).ToList();
        var subset = new List<StructuralMeasurement> { pts[0], pts[6], pts[9] };
        var plane = StructuralAnalysis.Recompute(batch, subset, 0);

        Assert.True(plane.IsValid);
        Assert.Equal(40, plane.Dip, 2);
    }

    [Fact]
    public void Declination_Manual_ShiftsStrikeNotDip()
    {
        var model = GeoPlaneModel(35, 110, new Vec3(5, 5, 0));

        var plain = StructuralAnalysis.Analyze(model, new StructuralOptions()).Planes[0];
        var shifted = StructuralAnalysis.Analyze(model, new StructuralOptions
        {
            Declination = new DeclinationOptions { Source = DeclinationSource.Manual, ManualDegrees = 7 },
        }).Planes[0];

        Assert.Equal(7, shifted.DeclinationApplied, 9);
        Assert.Equal(plain.Dip, shifted.Dip, 9);                                  // dip invariant
        Assert.Equal(PlaneFitter.NormalizeAzimuth(plain.Strike + 7), shifted.Strike, 4);
    }

    [Fact]
    public void IncludingOffPlaneOrigin_WorsensTheFit()
    {
        // A vertical plane at E = 10; the station (origin) sits 10 m off it.
        var model = GeoPlaneModel(90, 90, new Vec3(10, 0, 0));
        var result = StructuralAnalysis.Analyze(model, new StructuralOptions());
        var batch = result.Batches[0];

        var withoutOrigin = batch.Measurements.Where(m => !m.IsOrigin).ToList();
        var withOrigin = batch.Measurements.ToList();   // includes the synthetic origin row

        var clean = StructuralAnalysis.Recompute(batch, withoutOrigin, 0);
        var dirty = StructuralAnalysis.Recompute(batch, withOrigin, 0);

        Assert.True(clean.RmsResidual < 1e-6);
        Assert.True(dirty.RmsResidual > 0.5,
            $"expected the off-plane origin to inflate RMS, got {dirty.RmsResidual}");
    }

    [Fact]
    public void Recompute_FewerThanThreeIncluded_IsInvalid()
    {
        var model = GeoPlaneModel(20, 0, new Vec3(0, 0, 0));
        var batch = StructuralAnalysis.Analyze(model, new StructuralOptions()).Batches[0];

        var plane = StructuralAnalysis.Recompute(batch, batch.Measurements.Take(2).ToList(), 0);

        Assert.False(plane.IsValid);
    }
}
