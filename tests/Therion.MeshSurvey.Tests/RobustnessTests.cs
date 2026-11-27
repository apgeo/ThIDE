using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Therion.Cave3D;
using Therion.CaveGeometry;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.MeshSurvey.Tests;

/// <summary>The inputs real scans give: point clouds, holes, soup, noise, clutter; ThIDE's own tubes;
/// and the guarantees the library makes about itself.</summary>
public class RobustnessTests
{
    private static MeshSurveyOptions Options => PlanTests.Base;

    /// <summary>A point cloud sampled on the surface of an invented cave (one point per ~5 cm).</summary>
    private static List<CaveVector3> Cloud(SyntheticCave cave, double spacing = 0.05, int seed = 1)
    {
        var mesh = cave.Mesh(0.2);
        var random = new Random(seed);
        var points = new List<CaveVector3>();
        foreach (var t in mesh.Triangles)
        {
            var a = mesh.Vertices[(int)t.A]; var b = mesh.Vertices[(int)t.B]; var c = mesh.Vertices[(int)t.C];
            double area = 0.5 * (b - a).Cross(c - a).Length;
            int n = (int)Math.Ceiling(area / (spacing * spacing));
            for (int k = 0; k < n; k++)
            {
                double r1 = Math.Sqrt(random.NextDouble()), r2 = random.NextDouble();
                points.Add(a * (1 - r1) + b * (r1 * (1 - r2)) + c * (r1 * r2));
            }
        }
        return points;
    }

    [Fact]
    public void A_point_cloud_becomes_a_survey_too()
    {
        var cave = SyntheticCave.YJunction();
        var session = MeshSurveySession.Open(new RawModel(null, Cloud(cave), false, true, "cloud"), Options.Input);
        var o = Options with { Void = Options.Void with { HoleClosing = 0.2 } };
        var prepared = session.Prepare(o);
        Assert.Equal(1, prepared.VoidReport.Components);
        Assert.Equal(3, prepared.Skeleton.Report.Ends);
        var s = session.Plan(prepared, o);
        Assert.True(s.Legs.Count > 4);
        Assert.Equal(0, s.Report.Plan.BlockedLegs);
        // A leg from one branch of the Y to the other, through the rock between them.
        var blocked = session.CheckLeg(new CaveVector3(22, 2.57, 0), new CaveVector3(22, -5.45, -1.09));
        Assert.Equal(LegCheckStatus.Blocked, blocked.Status);
    }

    [Fact]
    public void A_mesh_with_holes_and_an_open_entrance_still_works()
    {
        var mesh = SyntheticCave.Straight(30, 1.5).Mesh(0.2);
        var random = new Random(4);
        // The entrance: the cap at x < 0 removed. Plus random scan holes (2 % of triangles).
        var kept = mesh.Triangles.Where(t =>
        {
            var c = (mesh.Vertices[(int)t.A] + mesh.Vertices[(int)t.B] + mesh.Vertices[(int)t.C]) / 3.0;
            return c.X > -0.2 && random.NextDouble() > 0.02;
        }).ToList();
        var session = MeshSurveySession.Open(new RawModel(mesh with { Triangles = kept }, null, false, true, "holes"), Options.Input);
        var inspect = session.Inspect(Options);
        Assert.True(inspect.Holes > 10);
        var prepared = session.Prepare(Options);
        Assert.Equal(1, prepared.VoidReport.Components);
        var s = session.Plan(prepared, Options);
        // The start is the entrance (the largest hole, at x ≈ 0).
        Assert.InRange(s.Stations[s.StartStation].Position.X, -1.0, 2.0);
        Assert.InRange(s.Length, 24, 34);
        Assert.Equal(0, s.Report.Plan.BlockedLegs);
    }

    [Fact]
    public void Clutter_in_the_passage_does_not_make_loops()
    {
        // Two "cavers" (0.35 m boxes) standing in the passage.
        var cave = SyntheticCave.Straight(30, 1.8)
            .Carve(new Box(new(8, 0.3, -0.9), new(0.17, 0.17, 0.9)))
            .Carve(new Box(new(19, -0.4, -0.9), new(0.17, 0.17, 0.9)));
        var session = MeshSurveySession.Open(new RawModel(cave.Mesh(0.1), null, false, true, "clutter"), Options.Input);
        var o = Options with { Void = Options.Void with { VoxelSize = 0.1 } };
        var prepared = session.Prepare(o);
        Assert.Equal(0, prepared.VoidReport.Loops);
        Assert.Equal(0, prepared.Skeleton.Report.Loops);
    }

    [Fact]
    public void Noisy_vertices_and_triangle_soup_give_the_same_topology()
    {
        var mesh = SyntheticCave.Loop().Mesh(0.25);
        var random = new Random(9);
        var soup = new List<CaveVector3>();
        var tris = new List<CaveTriangle>();
        var jittered = mesh.Vertices.Select(v => v + new CaveVector3(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * 0.04).ToList();
        foreach (var t in mesh.Triangles)
        {
            uint i = (uint)soup.Count;
            soup.Add(jittered[(int)t.A]); soup.Add(jittered[(int)t.B]); soup.Add(jittered[(int)t.C]);
            tris.Add(new CaveTriangle(i, i + 1, i + 2));
        }
        var session = MeshSurveySession.Open(new RawModel(new CaveMesh { Vertices = soup, Triangles = tris }, null, false, true, "soup"), Options.Input);
        var prepared = session.Prepare(Options);
        Assert.Equal(1, prepared.VoidReport.Loops);
        Assert.Equal(1, prepared.Skeleton.Report.Loops);
    }

    [Fact]
    public void ThIDE_s_own_tubes_round_trip_to_a_centreline_near_the_original()
    {
        // An invented centreline: a Y with a loop, skinned by the tube mesher.
        var stations = new[]
        {
            new CaveVector3(0, 0, 0), new(8, 0, -1), new(16, 2, -1), new(24, 2, -2), // 0..3 main
            new(16, 10, -1), new(8, 10, -1),                                         // 4..5 loop back
            new(24, -8, -3),                                                         // 6 branch
        };
        var legs = new (int, int)[] { (0, 1), (1, 2), (2, 3), (2, 4), (4, 5), (5, 1), (2, 6) };
        var lrud = new CaveLrud(1.2, 1.2, 1.0, 1.0);
        var shots = legs.Select(l => new CaveShot { FromPosition = stations[l.Item1], ToPosition = stations[l.Item2], FromLrud = lrud, ToLrud = lrud }).ToList();
        var mesh = TubeMesher.Build(shots, new GeometryOptions { TubeSides = 12, JoinTubes = true, CapTubes = true });
        var session = MeshSurveySession.Open(new RawModel(mesh, null, false, true, "tubes"), Options.Input);
        var prepared = session.Prepare(Options);
        Assert.Equal(1, prepared.VoidReport.Loops);
        // Every gallery, however well the junction already sees it (coverage alone would leave the short
        // dead ends out: their walls are visible from the junction).
        var s = session.Plan(prepared, Options with { Laterals = new LateralOptions { SurveyAllLongerThan = 3 } });
        Assert.Equal(1, s.Legs.Count - s.Stations.Count + 1);
        // Every original station lies near a derived leg.
        foreach (var st in stations)
        {
            double best = s.Legs.Min(l => DistanceToSegment(st, s.Stations[l.From].Position, s.Stations[l.To].Position));
            Assert.True(best < 1.5, $"station {st} is {best:0.00} m from the derived centreline");
        }
        double original = legs.Sum(l => (stations[l.Item2] - stations[l.Item1]).Length);
        Assert.InRange(s.Length, original * 0.85, original * 1.15);
    }

    private static double DistanceToSegment(CaveVector3 p, CaveVector3 a, CaveVector3 b)
    {
        var ab = b - a;
        double t = ab.LengthSquared > 0 ? Math.Clamp((p - a).Dot(ab) / ab.LengthSquared, 0, 1) : 0;
        return (p - (a + ab * t)).Length;
    }

    [Fact]
    public void The_result_does_not_depend_on_the_number_of_threads()
    {
        string Run()
        {
            var cave = SyntheticCave.YJunction();
            var session = MeshSurveySession.Open(new RawModel(cave.Mesh(0.25), null, false, true, "y"), Options.Input);
            var o = Options with { Splays = new SplayOptions { Mode = SplayMode.Adaptive, Count = 20 }, Readings = new ReadingOptions { Noise = new NoiseOptions() } };
            var prepared = session.Prepare(o);
            return TherionCentrelineWriter.Write(session.Plan(prepared, o), o) + MeshSurveyJson.Serialize(prepared.Skeleton.Report);
        }
        string parallel = Run();
        string serial;
        int workers, ports;
        ThreadPool.GetMaxThreads(out workers, out ports);
        int min, minPorts;
        ThreadPool.GetMinThreads(out min, out minPorts);
        try
        {
            ThreadPool.SetMinThreads(1, minPorts);
            ThreadPool.SetMaxThreads(Math.Max(1, Environment.ProcessorCount > 1 ? 2 : 1), ports);
            serial = Run();
        }
        finally
        {
            ThreadPool.SetMaxThreads(workers, ports);
            ThreadPool.SetMinThreads(min, minPorts);
        }
        Assert.Equal(parallel, serial);
    }

    [Fact]
    public void An_empty_model_or_one_with_no_inside_says_so()
    {
        var empty = MeshSurveySession.Open(new RawModel(CaveMesh.Empty, null, false, true, "empty"), Options.Input);
        Assert.Throws<InvalidOperationException>(() => empty.Prepare(Options));
        // A single flat square: a surface, but nothing enclosed.
        var sheet = new CaveMesh
        {
            Vertices = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)],
            Triangles = [new CaveTriangle(0, 1, 2), new CaveTriangle(0, 2, 3)],
        };
        var flat = MeshSurveySession.Open(new RawModel(sheet, null, false, true, "sheet"), Options.Input);
        var prepared = flat.Prepare(Options);
        var ex = Assert.Throws<InvalidOperationException>(() => flat.Plan(prepared, Options));
        Assert.Contains("No enclosed space", ex.Message);
    }

    [Fact]
    public void The_library_needs_only_the_model_and_geometry_libraries_and_starts_no_process()
    {
        var assembly = typeof(MeshSurveySession).Assembly;
        var therion = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Therion.", StringComparison.Ordinal)).Order().ToArray();
        Assert.Equal(["Therion.Cave3D", "Therion.CaveGeometry"], therion);
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeReferences.Select(h => reader.GetTypeReference(h)).Select(t => reader.GetString(t.Namespace) + "." + reader.GetString(t.Name)).ToHashSet();
        Assert.DoesNotContain("System.Diagnostics.Process", types);
        Assert.DoesNotContain("System.Diagnostics.ProcessStartInfo", types);
    }
}
