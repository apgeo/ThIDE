// Tube synthesis + geometry-stage orchestration: cross-section framing, LRUD extremes,
// mesh topology counts, recentering math, scraps-vs-tubes selection, depth tint, and
// the real av_cerbul .lox producing a non-empty wall mesh.

using Therion.Blender;
using Therion.Blender.Geometry;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class GeometryStageTests
{
    [Fact]
    public void CrossSectionFrame_HorizontalNorthLeg_RightIsEastUpIsVertical()
    {
        var (right, up) = TubeMesher.CrossSectionFrame(new CaveVector3(0, 1, 0)); // heading north
        AssertVec(new CaveVector3(1, 0, 0), right);  // east
        AssertVec(new CaveVector3(0, 0, 1), up);     // world up
    }

    [Fact]
    public void CrossSectionFrame_VerticalLeg_DoesNotDegenerate()
    {
        var (right, up) = TubeMesher.CrossSectionFrame(new CaveVector3(0, 0, 1)); // straight up
        Assert.Equal(1.0, right.Length, 9);
        Assert.Equal(1.0, up.Length, 9);
        Assert.True(Math.Abs(right.Dot(up)) < 1e-9, "frame must stay orthogonal");
    }

    [Fact]
    public void Tube_HitsLrudExtremesExactly()
    {
        // One north leg with distinct L/R/U/D; the ring must contain the 4 wall points.
        var shot = new CaveShot
        {
            FromPosition = new CaveVector3(0, 0, 0),
            ToPosition = new CaveVector3(0, 10, 0),
            Flags = CaveShotFlags.None,
            FromLrud = new CaveLrud(2, 3, 4, 5), // L R U D
            ToLrud = new CaveLrud(2, 3, 4, 5),
        };
        var mesh = TubeMesher.Build([shot], new GeometryOptions { TubeSides = 8, CapTubes = false, DepthTint = false });

        // From-ring is centered at origin; east=+X (right), up=+Z. The 4 LRUD extremes
        // are hit within floating-point tolerance (sin/cos at π/2, π aren't bit-exact).
        AssertHasVertexNear(mesh, new CaveVector3(3, 0, 0));   // right wall = +R east
        AssertHasVertexNear(mesh, new CaveVector3(-2, 0, 0));  // left wall  = -L east
        AssertHasVertexNear(mesh, new CaveVector3(0, 0, 4));   // up wall    = +U
        AssertHasVertexNear(mesh, new CaveVector3(0, 0, -5));  // down wall  = -D
    }

    private static void AssertHasVertexNear(CaveMesh mesh, CaveVector3 target)
        => Assert.True(mesh.Vertices.Any(v => (v - target).Length < 1e-6),
            $"no mesh vertex within 1e-6 of {target}");

    [Fact]
    public void Tube_TopologyCounts_MatchSidesAndCaps()
    {
        var shot = new CaveShot
        {
            FromPosition = new CaveVector3(0, 0, 0),
            ToPosition = new CaveVector3(0, 10, 0),
            Flags = CaveShotFlags.None,
        };

        var open = TubeMesher.Build([shot], new GeometryOptions { TubeSides = 8, CapTubes = false });
        Assert.Equal(16, open.Vertices.Count);      // two 8-vertex rings
        Assert.Equal(16, open.Triangles.Count);     // 8 side quads × 2

        var capped = TubeMesher.Build([shot], new GeometryOptions { TubeSides = 8, CapTubes = true });
        Assert.Equal(16 + 2 * (1 + 8), capped.Vertices.Count);   // + centre + ring per cap
        Assert.Equal(16 + 2 * 8, capped.Triangles.Count);        // + 8 fan triangles per cap
    }

    [Fact]
    public void Tube_SkipsSplaySurfaceDuplicateAndZeroLengthLegs()
    {
        var shots = new List<CaveShot>
        {
            new() { FromPosition = new CaveVector3(0, 0, 0), ToPosition = new CaveVector3(0, 10, 0), Flags = CaveShotFlags.Splay },
            new() { FromPosition = new CaveVector3(0, 0, 0), ToPosition = new CaveVector3(0, 10, 0), Flags = CaveShotFlags.Surface },
            new() { FromPosition = new CaveVector3(0, 0, 0), ToPosition = new CaveVector3(0, 10, 0), Flags = CaveShotFlags.Duplicate },
            new() { FromPosition = new CaveVector3(5, 5, 5), ToPosition = new CaveVector3(5, 5, 5), Flags = CaveShotFlags.None }, // zero length
        };
        Assert.True(TubeMesher.Build(shots, new GeometryOptions()).IsEmpty);
    }

    [Fact]
    public void Stage_Recenters_ToBboxCenter_AndStoresOffset()
    {
        var model = new CaveModel
        {
            Stations =
            [
                new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(1000, 2000, 100) },
                new CaveStation { Id = 1, Name = "b", Position = new CaveVector3(1010, 2010, 110) },
            ],
            Shots = [new CaveShot { FromStationId = 0, ToStationId = 1,
                FromPosition = new CaveVector3(1000, 2000, 100), ToPosition = new CaveVector3(1010, 2010, 110),
                Flags = CaveShotFlags.None }],
        };

        var result = GeometryStage.Build(model, new GeometryOptions { WallSource = WallSource.Tubes });

        Assert.Equal(new CaveVector3(1005, 2005, 105), result.Offset); // bbox center
        // Recentered station positions are symmetric about the origin.
        Assert.Equal(new CaveVector3(-5, -5, -5), result.RecenteredModel.Stations[0].Position);
        Assert.Equal(new CaveVector3(5, 5, 5), result.RecenteredModel.Stations[1].Position);
        // world = local + offset holds.
        Assert.Equal(model.Stations[0].Position, result.RecenteredModel.Stations[0].Position + result.Offset);
        Assert.Equal(new CaveVector3(-5, -5, -5), result.LocalBounds.Min);
    }

    [Fact]
    public void Stage_RecenterDisabled_KeepsWorldCoordinates()
    {
        var model = new CaveModel
        {
            Stations = [new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(1000, 2000, 100) }],
        };
        var result = GeometryStage.Build(model, new GeometryOptions { Recenter = false });
        Assert.Equal(CaveVector3.Zero, result.Offset);
        Assert.Equal(new CaveVector3(1000, 2000, 100), result.RecenteredModel.Stations[0].Position);
    }

    [Fact]
    public void Stage_Auto_PrefersScrapsWhenPresent()
    {
        var model = new CaveModel
        {
            Stations = [new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(0, 0, 0) }],
            Scraps =
            [
                new CaveScrap
                {
                    Id = 0,
                    Points = [new CaveVector3(0, 0, 0), new CaveVector3(1, 0, 0), new CaveVector3(0, 1, 0)],
                    Triangles = [new CaveTriangle(0, 1, 2)],
                },
            ],
            Shots = [new CaveShot { FromPosition = new CaveVector3(0, 0, 0), ToPosition = new CaveVector3(0, 10, 0), Flags = CaveShotFlags.None }],
        };

        var result = GeometryStage.Build(model, new GeometryOptions { WallSource = WallSource.Auto, Recenter = false });
        // Scrap mesh = exactly one triangle, not a synthesized tube.
        Assert.Single(result.Walls.Triangles);
    }

    [Fact]
    public void Stage_DepthTint_ColorsEveryVertex()
    {
        var model = new CaveModel
        {
            Shots = [new CaveShot { FromPosition = new CaveVector3(0, 0, 0), ToPosition = new CaveVector3(0, 10, -20), Flags = CaveShotFlags.None }],
        };
        var result = GeometryStage.Build(model, new GeometryOptions { WallSource = WallSource.Tubes, DepthTint = true });
        Assert.True(result.Walls.HasColors);
        Assert.Equal(result.Walls.Vertices.Count, result.Walls.VertexColors!.Count);
    }

    [Fact]
    public void Stage_RealAvCerbulLox_ProducesNonEmptyWalls()
    {
        var model = LoxReader.ReadFile(TestCorpus.AvCerbulLox());
        var result = GeometryStage.Build(model);

        Assert.True(result.HasWalls, "av_cerbul .lox carries scrap walls");
        Assert.True(result.Walls.Triangles.Count > 100);
        // Recentered geometry sits near the origin (float32-safe).
        Assert.True(Math.Abs(result.LocalBounds.Min.X) < result.OriginalBounds.Diagonal);
        Assert.True(result.LocalBounds.Max.X < 1e5);
        Assert.NotEqual(CaveVector3.Zero, result.Offset); // UTM coords were shifted
    }

    [Fact]
    public void Stage_Deterministic()
    {
        var model = LoxReader.ReadFile(TestCorpus.AvCerbulLox());
        var a = GeometryStage.Build(model);
        var b = GeometryStage.Build(model);
        Assert.Equal(a.Walls.Vertices, b.Walls.Vertices);
        Assert.Equal(a.Walls.Triangles, b.Walls.Triangles);
    }

    private static void AssertVec(CaveVector3 expected, CaveVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }
}
