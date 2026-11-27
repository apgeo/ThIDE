using Therion.Cave3D;
using Therion.CaveGeometry;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Spatial;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.MeshSurvey.Tests;

public class SessionAndLegCheckTests
{
    private static MeshSurveySession Open(SyntheticCave cave, double spacing = 0.2, InputOptions? input = null) =>
        MeshSurveySession.Open(new RawModel(cave.Mesh(spacing), null, false, false, "test"), input);

    [Fact]
    public void Surface_nets_mesh_is_closed_and_wound_outwards()
    {
        var mesh = SyntheticCave.Straight().Mesh(0.25);
        Assert.Equal(0, MeshTopology.CountBoundaryEdges(mesh.Triangles));
        var fwn = new FastWindingNumber(new TriangleBvh(mesh));
        Assert.InRange(fwn.At(new CaveVector3(10, 0, 0)), 0.97, 1.03);
        Assert.InRange(fwn.At(new CaveVector3(10, 3, 0)), -0.02, 0.02);
    }

    [Fact]
    public void Winding_number_matches_the_shape_away_from_the_wall()
    {
        var cave = SyntheticCave.Loop();
        var fwn = new FastWindingNumber(new TriangleBvh(cave.Mesh(0.25)));
        var random = new Random(3);
        var b = cave.Bounds;
        int checkedPoints = 0;
        for (int i = 0; i < 400; i++)
        {
            var p = new CaveVector3(b.Min.X + random.NextDouble() * b.Size.X, b.Min.Y + random.NextDouble() * b.Size.Y, b.Min.Z + random.NextDouble() * b.Size.Z);
            double d = cave.Distance(p);
            if (Math.Abs(d) < 0.3) continue;
            Assert.Equal(d < 0, Math.Abs(fwn.At(p)) > 0.5);
            checkedPoints++;
        }
        Assert.True(checkedPoints > 200);
    }

    [Fact]
    public void Winding_number_survives_a_hole_in_the_mesh()
    {
        var mesh = SyntheticCave.Straight().Mesh(0.25);
        // Punch a hole: drop the triangles near the middle of the roof.
        var kept = mesh.Triangles.Where(t =>
        {
            var c = (mesh.Vertices[(int)t.A] + mesh.Vertices[(int)t.B] + mesh.Vertices[(int)t.C]) / 3.0;
            return !(Math.Abs(c.X - 10) < 0.6 && c.Z > 1.0);
        }).ToList();
        Assert.True(kept.Count < mesh.Triangles.Count);
        var holed = mesh with { Triangles = kept };
        var fwn = new FastWindingNumber(new TriangleBvh(holed));
        Assert.True(Math.Abs(fwn.At(new CaveVector3(10, 0, 0))) > 0.5);
        Assert.True(Math.Abs(fwn.At(new CaveVector3(5, 0, 0))) > 0.9);
        Assert.True(Math.Abs(fwn.At(new CaveVector3(10, 0, 3))) < 0.5);
    }

    [Fact]
    public void Raycast_and_nearest_agree_with_the_shape()
    {
        var mesh = SyntheticCave.Straight(20, 1.5).Mesh(0.1);
        var bvh = new TriangleBvh(mesh);
        Assert.True(bvh.Raycast(new CaveVector3(10, 0, 0), new CaveVector3(0, 0, 1), 10, out var up));
        Assert.InRange(up.Distance, 1.45, 1.55);
        Assert.True(bvh.Raycast(new CaveVector3(10, 0, 0), new CaveVector3(0, 1, 1), 10, out var diagonal));
        Assert.InRange(diagonal.Distance, 1.45, 1.55);
        Assert.False(bvh.Raycast(new CaveVector3(10, 0, 0), new CaveVector3(0, 0, 1), 1.0, out _));
        double d = bvh.Nearest(new CaveVector3(10, 0.5, 0), 5, out var closest, out int tri);
        Assert.InRange(d, 0.95, 1.05);
        Assert.True(tri >= 0);
        Assert.InRange(closest.Y, 1.45, 1.55);
        // Outward normals: the hit's normal points away from the axis.
        Assert.True(up.Normal.Z > 0.9);
    }

    [Fact]
    public void Normalisation_turns_a_y_up_millimetre_model_into_metres_z_up()
    {
        var mesh = SyntheticCave.Straight(20, 1.5).Mesh(0.25);
        // As a y-up millimetre file: (x, y, z) metres z-up → (1000x, 1000z, -1000y).
        var file = mesh with { Vertices = mesh.Vertices.Select(v => new CaveVector3(v.X * 1000, v.Z * 1000, -v.Y * 1000)).ToList() };
        var session = MeshSurveySession.Open(new RawModel(file, null, FormatIsYUp: true, UnitsKnown: false, "mm"));
        Assert.Equal(ModelLengthUnit.Millimetres, session.Model.Report.Units);
        Assert.True(session.Model.Report.UnitsGuessed);
        Assert.Equal(ModelUpAxisChoice.Y, session.Model.Report.UpAxis);
        var b = BoundingBox.FromPoints(session.Model.Mesh!.Vertices.Select(session.Model.ToModel));
        Assert.InRange(b.Size.X, 22.5, 23.5);
        Assert.InRange(b.Size.Z, 2.8, 3.2);
        Assert.InRange(b.Min.X, -1.6, -1.4);
        // Source ↔ model round trip.
        var p = new CaveVector3(1234, 567, -89);
        var back = session.Model.ModelToSource(session.Model.SourceToModel(p));
        Assert.True((back - p).Length < 1e-6);
    }

    [Fact]
    public void North_rotation_turns_the_model_y_axis_to_its_azimuth()
    {
        var session = Open(SyntheticCave.Straight(), 0.3, new InputOptions { NorthRotation = 90, Units = ModelLengthUnit.Metres });
        // Model +Y points east → a point at +Y in the file ends up at +X (east).
        var p = session.Model.SourceToModel(new CaveVector3(0, 1, 0));
        Assert.InRange(p.X, 0.999, 1.001);
        Assert.InRange(Math.Abs(p.Y), 0, 1e-9);
    }

    [Fact]
    public void Welding_merges_a_triangle_soup()
    {
        var mesh = SyntheticCave.Straight().Mesh(0.3);
        var soup = new List<CaveVector3>();
        var tris = new List<CaveTriangle>();
        foreach (var t in mesh.Triangles)
        {
            uint i = (uint)soup.Count;
            soup.Add(mesh.Vertices[(int)t.A]); soup.Add(mesh.Vertices[(int)t.B]); soup.Add(mesh.Vertices[(int)t.C]);
            tris.Add(new CaveTriangle(i, i + 1, i + 2));
        }
        var session = MeshSurveySession.Open(new RawModel(new CaveMesh { Vertices = soup, Triangles = tris }, null, false, true, "soup"));
        Assert.Equal(mesh.Vertices.Count, session.Model.Mesh!.Vertices.Count);
        Assert.Equal(0, session.Model.Report.BoundaryEdges);
    }

    [Fact]
    public void Orientation_is_repaired_when_half_the_triangles_are_flipped()
    {
        var mesh = SyntheticCave.Straight().Mesh(0.3);
        var flipped = mesh.Triangles.Select((t, i) => i % 2 == 0 ? new CaveTriangle(t.A, t.C, t.B) : t).ToList();
        var session = MeshSurveySession.Open(new RawModel(mesh with { Triangles = flipped }, null, false, true, "flip"));
        Assert.True(session.Model.Report.TrianglesFlipped > 0);
        Assert.True(session.IsInVoid(new CaveVector3(10, 0, 0)));
        Assert.False(session.IsInVoid(new CaveVector3(10, 0, 2.5)));
    }

    [Fact]
    public void A_clear_leg_along_the_passage_is_clear()
    {
        var session = Open(SyntheticCave.Straight(20, 1.5), 0.2, new InputOptions { Units = ModelLengthUnit.Metres });
        var check = session.CheckLeg(new CaveVector3(2, 0, 0), new CaveVector3(12, 0, 0), new LegCheckOptions { Clearance = 0.3, MaxLength = 15 });
        Assert.Equal(LegCheckStatus.Clear, check.Status);
        Assert.InRange(check.MinClearance, 1.3, 1.6);
        Assert.Equal("clear", check.Describe());
    }

    [Fact]
    public void A_leg_across_the_bend_is_blocked_with_the_hit_and_the_rock()
    {
        var session = Open(SyntheticCave.Bend(), 0.2, new InputOptions { Units = ModelLengthUnit.Metres });
        // From the first passage to the second, cutting the corner through the rock.
        var check = session.CheckLeg(new CaveVector3(6, 0, 0), new CaveVector3(20, 7, -0.8));
        Assert.Equal(LegCheckStatus.Blocked, check.Status);
        Assert.NotNull(check.FirstHit);
        Assert.True(check.Crossings >= 2);
        Assert.True(check.RockThickness > 0.5);
        Assert.Contains("blocked", check.Describe());
    }

    [Fact]
    public void Tight_out_of_range_and_endpoint_checks()
    {
        var session = Open(SyntheticCave.Straight(20, 1.5), 0.2, new InputOptions { Units = ModelLengthUnit.Metres });
        var tight = session.CheckLeg(new CaveVector3(2, 1.3, 0), new CaveVector3(10, 1.3, 0), new LegCheckOptions { Clearance = 0.3 });
        Assert.Equal(LegCheckStatus.Tight, tight.Status);
        var tooLong = session.CheckLeg(new CaveVector3(1, 0, 0), new CaveVector3(19, 0, 0), new LegCheckOptions { MaxLength = 10 });
        Assert.Equal(LegCheckStatus.OutOfRange, tooLong.Status);
        Assert.True(tooLong.TooLong);
        var inRock = session.CheckLeg(new CaveVector3(2, 0, 0), new CaveVector3(10, 0, 1.8));
        Assert.Equal(LegCheckStatus.EndpointInRock, inRock.Status);
        Assert.False(inRock.ToInVoid);
        var outside = session.CheckLeg(new CaveVector3(2, 0, 0), new CaveVector3(10, 0, 40));
        Assert.Equal(LegCheckStatus.EndpointOutside, outside.Status);
    }
}
