// Numeric known-answer tests for the geometry-core primitives: vector math, bounding
// box, centerline component graph, arc-length path (+ Chaikin smoothing), k-d tree
// nearest-neighbour, and the depth ramp.

using Therion.Blender;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class GeometryCoreTests
{
    [Fact]
    public void Vector_CrossDotLengthNormalize()
    {
        var x = new CaveVector3(1, 0, 0);
        var y = new CaveVector3(0, 1, 0);
        Assert.Equal(new CaveVector3(0, 0, 1), x.Cross(y));
        Assert.Equal(0.0, x.Dot(y));
        Assert.Equal(5.0, new CaveVector3(3, 4, 0).Length, 12);
        Assert.Equal(new CaveVector3(1, 0, 0), new CaveVector3(9, 0, 0).Normalized());
        Assert.Equal(CaveVector3.Zero, CaveVector3.Zero.Normalized());
        Assert.Equal(new CaveVector3(1, 1, 0), new CaveVector3(0, 0, 0).Lerp(new CaveVector3(2, 2, 0), 0.5));
    }

    [Fact]
    public void BoundingBox_FromPointsCenterSizeDiagonal()
    {
        var box = BoundingBox.FromPoints([new CaveVector3(-1, -2, -3), new CaveVector3(3, 2, 1)]);
        Assert.False(box.IsEmpty);
        Assert.Equal(new CaveVector3(1, 0, -1), box.Center);
        Assert.Equal(new CaveVector3(4, 4, 4), box.Size);
        Assert.Equal(Math.Sqrt(48), box.Diagonal, 12);
    }

    [Fact]
    public void BoundingBox_Empty_IsIdentityForUnion()
    {
        Assert.True(BoundingBox.Empty.IsEmpty);
        Assert.True(BoundingBox.FromPoints([]).IsEmpty);
        var box = BoundingBox.FromPoints([new CaveVector3(1, 1, 1)]);
        Assert.Equal(box, BoundingBox.Empty.Union(box));
        Assert.Equal(box, box.Union(BoundingBox.Empty));
    }

    [Fact]
    public void CenterlineGraph_SplitsComponents_AndExcludesSplays()
    {
        // Two separate two-station legs + a splay hanging off the first.
        var model = new CaveModel
        {
            Stations =
            [
                new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 1, Name = "b", Position = new CaveVector3(10, 0, 0) },
                new CaveStation { Id = 2, Name = "c", Position = new CaveVector3(0, 50, 0) },
                new CaveStation { Id = 3, Name = "d", Position = new CaveVector3(10, 50, 0) },
                new CaveStation { Id = 4, Name = "s", Position = new CaveVector3(3, 3, 0) },
            ],
            Shots =
            [
                Leg(0, 0, 0, 10, 0, 0),                        // a-b
                Leg(0, 50, 0, 10, 50, 0),                      // c-d
                Splay(0, 0, 0, 3, 3, 0),                       // a-s (excluded)
            ],
        };

        var graph = CenterlineGraph.Build(model);

        Assert.Equal(2, graph.Components.Count);
        Assert.All(graph.Components, c => Assert.Equal(2, c.StationCount)); // splay station not included
        Assert.Equal(new CaveVector3(5, 0, 0), graph.Components[0].Centroid);
        Assert.Equal(new CaveVector3(5, 50, 0), graph.Components[1].Centroid);
    }

    [Fact]
    public void CenterlinePath_ArcLengthAndConstantSpeedResample()
    {
        var path = new CenterlinePath([new CaveVector3(0, 0, 0), new CaveVector3(3, 0, 0), new CaveVector3(3, 4, 0)]);
        Assert.Equal(7.0, path.Length, 12); // 3 + 4

        Assert.Equal(new CaveVector3(3, 0, 0), path.SampleAtDistance(3));
        Assert.Equal(new CaveVector3(3, 2, 0), path.SampleAtDistance(5)); // 2 up the second leg

        var samples = path.ResampleByArcLength(8);
        Assert.Equal(8, samples.Count);
        Assert.Equal(new CaveVector3(0, 0, 0), samples[0]);
        Assert.Equal(new CaveVector3(3, 4, 0), samples[^1]);
        // Even spacing: consecutive gaps all equal to Length/7.
        for (int i = 1; i < samples.Count; i++)
            Assert.Equal(path.Length / 7, (samples[i] - samples[i - 1]).Length, 9);
    }

    [Fact]
    public void CenterlinePath_ChaikinSmoothing_KeepsEndpointsAndGrows()
    {
        var path = new CenterlinePath([new CaveVector3(0, 0, 0), new CaveVector3(1, 1, 0), new CaveVector3(2, 0, 0)]);
        var smoothed = path.Smooth(1);
        Assert.Equal(path.Points[0], smoothed.Points[0]);
        Assert.Equal(path.Points[^1], smoothed.Points[^1]);
        Assert.True(smoothed.Points.Count > path.Points.Count);
        // The sharp middle corner is cut: no interior point sits at the original apex.
        Assert.DoesNotContain(new CaveVector3(1, 1, 0), smoothed.Points);
    }

    [Fact]
    public void KdTree_FindsNearest_MatchingBruteForce()
    {
        var random = new Random(7);
        var points = new List<CaveVector3>();
        for (int i = 0; i < 500; i++)
            points.Add(new CaveVector3(random.NextDouble() * 100, random.NextDouble() * 100, random.NextDouble() * 100));
        var tree = new KdTree(points);

        for (int q = 0; q < 50; q++)
        {
            var query = new CaveVector3(random.NextDouble() * 100, random.NextDouble() * 100, random.NextDouble() * 100);
            double brute = points.Min(p => (p - query).LengthSquared);
            Assert.Equal(brute, tree.NearestDistanceSquared(query), 9);
        }
    }

    [Fact]
    public void KdTree_Empty_ReturnsInfinity()
    {
        Assert.Equal(double.PositiveInfinity, new KdTree([]).NearestDistanceSquared(CaveVector3.Zero));
    }

    [Fact]
    public void DepthRamp_TopWarm_BottomCool_Clamps()
    {
        var top = DepthRamp.Sample(1.0);
        var bottom = DepthRamp.Sample(0.0);
        Assert.True(top.R > top.B, "top of cave should read warm (red over blue)");
        Assert.True(bottom.B > bottom.R, "bottom should read cool (blue over red)");
        Assert.Equal(top, DepthRamp.Sample(2.0));   // clamps high
        Assert.Equal(bottom, DepthRamp.Sample(-1)); // clamps low
        Assert.Equal(DepthRamp.Sample(0.5), DepthRamp.SampleZ(5, 0, 10)); // midpoint by Z
    }

    private static CaveShot Leg(double x0, double y0, double z0, double x1, double y1, double z1) => new()
    {
        FromPosition = new CaveVector3(x0, y0, z0),
        ToPosition = new CaveVector3(x1, y1, z1),
        Flags = CaveShotFlags.None,
    };

    private static CaveShot Splay(double x0, double y0, double z0, double x1, double y1, double z1) => new()
    {
        FromPosition = new CaveVector3(x0, y0, z0),
        ToPosition = new CaveVector3(x1, y1, z1),
        Flags = CaveShotFlags.Splay,
    };
}
