// Tubes skinned over generated centreline graphs: every triangle faces out of the tube (the sides away
// from the leg axis, the end caps along it), a ring reaches exactly the left, right, up and down wall
// distances on the observer's own left, right, up and down, and models with nothing in them still give
// scene metadata that is valid JSON. Seeds are fixed (Therion.TestSupport.GeneratedRuns).

using System.Text.Json;
using Therion.Blender.Geometry;
using Therion.Blender.Parsing;
using Therion.Blender.Writers;
using Therion.TestSupport;

namespace Therion.Blender.Tests;

public class TubeMesherPropertyTests
{
    private const int SeedBase = 27_000;

    private sealed record Leg(CaveShot Shot, bool Meshed);

    private static List<Leg> GenerateLegs(Random random)
    {
        var legs = new List<Leg>();
        int count = random.Next(1, 13);
        var here = new CaveVector3(random.NextDouble() * 1e5, random.NextDouble() * 1e5, random.NextDouble() * 500);
        for (int i = 0; i < count; i++)
        {
            CaveVector3 to = random.Next(8) switch
            {
                0 => here + new CaveVector3(0, 0, (random.NextDouble() - 0.5) * 30),                       // vertical
                1 => here + new CaveVector3((random.NextDouble() - 0.5) * 1e-4, 0, 0),                      // shorter than a millimetre
                2 => here + new CaveVector3((random.NextDouble() - 0.5) * 1e-3, (random.NextDouble() - 0.5) * 1e-3, 0), // around the cut-off
                3 => here + new CaveVector3((random.NextDouble() - 0.5) * 2, (random.NextDouble() - 0.5) * 2, (random.NextDouble() - 0.5) * 0.2), // short
                _ => here + new CaveVector3((random.NextDouble() - 0.5) * 40, (random.NextDouble() - 0.5) * 40, (random.NextDouble() - 0.5) * 20),
            };
            var flags = random.Next(6) switch
            {
                0 => CaveShotFlags.Splay,
                1 => CaveShotFlags.Surface,
                2 => CaveShotFlags.Duplicate,
                3 => CaveShotFlags.NotLrud | CaveShotFlags.NotVisible, // these two do not stop a leg being meshed
                _ => CaveShotFlags.None,
            };
            var shot = new CaveShot
            {
                FromPosition = here, ToPosition = to, Flags = flags,
                FromLrud = RandomLrud(random), ToLrud = RandomLrud(random),
            };
            bool meshed = (flags & (CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate)) == 0 && (to - here).Length >= 1e-3;
            legs.Add(new Leg(shot, meshed));
            here = random.Next(3) == 0 ? new CaveVector3(random.NextDouble() * 1e5, random.NextDouble() * 1e5, random.NextDouble() * 500) : to;
        }
        return legs;
    }

    // Distances to the four walls: measured, or not (a value at or below zero stands for "not measured"), or absent.
    private static CaveLrud? RandomLrud(Random random) => random.Next(6) switch
    {
        0 => null,
        1 => new CaveLrud(-1, -1, -1, -1),
        2 => new CaveLrud(0, random.NextDouble() * 3, -1, random.NextDouble()),
        _ => new CaveLrud(Math.Round(random.NextDouble() * 5, 2) + 0.05, Math.Round(random.NextDouble() * 5, 2) + 0.05,
            Math.Round(random.NextDouble() * 4, 2) + 0.05, Math.Round(random.NextDouble() * 4, 2) + 0.05),
    };

    private static double Wall(double measured, double fallback) => measured > 0 ? measured : fallback;

    [Fact]
    public void Every_triangle_of_a_generated_tube_faces_out_of_it()
    {
        GeneratedRuns.ForEachSeed(SeedBase, 400, seed =>
        {
            var random = new Random(seed);
            var legs = GenerateLegs(random);
            var options = new GeometryOptions
            {
                // From four sides up. A three-sided section is a triangle whose facets can fold over when the
                // two ends differ a lot (see A_three_sided_tube_with_the_same_walls_at_both_ends_faces_out).
                TubeSides = random.Next(4, 13), CapTubes = random.Next(4) != 0,
                DefaultTubeRadius = 0.2 + random.NextDouble() * 2, DepthTint = false,
            };

            var mesh = TubeMesher.Build(legs.Select(l => l.Shot).ToList(), options);

            int sides = options.TubeSides;
            int ringVertices = 2 * sides + (options.CapTubes ? 2 * (1 + sides) : 0);
            int legTriangles = 2 * sides + (options.CapTubes ? 2 * sides : 0);
            int meshed = legs.Count(l => l.Meshed);
            Assert.Equal(meshed * ringVertices, mesh.Vertices.Count);
            Assert.Equal(meshed * legTriangles, mesh.Triangles.Count);
            Assert.All(mesh.Vertices, v => Assert.True(double.IsFinite(v.X + v.Y + v.Z), "a vertex is not finite"));

            int block = 0;
            foreach (var leg in legs.Where(l => l.Meshed))
            {
                var axis = (leg.Shot.ToPosition - leg.Shot.FromPosition).Normalized();
                var origin = leg.Shot.FromPosition;
                for (int t = 0; t < legTriangles; t++)
                {
                    var tri = mesh.Triangles[block * legTriangles + t];
                    Assert.True(tri.A < mesh.Vertices.Count && tri.B < mesh.Vertices.Count && tri.C < mesh.Vertices.Count, $"leg {block}: index out of range");
                    var a = mesh.Vertices[(int)tri.A]; var b = mesh.Vertices[(int)tri.B]; var c = mesh.Vertices[(int)tri.C];
                    var normal = (b - a).Cross(c - a);
                    Assert.True(normal.Length > 0, $"leg {block}, triangle {t}: no area");
                    var centroid = (a + b + c) * (1.0 / 3.0);
                    var offset = centroid - origin;
                    var radial = offset - axis * offset.Dot(axis);   // the part of the centroid that is out from the axis

                    if (t < 2 * sides)
                        Assert.True(normal.Dot(radial) > 0,
                            $"leg {block}: side triangle {t} faces into the tube ({sides} sides, length {(leg.Shot.ToPosition - origin).Length:0.####} m, from {leg.Shot.FromLrud}, to {leg.Shot.ToLrud}, default radius {options.DefaultTubeRadius:0.###})");
                    else if (t < 3 * sides)
                        Assert.True(normal.Dot(axis) < 0, $"leg {block}: the start cap (triangle {t}) does not face back along the leg");
                    else
                        Assert.True(normal.Dot(axis) > 0, $"leg {block}: the end cap (triangle {t}) does not face forward along the leg");
                }
                block++;
            }
        });
    }

    [Fact]
    public void A_three_sided_tube_with_the_same_walls_at_both_ends_faces_out()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 5_000, 200, seed =>
        {
            var random = new Random(seed);
            var lrud = new CaveLrud(0.1 + random.NextDouble() * 5, 0.1 + random.NextDouble() * 5, 0.1 + random.NextDouble() * 5, 0.1 + random.NextDouble() * 5);
            var from = new CaveVector3(random.NextDouble() * 10, random.NextDouble() * 10, random.NextDouble() * 10);
            var to = from + new CaveVector3(random.NextDouble() * 30 - 15, random.NextDouble() * 30 - 15, random.NextDouble() * 30 - 15);
            var mesh = TubeMesher.Build([new CaveShot { FromPosition = from, ToPosition = to, FromLrud = lrud, ToLrud = lrud }],
                new GeometryOptions { TubeSides = 3, CapTubes = true });

            var axis = (to - from).Normalized();
            for (int t = 0; t < mesh.Triangles.Count; t++)
            {
                var tri = mesh.Triangles[t];
                var a = mesh.Vertices[(int)tri.A]; var b = mesh.Vertices[(int)tri.B]; var c = mesh.Vertices[(int)tri.C];
                var normal = (b - a).Cross(c - a);
                var offset = (a + b + c) * (1.0 / 3.0) - from;
                if (t < 6) Assert.True(normal.Dot(offset - axis * offset.Dot(axis)) > 0, $"side triangle {t} faces into the tube");
                else if (t < 9) Assert.True(normal.Dot(axis) < 0, $"start cap triangle {t} faces into the tube");
                else Assert.True(normal.Dot(axis) > 0, $"end cap triangle {t} faces into the tube");
            }
        });
    }

    [Fact]
    public void A_ring_reaches_the_wall_distances_on_the_observers_own_sides()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 10_000, 400, seed =>
        {
            var random = new Random(seed);
            var legs = GenerateLegs(random);
            // A multiple of four sides puts a vertex on each of the four wall directions.
            var options = new GeometryOptions { TubeSides = 4 * random.Next(1, 5), CapTubes = random.Next(2) == 0, DefaultTubeRadius = 0.3 + random.NextDouble() * 2 };
            int sides = options.TubeSides;

            var mesh = TubeMesher.Build(legs.Select(l => l.Shot).ToList(), options);

            int ringVertices = 2 * sides + (options.CapTubes ? 2 * (1 + sides) : 0);
            int block = 0;
            foreach (var leg in legs.Where(l => l.Meshed))
            {
                var shot = leg.Shot;
                var axis = (shot.ToPosition - shot.FromPosition).Normalized();
                bool vertical = new CaveVector3(axis.X, axis.Y, 0).Length < 1e-6;
                var rightOfTheObserver = axis.Cross(CaveVector3.UnitZ);

                for (int end = 0; end < 2; end++)
                {
                    var centre = end == 0 ? shot.FromPosition : shot.ToPosition;
                    var lrud = end == 0 ? shot.FromLrud : shot.ToLrud;
                    double left = Wall(lrud?.Left ?? 0, options.DefaultTubeRadius), right = Wall(lrud?.Right ?? 0, options.DefaultTubeRadius);
                    double up = Wall(lrud?.Up ?? 0, options.DefaultTubeRadius), down = Wall(lrud?.Down ?? 0, options.DefaultTubeRadius);
                    int ring = block * ringVertices + end * sides;

                    var toRight = mesh.Vertices[ring] - centre;
                    var toUp = mesh.Vertices[ring + sides / 4] - centre;
                    var toLeft = mesh.Vertices[ring + sides / 2] - centre;
                    var toDown = mesh.Vertices[ring + 3 * sides / 4] - centre;

                    string at = $"leg {block} end {end}";
                    Assert.True(Math.Abs(toRight.Length - right) < 1e-6, $"{at}: right wall {toRight.Length:0.####} instead of {right:0.####}");
                    Assert.True(Math.Abs(toLeft.Length - left) < 1e-6, $"{at}: left wall {toLeft.Length:0.####} instead of {left:0.####}");
                    Assert.True(Math.Abs(toUp.Length - up) < 1e-6, $"{at}: ceiling {toUp.Length:0.####} instead of {up:0.####}");
                    Assert.True(Math.Abs(toDown.Length - down) < 1e-6, $"{at}: floor {toDown.Length:0.####} instead of {down:0.####}");
                    // Every wall point is in the cross-section, square to the leg.
                    foreach (var v in new[] { toRight, toLeft, toUp, toDown })
                        Assert.True(Math.Abs(v.Dot(axis)) < 1e-6, $"{at}: a wall point is off the cross-section plane");
                    if (!vertical)
                    {
                        // Looking along the leg: the right wall is on the right, the ceiling above and the floor below.
                        Assert.True(toRight.Dot(rightOfTheObserver) > 0, $"{at}: the right wall is on the left");
                        Assert.True(toLeft.Dot(rightOfTheObserver) < 0, $"{at}: the left wall is on the right");
                        Assert.True(toUp.Z > 0, $"{at}: the ceiling is not above the leg");
                        Assert.True(toDown.Z < 0, $"{at}: the floor is not below the leg");
                        Assert.True(Math.Abs(toRight.Z) < 1e-6 && Math.Abs(toLeft.Z) < 1e-6, $"{at}: the side walls are not level");
                    }
                }
                block++;
            }
        });
    }

    [Fact]
    public void Splays_surface_legs_duplicates_and_tiny_legs_make_no_tube()
    {
        var options = new GeometryOptions();
        var origin = new CaveVector3(1, 2, 3);
        foreach (var flag in new[] { CaveShotFlags.Splay, CaveShotFlags.Surface, CaveShotFlags.Duplicate })
            Assert.True(TubeMesher.Build([new CaveShot { FromPosition = origin, ToPosition = origin + new CaveVector3(5, 0, 0), Flags = flag }], options).IsEmpty);
        Assert.True(TubeMesher.Build([new CaveShot { FromPosition = origin, ToPosition = origin + new CaveVector3(0.0009, 0, 0) }], options).IsEmpty);
        Assert.False(TubeMesher.Build([new CaveShot { FromPosition = origin, ToPosition = origin + new CaveVector3(0.0011, 0, 0) }], options).IsEmpty);
        Assert.True(TubeMesher.Build([], options).IsEmpty);
    }

    // ---- scene metadata of a model with little or nothing in it --------------------------------

    private static IEnumerable<(string Name, CaveModel Model)> SparseModels()
    {
        yield return ("empty .lox", LoxReader.Read([]));
        yield return ("empty .3d", Survex3dReader.Read(Survex3dFixtureBuilder.V8("empty").V8Style(0x00).V8Stop().Build()));
        yield return ("a .3d with one move", Survex3dReader.Read(Survex3dFixtureBuilder.V8("move").V8Style(0x00).V8Move(1, 2, 3).V8Stop().Build()));
        yield return ("a .3d with one label", Survex3dReader.Read(Survex3dFixtureBuilder.V8("label").V8Style(0x00).V8Label(0x01, 0, "a", 1, 2, 3).V8Stop().Build()));
        yield return ("a title only", new CaveModel { Title = "t", SourceFormat = CaveSourceFormat.Survex3d });
        yield return ("surveys only", new CaveModel { SourceFormat = CaveSourceFormat.Lox, Surveys = [new CaveSurvey(0, 0, "", null)] });
        yield return ("a scrap with no points", new CaveModel { SourceFormat = CaveSourceFormat.Lox, Scraps = [new CaveScrap { Id = 0, Points = [], Triangles = [] }] });
        yield return ("a leg with no stations", new CaveModel { SourceFormat = CaveSourceFormat.Lox, Shots = [new CaveShot { FromStationId = 1, ToStationId = 2 }] });
        yield return ("stations only", new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations = [new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(5, 6, 7) }],
        });
    }

    [Fact]
    public void A_model_with_little_or_nothing_in_it_gives_valid_scene_metadata()
    {
        foreach (var (name, model) in SparseModels())
        {
            string json = "";
            var failure = Record.Exception(() =>
            {
                foreach (var wallSource in Enum.GetValues<WallSource>())
                {
                    var geometry = GeometryStage.Build(model, new GeometryOptions { WallSource = wallSource });
                    json = SceneMetaWriter.Write(SceneMeta.Build(geometry));
                    using var document = JsonDocument.Parse(json);
                    Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
                    var back = SceneMetaWriter.Read(json);
                    Assert.True(double.IsFinite(back.WorldBounds.Min.X + back.WorldBounds.Max.X + back.WorldBounds.Center.X + back.WorldBounds.Size.X),
                        $"{wallSource}: the world bounds are not finite");
                    Assert.True(double.IsFinite(back.LocalBounds.Min.X + back.LocalBounds.Max.X), $"{wallSource}: the local bounds are not finite");
                }
            });
            Assert.True(failure is null, $"{name}: {failure}");
        }
    }
}
