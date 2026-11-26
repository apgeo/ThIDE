// The route camera, without Blender: stations found by name and by symbol, joined by the
// shortest passages; a speed by passage size; holds, turns in place and an ease at every stop;
// fitted to a given length or timed by itself; one pose a frame past the flythrough's pose limit.
// Every cave here is invented: the seeded ones of SceneProbe, and straight hand-made passages
// whose sizes are known exactly.

using Therion.Blender.Emit;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class CameraRouteTests
{
    private const double Turn = FlythroughRoute.MaxTurnDegreesPerSecond * Math.PI / 180.0;

    private static CameraFraming Small() => CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)));

    private static SceneSpec Spec(RouteParams? route, int fps, double seconds) => SceneSpecTests.ValidSpec() with
    {
        Camera = new CameraSpec { Template = CameraTemplate.Route, Route = route },
        Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
    };

    private static IReadOnlyList<CameraKeyframe> Plan(CameraFraming framing, RouteParams? route, int fps, double seconds)
        => CameraPlanner.Plan(Spec(route, fps, seconds), framing).Keyframes;

    private static string[] Names(CameraFraming framing, IEnumerable<int> stations)
        => stations.Select(framing.Centerline!.StationName).ToArray();

    // ---- which stations ----

    [Fact]
    public void JoinsTheStopsByTheShortestPassages()
    {
        var framing = Small();
        // The Small cave: main0 (the entrance) … main12, and a side passage side0 … side3 off main6.
        var path = CameraRoutes.Resolve(framing, new RouteParams { From = "main2", Via = ["side3"], To = "main9" });

        string[] expected =
        [
            "main2", "main3", "main4", "main5", "main6", "side0", "side1", "side2", "side3",
            "side2", "side1", "side0", "main6", "main7", "main8", "main9",
        ];
        Assert.Equal(expected, Names(framing, path.Stations));
        Assert.Equal([0, 8, expected.Length - 1], path.Stops);
        Assert.Equal([0.0, 1.0, 0.0], path.HoldSeconds);
    }

    [Fact]
    public void TheCentrelinesShortestPathIsAsLongAsItsShortestDistance()
    {
        var graph = Small().Centerline!;
        int from = 0, to = Enumerable.Range(0, 30).Last(i => graph.Degree(i) > 0);
        var way = graph.ShortestPath(from, to);
        double length = way.Zip(way.Skip(1), (a, b) => (graph.StationPosition(b) - graph.StationPosition(a)).Length).Sum();
        Assert.Equal(graph.ShortestPathLengths(from)[to], length, 9);
        Assert.Equal([from], graph.ShortestPath(from, from));

        var large = GeometryStage.Build(InventedCave.Build(InventedCaveSize.Large)).Centerline;
        Assert.Empty(large.ShortestPath(large.Components[0].StationIndices[0], large.Components[1].StationIndices[0]));
    }

    [Fact]
    public void AListOfStationsStopsOnlyAtItsEnds()
    {
        var framing = Small();
        var path = CameraRoutes.Resolve(framing, new RouteParams { Stations = ["main1", "main4", "side1"], EndHoldSeconds = 2 });

        Assert.Equal(["main1", "main2", "main3", "main4", "main5", "main6", "side0", "side1"], Names(framing, path.Stations));
        Assert.Equal([0, 7], path.Stops);
        Assert.Equal([0.0, 2.0], path.HoldSeconds);
    }

    [Fact]
    public void WithoutParametersARouteRunsFromTheEntranceToTheFarthestStation()
    {
        var framing = Small();
        var path = CameraRoutes.Resolve(framing, new RouteParams());
        var graph = framing.Centerline!;

        Assert.Equal("main0", graph.StationName(path.Stations[0]));
        var lengths = graph.ShortestPathLengths(path.Stations[0]);
        Assert.Equal(lengths.Values.Max(), lengths[path.Stations[^1]]);
        Assert.Equal(path.Stations, CameraRoutes.Resolve(framing, new RouteParams { From = "entrance", To = "farthest" }).Stations);
    }

    [Fact]
    public void TheEntranceIsTheFirstStationFlaggedSo()
    {
        // The Medium cave flags two entrances: upper0, then the far end of the lower level.
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Medium)));
        var path = CameraRoutes.Resolve(framing, new RouteParams { From = RouteParams.Entrance, To = "upper5" });
        Assert.Equal("upper0", framing.Centerline!.StationName(path.Stations[0]));
    }

    [Theory]
    [InlineData("nowhere", null, "camera.route.from")]
    [InlineData("main0", "nowhere", "camera.route.via[0]")]
    public void AStationNotInTheModelIsNamed(string from, string? via, string field)
    {
        var route = new RouteParams { From = from, Via = via is null ? [] : [via], To = "main3" };
        var error = Assert.Throws<ArgumentException>(() => CameraRoutes.Resolve(Small(), route));
        Assert.Contains("'nowhere'", error.Message);
        Assert.Contains(field, error.Message);
    }

    [Fact]
    public void ANameInTwoSurveysTakesItsSurvey()
    {
        var framing = TwoSurveys();
        var error = Assert.Throws<ArgumentException>(() => CameraRoutes.Resolve(framing, new RouteParams { From = "0", To = "3" }));
        Assert.Contains("'0'", error.Message);
        Assert.Contains("a, b", error.Message);

        var path = CameraRoutes.Resolve(framing, new RouteParams { From = "0@a", To = "3@b" });
        Assert.Equal(0, path.Stations[0]);
        Assert.Equal("3@b", CameraRoutes.NameOf(framing, path.Stations[^1]));
        Assert.Equal("0@a", CameraRoutes.NameOf(framing, path.Stations[0]));
    }

    [Fact]
    public void StopsInPartsNotJoinedAreRefused()
    {
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Large)));
        var error = Assert.Throws<ArgumentException>(() => CameraRoutes.Resolve(framing, new RouteParams { From = "system0", To = "far3" }));
        Assert.Contains("'far3'", error.Message);
    }

    [Fact]
    public void TheValidatorChecksTheRoutesFields()
    {
        var errors = SceneSpecValidator.Validate(Spec(new RouteParams { From = "a", Stations = ["a", "b"], HoldSeconds = -1 }, 30, 4));
        Assert.Contains(errors, e => e.Path == "camera.route.stations");
        Assert.Contains(errors, e => e.Path == "camera.route.holdSeconds");
        Assert.Contains(SceneSpecValidator.Validate(Spec(new RouteParams { From = "farthest" }, 30, 4)), e => e.Path == "camera.route.from");
        Assert.Empty(SceneSpecValidator.Validate(Spec(null, 30, 4)));
    }

    [Fact]
    public void ARouteRoundTripsThroughJson()
    {
        var spec = Spec(new RouteParams { From = "main0", Via = ["side3"], To = "main12", EndHoldSeconds = 1.5, Timing = RouteTiming.Natural }, 30, 4);
        var back = SceneSpecSerializer.Read(SceneSpecSerializer.Write(spec));
        Assert.Equal(CameraTemplate.Route, back.Camera.Template);
        Assert.Equal(["side3"], back.Camera.Route!.Via);
        Assert.Equal(RouteTiming.Natural, back.Camera.Route.Timing);
        Assert.Equal(1.5, back.Camera.Route.EndHoldSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ARouteKeepsClearOfTheWallsOfItsPassages(int seed)
    {
        var geometry = GeometryStage.Build(InventedCave.Build(InventedCaveSize.Medium, seed));
        var framing = CameraFraming.FromGeometry(geometry);
        // Down the pitch, out to a dead end of the lower level and back, on to the far entrance.
        var route = new RouteParams { From = RouteParams.Entrance, Via = ["lower61", "lower30"], To = "lower49" };
        var path = CameraRoutes.Resolve(framing, route);
        var passage = new FlythroughRouteTests.Passage(path.Stations.Select(framing.Centerline!.StationPosition).ToList(), geometry.RecenteredModel);

        var keys = Plan(framing, route, 10, 60);
        var tight = keys.Select(k => (k.Frame, Room: passage.Room(k.Location))).Where(k => k.Room < 0.3).ToList();
        Assert.True(tight.Count == 0, "less than 0.3 m from a wall at " + string.Join(", ", tight.Take(8).Select(k => $"frame {k.Frame}: {k.Room:F2} m")));
        // The view never steeper than the limit.
        Assert.All(keys, k => Assert.True(Math.Abs(Math.Asin(Direction(k).Z)) <= FlythroughRoute.MaxPitchDegrees * Math.PI / 180 + 1e-6));
    }

    // ---- how fast ----

    [Fact]
    public void AWidePassageIsFlownFasterThanANarrowOne()
    {
        // One straight passage east: 100 m with walls 6 m from its middle, then 100 m with walls
        // 0.5 m off: 12 m/s and 1.5 m/s, the two ends of the speed range.
        var framing = Straight(wide: 6.0, narrow: 0.5);
        var noHold = new RouteParams { HoldSeconds = 0, ClearanceMeters = 0.3 };
        var wide = CameraRoutes.Measure(framing, noHold with { From = "w0", To = "w10" });
        var narrow = CameraRoutes.Measure(framing, noHold with { From = "n0", To = "n10" });

        // Cruise, plus half the ease at each end (from rest to cruise over one second covers half
        // the distance cruise would).
        Assert.Equal(wide.LengthMeters / 12.0 + FlythroughRoute.EaseSeconds, wide.TravelSeconds, 0.05);
        Assert.Equal(narrow.LengthMeters / 1.5 + FlythroughRoute.EaseSeconds, narrow.TravelSeconds, 0.05);
        Assert.True(narrow.TravelSeconds > 6 * wide.TravelSeconds);
        Assert.Equal(12.0, CameraRoutes.SpeedFor(40));
        Assert.Equal(1.5, CameraRoutes.SpeedFor(0.2));
        Assert.Equal(5.0, CameraRoutes.SpeedFor(2.5));
    }

    [Fact]
    public void SpeedChangesGraduallyBetweenPassagesOfDifferentSize()
    {
        var framing = Straight(wide: 6.0, narrow: 0.5);
        var route = new RouteParams { From = "w0", To = "n10", ClearanceMeters = 0.3 };
        var measure = CameraRoutes.Measure(framing, route);
        int fps = 30;
        var keys = Plan(framing, route with { Timing = RouteTiming.Natural }, fps, measure.Seconds + 1.0 / fps);
        var speeds = Speeds(keys, fps);

        Assert.InRange(speeds.Max(), 11.0, 12.05);
        // Never faster up or down than the cruise acceleration, away from the two ends' ease.
        // Over a third of a second, which averages out the steps between the samples of the profile.
        const int window = 10;
        for (int i = fps * 2; i < speeds.Length - fps * 2; i++)
            Assert.True(Math.Abs(speeds[i] - speeds[i - window]) * fps / window <= RouteFlight.CruiseAcceleration + 0.05,
                $"frame {i}: {speeds[i - window]:F3} → {speeds[i]:F3} m/s");
    }

    // ---- stops ----

    [Fact]
    public void EveryStopEasesInAndOut()
    {
        var framing = Small();
        var route = new RouteParams { From = "main0", Via = ["main6"], To = "main12", HoldSeconds = 0.2 };
        var measure = CameraRoutes.Measure(framing, route);
        const int fps = 30;
        var keys = Plan(framing, route, fps, measure.Seconds + 1.0 / fps);
        var speeds = Speeds(keys, fps);

        // At rest at both ends and at the stop between; at speed between them.
        Assert.True(speeds[0] < 0.15 && speeds[^1] < 0.15, $"{speeds[0]:F3}, {speeds[^1]:F3} m/s");
        var stop = Enumerable.Range(1, speeds.Length - 2).Where(i => speeds[i] < 0.05).ToList();
        Assert.NotEmpty(stop);
        int middle = stop[stop.Count / 2];
        Assert.InRange(middle, fps * 3, speeds.Length - fps * 3);
        Assert.True(speeds.Take(middle).Max() > 1.5 && speeds.Skip(middle).Max() > 1.5);
        // From rest at an even acceleration over about a second: half the speed at half a second.
        int moving = Enumerable.Range(middle, speeds.Length - middle).First(i => speeds[i] > 0.05);
        double ratio = speeds[moving + fps / 2] / speeds[moving + fps];
        Assert.InRange(ratio, 0.35, 0.65);
    }

    [Fact]
    public void TheCameraHoldsWhereItIsAskedTo()
    {
        var framing = Small();
        var route = new RouteParams { From = "main0", Via = ["main6"], To = "main12", StartHoldSeconds = 1, HoldSeconds = 2, EndHoldSeconds = 1.5 };
        var measure = CameraRoutes.Measure(framing, route);
        Assert.Equal(4.5, measure.HoldSeconds, 9);

        const int fps = 20;
        var keys = Plan(framing, route, fps, measure.Seconds + 1.0 / fps);
        Assert.Equal(keys.Count, (int)Math.Round(fps * (measure.Seconds + 1.0 / fps)));
        // The longest run of frames standing still: the hold at main6 and its turn, two seconds or more.
        int longest = 0, run = 0;
        for (int i = 1; i < keys.Count; i++)
        {
            run = (keys[i].Location - keys[i - 1].Location).Length < 1e-9 ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        Assert.InRange(longest, 2 * fps, (2 + measure.TurnSeconds) * fps + 2);
        // Still for the first second and the last second and a half.
        Assert.All(keys.Take(fps), k => Assert.Equal(keys[0].Location, k.Location));
        Assert.All(keys.TakeLast(3 * fps / 2), k => Assert.Equal(keys[^1].Location, k.Location));
    }

    [Fact]
    public void AtADeadEndTheCameraTurnsRoundInPlace()
    {
        var framing = Small();
        // Out to the end of the side passage and back: a half turn there.
        var route = new RouteParams { From = "main6", Via = ["side3"], To = "main6", HoldSeconds = 0.5 };
        var measure = CameraRoutes.Measure(framing, route);
        Assert.InRange(measure.TurnSeconds, Math.PI * 0.85 / Turn, Math.PI * 1.05 / Turn);

        const int fps = 30;
        var keys = Plan(framing, route, fps, measure.Seconds + 1.0 / fps);
        // The frames where the camera stands still at the dead end: its view turns round there,
        // never faster than the turn limit.
        var still = Enumerable.Range(1, keys.Count - 2)
            .Where(i => (keys[i].Location - keys[i - 1].Location).Length < 1e-9 && (keys[i].Location - keys[0].Location).Length > 1).ToList();
        Assert.InRange(still.Count, (int)((0.5 + measure.TurnSeconds) * fps) - 2, (int)((0.5 + measure.TurnSeconds) * fps) + 2);
        var before = Direction(keys[still[0]]);
        var after = Direction(keys[still[^1]]);
        Assert.True(before.Dot(after) < -0.8, $"the view turned {Math.Acos(before.Dot(after)) * 180 / Math.PI:F0}°");
        double limit = Turn / fps + 1e-6;
        for (int i = 1; i < keys.Count; i++)
            Assert.True(Math.Acos(Math.Clamp(Direction(keys[i]).Dot(Direction(keys[i - 1])), -1, 1)) <= limit * 1.01, $"frame {i}");
    }

    // ---- how long ----

    [Fact]
    public void AGivenLengthScalesTheTravelAndKeepsTheHolds()
    {
        var framing = Small();
        var route = new RouteParams { From = "main0", To = "main12", StartHoldSeconds = 2, HoldSeconds = 0 };
        var measure = CameraRoutes.Measure(framing, route);
        const int fps = 10;
        double seconds = 2 + measure.TravelSeconds * 2; // twice the travel time
        var keys = Plan(framing, route, fps, seconds + 1.0 / fps);
        // The camera starts to move after the two seconds' hold, and keeps moving to the end.
        int firstMove = Enumerable.Range(1, keys.Count - 1).First(i => (keys[i].Location - keys[i - 1].Location).Length > 1e-9);
        Assert.InRange(firstMove, 2 * fps, 2 * fps + 1);
        Assert.True((keys[^1].Location - keys[^2].Location).Length > 0);
        Assert.Equal(keys[0].Location, CameraRoutes.Fly(framing, route, 2, 1)[0].Location);
    }

    [Fact]
    public void ANaturalRouteSetsTheDuration()
    {
        var framing = Small();
        var route = new RouteParams { Timing = RouteTiming.Natural, EndHoldSeconds = 1 };
        var spec = CameraRoutes.WithNaturalDuration(Spec(route, 30, 4), framing);
        Assert.Equal(CameraRoutes.Measure(framing, route).Seconds + 1.0 / 30, spec.Animation.DurationSeconds, 3);

        var fitted = Spec(route with { Timing = RouteTiming.Fit }, 30, 4);
        Assert.Same(fitted, CameraRoutes.WithNaturalDuration(fitted, framing));
    }

    [Fact]
    public void ALongRouteHasAPoseForEveryFramePastTheFlythroughsLimit()
    {
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Medium)));
        var keys = Plan(framing, new RouteParams(), 30, 120); // 3,600 frames
        Assert.Equal(3600, keys.Count);
        Assert.Equal(Enumerable.Range(1, 3600), keys.Select(k => k.Frame));
        Assert.All(keys, k => Assert.True(double.IsFinite(k.Location.X) && double.IsFinite(k.Target.Z)));
    }

    [Fact]
    public void TheSameRoutePlansTheSamePoses()
    {
        var a = Plan(Small(), new RouteParams { Via = ["side3"] }, 24, 30);
        var b = Plan(Small(), new RouteParams { Via = ["side3"] }, 24, 30);
        Assert.Equal(a, b);
    }

    // ---- framing ----

    [Fact]
    public void AFramingCanRunAlongAnyStations()
    {
        var framing = Small();
        var path = CameraRoutes.Resolve(framing, new RouteParams { From = "main3", To = "side2" });
        var along = framing.AlongStations(path.Stations);

        Assert.Equal(path.Stations.Count, along.CenterlinePath!.Count);
        Assert.Equal(path.Stations.Count - 1, along.RouteTubes!.Count);
        Assert.All(along.RouteTubes, t => Assert.True(t.HasValue));
        // A flythrough along it flies that way and not the longest passage.
        var spec = SceneSpecTests.ValidSpec() with { Camera = new CameraSpec { Template = CameraTemplate.Flythrough } };
        var keys = CameraPlanner.Plan(spec, along).Keyframes;
        Assert.True((keys[^1].Location - along.CenterlinePath[^1]).Length < 4);
    }

    // ---- helpers ----

    private static double[] Speeds(IReadOnlyList<CameraKeyframe> keys, int fps)
    {
        var speeds = new double[keys.Count];
        for (int i = 1; i < keys.Count; i++) speeds[i] = (keys[i].Location - keys[i - 1].Location).Length * fps;
        speeds[0] = speeds.Length > 1 ? speeds[1] : 0.0;
        return speeds;
    }

    private static CaveVector3 Direction(CameraKeyframe key) => (key.Target - key.Location).Normalized();

    /// <summary>A straight passage east: ten 10 m legs with the walls <paramref name="wide"/>
    /// from the middle (w0 … w10), then ten with them <paramref name="narrow"/> off (n0 … n10,
    /// n0 at w10's place).</summary>
    private static CameraFraming Straight(double wide, double narrow)
    {
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        var lrud = new List<CaveLrud>();
        void Station(string name, double x, double size)
        {
            stations.Add(new CaveStation { Id = (uint)stations.Count, SurveyId = 1, Name = name, Position = new CaveVector3(x, 0, 0) });
            lrud.Add(new CaveLrud(size, size, size, size));
        }
        void Leg(int from, int to) => shots.Add(new CaveShot
        {
            FromStationId = (uint)from, ToStationId = (uint)to,
            FromPosition = stations[from].Position, ToPosition = stations[to].Position,
            SurveyId = 1, SectionType = CaveShotSection.Oval, FromLrud = lrud[from], ToLrud = lrud[to],
        });
        for (int i = 0; i <= 10; i++) Station($"w{i}", i * 10.0, wide);
        for (int i = 1; i <= 10; i++) Leg(i - 1, i);
        Station("n0", 100.0, narrow); // the same place as w10: one station of the centreline
        for (int i = 1; i <= 10; i++) Station($"n{i}", 100.0 + i * 10.0, narrow);
        for (int i = 1; i <= 10; i++) Leg(10 + i, 11 + i);
        shots[10] = shots[10] with { FromStationId = 10, FromPosition = stations[10].Position, FromLrud = lrud[11] };
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Surveys = [new CaveSurvey(0, 0, "", "Invented straight"), new CaveSurvey(1, 0, "s", "Straight")],
            Stations = stations,
            Shots = shots,
        };
        return CameraFraming.FromGeometry(GeometryStage.Build(model));
    }

    /// <summary>Two invented surveys, a and b, each with stations 0 … 3, joined end to start.</summary>
    private static CameraFraming TwoSurveys()
    {
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        for (uint survey = 1; survey <= 2; survey++)
            for (int i = 0; i < 4; i++)
                stations.Add(new CaveStation
                {
                    Id = (uint)stations.Count, SurveyId = survey, Name = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Position = new CaveVector3((stations.Count - (survey - 1)) * 8.0, 0, 0),
                });
        for (int i = 1; i < stations.Count; i++)
        {
            if (i == 4) continue; // b0 stands where a3 does: the two join there
            shots.Add(new CaveShot
            {
                FromStationId = (uint)(i - 1), ToStationId = (uint)i,
                FromPosition = stations[i - 1].Position, ToPosition = stations[i].Position, SurveyId = stations[i].SurveyId ?? 0,
            });
        }
        return CameraFraming.FromGeometry(GeometryStage.Build(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Surveys = [new CaveSurvey(0, 0, "", "Invented"), new CaveSurvey(1, 0, "a", "A"), new CaveSurvey(2, 0, "b", "B")],
            Stations = stations,
            Shots = shots,
        }));
    }
}
