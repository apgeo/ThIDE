// The flythrough's route, checked without Blender on the invented caves and on small hand-made
// passages: the camera keeps to the middle of the passage it flies through, starts and ends inside
// it, never turns its view faster than the limit, never looks nearly straight up or down, and
// speeds up and slows down at the ends.
//
// "Room" is measured here from the planes of each leg's own tube walls, not with the
// cross-section code the planner uses, so the two check each other. The distance to the skin the
// tubes are joined into, which is what a camera in Blender has round it, is measured from its
// triangles (KeepsClearOfTheSkinTheTubesAreJoinedInto).

using Therion.Blender.Emit;
using Therion.Blender.Presets;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class FlythroughRouteTests
{
    private static readonly FlythroughParams PresetParams =
        BuiltInPresets.All.Single(p => p.Name == "Full flythrough").Spec.Camera.Flythrough!;

    private static SceneSpec Spec(int fps, double seconds, FlythroughParams? flythrough = null)
        => SceneSpecTests.ValidSpec() with
        {
            Camera = new CameraSpec { Template = CameraTemplate.Flythrough, Flythrough = flythrough ?? PresetParams },
            Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
        };

    private static IReadOnlyList<CameraKeyframe> Fly(CameraFraming framing, int fps, double seconds, FlythroughParams? flythrough = null)
        => CameraPlanner.Plan(Spec(fps, seconds, flythrough), framing).Keyframes;

    public static TheoryData<InventedCaveSize, int> Caves() => new()
    {
        { InventedCaveSize.Small, 1 }, { InventedCaveSize.Small, 2 },
        { InventedCaveSize.Medium, 1 }, { InventedCaveSize.Medium, 2 }, { InventedCaveSize.Medium, 3 },
        { InventedCaveSize.Large, 1 },
    };

    // ---- where it flies ----

    [Theory]
    [MemberData(nameof(Caves))]
    public void KeepsClearOfTheWallsOfItsPassage(InventedCaveSize size, int seed)
    {
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed));
        var framing = CameraFraming.FromGeometry(geometry);
        var passage = new Passage(framing.CenterlinePath!, geometry.RecenteredModel);

        // The probe's clip (80 frames at 10 fps), the preset's own, and the same flown backwards.
        var clips = new[]
        {
            Fly(framing, 10, 8), Fly(framing, 30, 20), Fly(framing, 30, 20, PresetParams with { Reverse = true }),
        };
        // 0.3 m is what the scene probe asks of the camera in a real Blender scene. The narrowest
        // invented passage has about 0.85 m of room at its middle, and Room() measures low.
        foreach (var keys in clips)
        {
            var tight = keys.Select(k => (k.Frame, Room: passage.Room(k.Location))).Where(k => k.Room < 0.3).ToList();
            Assert.True(tight.Count == 0,
                "less than 0.3 m from a wall at " + string.Join(", ", tight.Take(8).Select(k => $"frame {k.Frame}: {k.Room:F2} m")));
        }
    }

    // The probe's clip again, against the wall mesh itself: nothing of it within 0.3 m of the
    // camera, which is what the scene probe asks in a real Blender scene. Before the tubes were
    // joined the camera passed through two end caps at every station (8 of 80 frames on the
    // Medium cave). The caves here are the ones whose passages stay out of each other's way: in
    // Medium seed 2 and in the Large cave a passage runs through another one beyond the station
    // where they join, or away from any, and a camera in the one passes through the wall of
    // the other.
    [Theory]
    [InlineData(InventedCaveSize.Small, 1)]
    [InlineData(InventedCaveSize.Small, 2)]
    [InlineData(InventedCaveSize.Small, 3)]
    [InlineData(InventedCaveSize.Medium, 1)]
    [InlineData(InventedCaveSize.Medium, 3)]
    [InlineData(InventedCaveSize.Medium, 4)]
    public void KeepsClearOfTheSkinTheTubesAreJoinedInto(InventedCaveSize size, int seed)
    {
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed));
        var keys = Fly(CameraFraming.FromGeometry(geometry), 10, 8);

        var close = keys.Select(k => (k.Frame, Distance: MeshProbe.Nearest(geometry.Walls, k.Location))).Where(k => k.Distance < 0.3).ToList();
        Assert.True(close.Count == 0,
            "within 0.3 m of a wall surface at " + string.Join(", ", close.Select(k => $"frame {k.Frame}: {k.Distance:F2} m")));
    }

    [Fact]
    public void WithEveryLegATubeOfItsOwn_TheCameraPassesThroughTheirEndCaps()
    {
        // What joining the tubes is for: the same route through the same cave, each leg capped.
        var geometry = GeometryStage.Build(InventedCave.Build(InventedCaveSize.Medium), new GeometryOptions { JoinTubes = false });
        var keys = Fly(CameraFraming.FromGeometry(geometry), 10, 8);

        int close = keys.Count(k => MeshProbe.Nearest(geometry.Walls, k.Location) < 0.3);
        Assert.True(close >= 5, $"within 0.3 m of a wall surface at {close} of {keys.Count} frames");
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void StartsAndEndsInsideThePassage_LookingAlongIt(InventedCaveSize size, int seed)
    {
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed));
        var framing = CameraFraming.FromGeometry(geometry);
        var route = framing.CenterlinePath!;
        var keys = Fly(framing, 10, 8);

        // Not on the end cap of the first or the last tube, and not past it.
        var first = new Passage([route[0], route[1]], geometry.RecenteredModel);
        var last = new Passage([route[^2], route[^1]], geometry.RecenteredModel);
        Assert.True(first.Room(keys[0].Location, beyondEnds: -0.5) >= 0.3, "the first frame is not well inside the first leg");
        Assert.True(last.Room(keys[^1].Location, beyondEnds: -0.5) >= 0.3, "the last frame is not well inside the last leg");

        // At the end the view still runs along the last leg rather than out through a wall.
        var look = (keys[^1].Target - keys[^1].Location).Normalized();
        var along = (route[^1] - route[^2]).Normalized();
        Assert.True(look.Dot(along) > Math.Cos(35.0 * Math.PI / 180.0),
            $"the last frame looks {Degrees(look, along):F0}° off the last leg");
    }

    [Fact]
    public void ANarrowPassage_IsFlownDownItsMiddle_AWideOneGivesRoomToCutCorners()
    {
        // The same zig-zag twice: 0.8 m across, and 10 m across.
        IReadOnlyList<CaveVector3> stations = [new(0, 0, 0), new(8, 0, 0), new(14, 6, 0), new(22, 6, 0), new(28, 0, 0), new(36, 0, 0)];
        double Wander(double wall)
        {
            var geometry = GeometryStage.Build(Chain(stations, new CaveLrud(wall, wall, wall, wall)), new GeometryOptions { Recenter = false });
            var framing = CameraFraming.FromGeometry(geometry);
            var passage = new Passage(framing.CenterlinePath!, geometry.RecenteredModel);
            var keys = Fly(framing, 30, 10);

            // 60 % of the room an eight-sided tube of that size has at its middle.
            double room = wall * Math.Cos(Math.PI / 8.0);
            Assert.All(keys, k => Assert.True(passage.Room(k.Location) >= Math.Min(1.5, 0.6 * room) - 0.02,
                $"frame {k.Frame}: {passage.Room(k.Location):F2} m of room in a passage with {room:F2} m at its middle"));
            return keys.Max(k => DistanceToPolyline(k.Location, stations));
        }

        double narrow = Wander(0.4), wide = Wander(5.0);
        Assert.True(narrow < 0.16, $"the route leaves the middle of the narrow passage by {narrow:F2} m");
        Assert.True(wide > 0.5, $"the route cuts the wide passage's corners by only {wide:F2} m");
    }

    [Fact]
    public void TheMiddle_IsTheMiddleOfTheCrossSection_NotTheStation()
    {
        // A straight passage heading north whose stations stand against its left wall and on its floor.
        var lrud = new CaveLrud(0.2, 3.8, 2.6, 0.4);
        IReadOnlyList<CaveVector3> stations = [new(0, 0, 0), new(0, 10, 0), new(0, 20, 0), new(0, 30, 0)];
        var geometry = GeometryStage.Build(Chain(stations, lrud), new GeometryOptions { Recenter = false });
        var keys = Fly(CameraFraming.FromGeometry(geometry), 10, 4);

        Assert.All(keys, k =>
        {
            Assert.Equal(1.8, k.Location.X, 6); // halfway between a wall 0.2 m west and one 3.8 m east
            Assert.Equal(1.1, k.Location.Z, 6); // halfway between the floor 0.4 m below and the roof 2.6 m above
        });
    }

    [Fact]
    public void BetweenScrapWalls_ItEndsInTheMiddle_NotAgainstTheFarWall()
    {
        // Two walls of vertices 2 m apart (y = ±1); the stations stand up to 0.7 m off the middle.
        // Moving 1.5 m away from the nearer wall would put the camera at or through the other one.
        var walls = new List<CaveVector3>();
        for (double x = -5; x <= 45; x += 0.25)
            for (double z = -1; z <= 1; z += 0.25)
            {
                walls.Add(new CaveVector3(x, -1, z));
                walls.Add(new CaveVector3(x, 1, z));
            }
        var framing = new CameraFraming
        {
            LocalBounds = BoundingBox.FromPoints(walls),
            CenterlinePath = [new(0, -0.1, 0), new(10, -0.6, 0), new(20, 0.5, 0), new(30, -0.1, 0), new(40, 0.7, 0)],
            WallTree = new KdTree(walls),
        };

        var keys = Fly(framing, 10, 6);

        Assert.All(keys, k => Assert.True(Math.Abs(k.Location.Y) < 0.2, $"frame {k.Frame} is at y = {k.Location.Y:F2}, not between the walls"));
    }

    [Theory]
    [InlineData(InventedCaveSize.Small)]
    [InlineData(InventedCaveSize.Medium)]
    public void BetweenScrapWalls_OfAnInventedCave_ItKeepsClearOfThem(InventedCaveSize size)
    {
        // The cave's walls the way a scrap mesh gives them: a wall vertex every half metre, and no
        // cross-sections to read.
        var geometry = GeometryStage.Build(InventedCave.Build(size));
        var route = CameraFraming.FromGeometry(geometry).CenterlinePath!;
        var pieces = new List<TubeLeg>();
        foreach (var leg in geometry.TubeLegs)
        {
            int count = Math.Max(1, (int)Math.Ceiling((leg.To.Station - leg.From.Station).Length / 0.5));
            for (int k = 0; k < count; k++)
                pieces.Add(new TubeLeg(leg.SectionAt((double)k / count), leg.SectionAt((double)(k + 1) / count), 16));
        }
        var wallVertices = TubeMesher.BuildFromLegs(pieces, new GeometryOptions { CapTubes = false, DepthTint = false }).Vertices;
        var framing = new CameraFraming { LocalBounds = geometry.LocalBounds, CenterlinePath = route, WallTree = new KdTree(wallVertices) };
        var passage = new Passage(route, geometry.RecenteredModel);

        var keys = Fly(framing, 30, 20);

        var tight = keys.Select(k => (k.Frame, Room: passage.Room(k.Location))).Where(k => k.Room < 0.3).ToList();
        Assert.True(tight.Count == 0,
            "less than 0.3 m from a wall at " + string.Join(", ", tight.Take(8).Select(k => $"frame {k.Frame}: {k.Room:F2} m")));
    }

    [Fact]
    public void ZeroClearance_RidesTheStationLine()
    {
        var lrud = new CaveLrud(0.2, 3.8, 2.6, 0.4);
        IReadOnlyList<CaveVector3> stations = [new(0, 0, 0), new(0, 10, 0), new(0, 20, 0)];
        var geometry = GeometryStage.Build(Chain(stations, lrud), new GeometryOptions { Recenter = false });

        var keys = Fly(CameraFraming.FromGeometry(geometry), 10, 2, PresetParams with { ClearanceMeters = 0 });

        // The route may run either way along the passage.
        Assert.Equal([stations[0], stations[^1]], new[] { keys[0].Location, keys[^1].Location }.OrderBy(p => p.Y));
        Assert.All(keys, k => Assert.Equal(0.0, k.Location.X, 9));
    }

    // ---- where it looks ----

    [Theory]
    [MemberData(nameof(Caves))]
    public void TheViewNeverTurnsFasterThanTheLimit(InventedCaveSize size, int seed)
    {
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(size, seed)));
        foreach (var (fps, seconds) in new[] { (10, 8.0), (30, 20.0), (24, 3.0) })
        {
            var keys = Fly(framing, fps, seconds);
            double limit = FlythroughRoute.MaxTurnDegreesPerSecond / fps;
            var fast = Turns(keys).Where(t => t.Degrees > limit + 1e-6).ToList();
            Assert.True(fast.Count == 0,
                $"at {fps} fps the view turns more than {limit:F1}° in a frame: " + string.Join(", ", fast.Take(8).Select(t => $"frame {t.Frame}: {t.Degrees:F1}°")));
        }
    }

    [Fact]
    public void InAShaft_TheViewStaysWellOffTheVertical()
    {
        // 20 m along, 40 m straight down, 20 m along in another direction.
        IReadOnlyList<CaveVector3> stations =
        [
            new(0, 0, 0), new(10, 0, 0), new(20, 0, 0), new(20, 0, -10), new(20, 0, -20), new(20, 0, -30),
            new(20, 0, -40), new(20, 10, -40), new(20, 20, -40),
        ];
        var geometry = GeometryStage.Build(Chain(stations, new CaveLrud(1.5, 1.5, 1.5, 1.5)), new GeometryOptions { Recenter = false });
        var keys = Fly(CameraFraming.FromGeometry(geometry), 30, 30);

        double steepest = keys.Max(k => Math.Abs(Math.Asin((k.Target - k.Location).Normalized().Z)) * 180.0 / Math.PI);
        Assert.InRange(steepest, 45.0, FlythroughRoute.MaxPitchDegrees + 1e-6); // it does look down the shaft, but not straight down
        Assert.All(Turns(keys), t => Assert.True(t.Degrees <= FlythroughRoute.MaxTurnDegreesPerSecond / 30 + 1e-6, $"frame {t.Frame}: {t.Degrees:F1}°"));
    }

    [Fact]
    public void TheTarget_IsTheLookAheadDistanceAway()
    {
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)));
        var keys = Fly(framing, 10, 8, PresetParams with { LookAheadMeters = 4 });
        Assert.All(keys, k => Assert.Equal(4.0, (k.Target - k.Location).Length, 6));
    }

    // ---- when it is where ----

    [Fact]
    public void ItSpeedsUpFromRest_AndSlowsDownToRest()
    {
        // A straight passage, so the distance between two frames is the distance flown.
        IReadOnlyList<CaveVector3> stations = [new(0, 0, 0), new(100, 0, 0), new(200, 0, 0), new(300, 0, 0)];
        var geometry = GeometryStage.Build(Chain(stations, new CaveLrud(2, 2, 2, 2)), new GeometryOptions { Recenter = false });
        var keys = Fly(CameraFraming.FromGeometry(geometry), 10, 8);
        var steps = keys.Zip(keys.Skip(1), (a, b) => (b.Location - a.Location).Length).ToList();

        // One second of speeding up at 10 fps, one of slowing down, the same speed between them.
        double cruise = steps[steps.Count / 2];
        Assert.True(steps[0] < cruise * 0.1, $"the first step is {steps[0]:F2} m of a cruising {cruise:F2} m");
        Assert.True(steps[^1] < cruise * 0.1, $"the last step is {steps[^1]:F2} m of a cruising {cruise:F2} m");
        for (int i = 1; i < 10; i++) Assert.True(steps[i] > steps[i - 1] + cruise * 0.05, $"step {i} does not grow");
        for (int i = 11; i < steps.Count - 11; i++) Assert.Equal(cruise, steps[i], 6);
        for (int i = steps.Count - 9; i < steps.Count; i++) Assert.True(steps[i] < steps[i - 1] - cruise * 0.05, $"step {i} does not shrink");
        Assert.Equal(80, keys.Count);
    }

    [Fact]
    public void EveryFrameHasItsOwnPose_UpToThirtySecondsAtSixtyFramesASecond()
    {
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)));
        Assert.Equal(Enumerable.Range(1, 600), Fly(framing, 30, 20).Select(k => k.Frame));
        Assert.Equal(Enumerable.Range(1, 1800), Fly(framing, 60, 30).Select(k => k.Frame));
    }

    // ---- the cross-sections the route is read from ----

    [Theory]
    [MemberData(nameof(Caves))]
    public void TubeLegs_AreTheCrossSectionsTheWallsWereBuiltFrom(InventedCaveSize size, int seed)
    {
        var options = new GeometryOptions { CapTubes = false, JoinTubes = false };
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed), options);

        // An open tube of its own is two rings of vertices per leg, in leg order.
        int sides = options.TubeSides;
        Assert.Equal(geometry.Walls.Vertices.Count, geometry.TubeLegs.Count * 2 * sides);
        for (int i = 0; i < geometry.TubeLegs.Count; i += Math.Max(1, geometry.TubeLegs.Count / 50))
        {
            var leg = geometry.TubeLegs[i];
            for (int v = 0; v < 2 * sides; v++)
                Assert.Equal(0.0, leg.Clearance(geometry.Walls.Vertices[i * 2 * sides + v]), 9);
            var middle = leg.SectionAt(0.5).Center;
            Assert.True(leg.Clearance(middle) > 0.8, $"leg {i}: {leg.Clearance(middle):F2} m of room at its middle");
            Assert.True(leg.Clearance(middle + leg.From.RightAxis * 10.0) < 0.0, "a point 10 m to the right is outside the tube");
        }
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void JoinedWalls_StayOnTheCrossSectionsOfTheirLegs(InventedCaveSize size, int seed)
    {
        // Joining moves the ends of a leg's walls along the leg (exactly on its cross-sections)
        // and, where two legs share a ring, halfway to what the other leg makes of the station.
        // That is millimetres between passage legs and more where a level leg meets a shaft,
        // whose frame is turned.
        var options = new GeometryOptions { CapTubes = false };
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed), options);
        var ends = TubeJoints.Plan(geometry.TubeLegs);

        var off = new List<double>();
        int next = 0;
        for (int i = 0; i < geometry.TubeLegs.Count; i++)
        {
            int steps = Math.Max(ends[2 * i].Steps(options.TubeSides), ends[2 * i + 1].Steps(options.TubeSides));
            var mine = new HashSet<uint>();
            for (int t = 2 * options.TubeSides * steps; t > 0; t--)
            {
                var triangle = geometry.Walls.Triangles[next++];
                mine.Add(triangle.A);
                mine.Add(triangle.B);
                mine.Add(triangle.C);
            }
            off.AddRange(mine.Select(v => Math.Abs(geometry.TubeLegs[i].Clearance(geometry.Walls.Vertices[(int)v]))));
        }
        Assert.Equal(geometry.Walls.Triangles.Count, next);

        off.Sort();
        Assert.True(off[off.Count / 2] < 0.01, $"half the wall vertices are more than {off[off.Count / 2]:F3} m off their leg's cross-section");
        Assert.True(off[off.Count * 9 / 10] < 0.05, $"a tenth of the wall vertices are more than {off[off.Count * 9 / 10]:F3} m off their leg's cross-section");
        Assert.True(off[^1] < 1.5, $"a wall vertex is {off[^1]:F2} m off its leg's cross-section");
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void RouteTubes_RunAlongTheRoute(InventedCaveSize size, int seed)
    {
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(size, seed)));
        var route = framing.CenterlinePath!;

        Assert.Equal(route.Count - 1, framing.RouteTubes!.Count);
        for (int i = 0; i < route.Count - 1; i++)
        {
            Assert.Equal(route[i], framing.RouteTubes[i]!.Value.From.Station);
            Assert.Equal(route[i + 1], framing.RouteTubes[i]!.Value.To.Station);
        }
    }

    [Fact]
    public void ScrapWalls_HaveNoTubes()
    {
        var geometry = GeometryStage.Build(LoxReaderTests.BuildSyntheticModel());
        Assert.True(geometry.HasWalls);
        Assert.Empty(geometry.TubeLegs);
        Assert.Null(CameraFraming.FromGeometry(geometry).RouteTubes);
    }

    // ---- helpers ----

    private static IEnumerable<(int Frame, double Degrees)> Turns(IReadOnlyList<CameraKeyframe> keys)
        => keys.Zip(keys.Skip(1), (a, b) => (b.Frame, Degrees((a.Target - a.Location).Normalized(), (b.Target - b.Location).Normalized())));

    private static double Degrees(CaveVector3 a, CaveVector3 b) => Math.Acos(Math.Clamp(a.Dot(b), -1.0, 1.0)) * 180.0 / Math.PI;

    private static double DistanceToPolyline(CaveVector3 point, IReadOnlyList<CaveVector3> line)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < line.Count - 1; i++)
        {
            var segment = line[i + 1] - line[i];
            double t = Math.Clamp((point - line[i]).Dot(segment) / segment.LengthSquared, 0.0, 1.0);
            best = Math.Min(best, (point - line[i].Lerp(line[i + 1], t)).Length);
        }
        return best;
    }

    /// <summary>A single passage through <paramref name="stations"/> with the same walls at every station.</summary>
    private static CaveModel Chain(IReadOnlyList<CaveVector3> stations, CaveLrud lrud)
    {
        var list = new List<CaveStation>();
        for (int i = 0; i < stations.Count; i++)
            list.Add(new CaveStation { Id = (uint)i, Name = $"s{i}", Position = stations[i] });
        var shots = new List<CaveShot>();
        for (int i = 0; i < stations.Count - 1; i++)
            shots.Add(new CaveShot { FromPosition = stations[i], ToPosition = stations[i + 1], FromLrud = lrud, ToLrud = lrud });
        return new CaveModel { Stations = list, Shots = shots };
    }

    /// <summary>The legs of a route as the open tubes the mesher makes of them, for asking how
    /// much room a point has: its distance to the nearest wall plane of a tube it is inside.</summary>
    private sealed class Passage
    {
        private readonly List<(CaveVector3 From, CaveVector3 Along, double Length, List<(CaveVector3 On, CaveVector3 Outward)> Walls)> _legs = [];

        public Passage(IReadOnlyList<CaveVector3> route, CaveModel model)
        {
            var open = new GeometryOptions { CapTubes = false, DepthTint = false };
            for (int i = 0; i < route.Count - 1; i++)
            {
                var (a, b) = (route[i], route[i + 1]);
                var shot = model.Shots.First(s => (s.FromPosition == a && s.ToPosition == b) || (s.FromPosition == b && s.ToPosition == a));
                var mesh = TubeMesher.Build([shot], open);
                var middle = (a + b) * 0.5;
                var walls = new List<(CaveVector3, CaveVector3)>();
                foreach (var t in mesh.Triangles)
                {
                    var (p, q, r) = (mesh.Vertices[(int)t.A], mesh.Vertices[(int)t.B], mesh.Vertices[(int)t.C]);
                    var normal = (q - p).Cross(r - p).Normalized();
                    walls.Add((p, normal.Dot(p - middle) < 0 ? normal * -1.0 : normal));
                }
                _legs.Add((a, (b - a).Normalized(), (b - a).Length, walls));
            }
        }

        /// <summary>How far inside the walls <paramref name="point"/> is: the most room any leg
        /// gives it that it lies beside. A leg counts up to <paramref name="beyondEnds"/> metres
        /// past its ends (the tubes of two legs do not meet exactly round the outside of a bend);
        /// a negative value asks for the point to be that far inside the ends instead. Negative
        /// when the point is outside the walls.</summary>
        public double Room(CaveVector3 point, double beyondEnds = 1.0)
        {
            double best = double.NegativeInfinity;
            foreach (var (from, along, length, walls) in _legs)
            {
                double at = (point - from).Dot(along);
                if (at < -beyondEnds || at > length + beyondEnds) continue;
                best = Math.Max(best, walls.Min(w => (w.On - point).Dot(w.Outward)));
            }
            return best;
        }
    }
}
