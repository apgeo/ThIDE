// Camera-engine numeric tests (BA-B6 batch 2): the path math behind each template —
// orbit stays on its fit sphere, the helix descends monotonically, the flythrough runs
// at constant speed / clears walls / looks ahead, viewpoints hit their frames with holds,
// and the still set frames the standard directions. Pure C#, no Blender.

using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class CameraEngineTests
{
    // A fixed, off-origin, non-cubic local box so the tests catch axis/centre mistakes.
    private static readonly BoundingBox Box = new(new CaveVector3(-40, -30, -20), new CaveVector3(60, 50, 20));
    private static CaveVector3 Center => Box.Center; // (10, 10, 0)

    private static CameraFraming Framing(IReadOnlyList<CaveVector3>? path = null, KdTree? walls = null)
        => new() { LocalBounds = Box, CenterlinePath = path, WallTree = walls };

    private static SceneSpec Spec(CameraSpec camera, int fps = 12, double seconds = 2, OutputKind kind = OutputKind.Video)
        => SceneSpecTests.ValidSpec() with
        {
            Camera = camera,
            Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
            Output = SceneSpecTests.ValidSpec().Output with { Kind = kind },
        };

    // ---- static ----

    [Fact]
    public void Static_IsRuntimeFramed_NoKeyframes()
    {
        var plan = CameraPlanner.Plan(Spec(new CameraSpec()));
        Assert.True(plan.IsStatic);
        Assert.Empty(plan.Keyframes);
        Assert.Equal(24, plan.FrameCount); // 12 fps × 2 s
    }

    [Fact]
    public void AnimatedTemplate_WithoutFraming_Throws()
    {
        var spec = Spec(new CameraSpec { Template = CameraTemplate.Orbit });
        var ex = Assert.Throws<ArgumentException>(() => CameraPlanner.Plan(spec, framing: null));
        Assert.Contains("framing", ex.Message);
    }

    // ---- orbit ----

    [Fact]
    public void Orbit_StaysOnTheFitSphere_AtConstantElevation()
    {
        var cam = new CameraSpec { Template = CameraTemplate.Orbit, FocalLength = 35, AutoFramePadding = 1.2, Orbit = new OrbitParams { Revolutions = 1, ElevationDegrees = 25 } };
        var plan = CameraPlanner.Plan(Spec(cam), Framing());

        double radius = Box.Diagonal / 2.0;
        double fit = CameraPlanner.FitDistance(radius, 35, 1.2);
        double expectedZ = Center.Z + Math.Sin(25 * Math.PI / 180.0) * fit;

        Assert.NotEmpty(plan.Keyframes);
        Assert.Equal(KeyInterpolation.Linear, plan.Interpolation);
        foreach (var k in plan.Keyframes)
        {
            Assert.Equal(fit, (k.Location - Center).Length, 6);     // on the sphere
            Assert.Equal(expectedZ, k.Location.Z, 6);               // constant elevation band
            Assert.Equal(Center, k.Target);                         // always looks at centre
        }
        Assert.Equal(1, plan.Keyframes[0].Frame);
        Assert.Equal(plan.FrameCount, plan.Keyframes[^1].Frame);
    }

    [Fact]
    public void Orbit_DirectionFlipsTheSweepSign()
    {
        CameraSpec Cam(TurnDirection d) => new() { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 1, ElevationDegrees = 0, StartAngleDegrees = 0, Direction = d } };
        var ccw = CameraPlanner.Plan(Spec(Cam(TurnDirection.CounterClockwise)), Framing());
        var cw = CameraPlanner.Plan(Spec(Cam(TurnDirection.Clockwise)), Framing());

        // Second keyframe: CCW turns toward +Y, CW toward -Y (both start on +X of centre).
        Assert.True(ccw.Keyframes[1].Location.Y > Center.Y);
        Assert.True(cw.Keyframes[1].Location.Y < Center.Y);
    }

    // ---- helix ----

    [Fact]
    public void Helix_DescendsMonotonically_TopToBottom()
    {
        var cam = new CameraSpec { Template = CameraTemplate.Helix, Helix = new HelixParams { Turns = 2, StartHeightFraction = 1, EndHeightFraction = 0 } };
        var plan = CameraPlanner.Plan(Spec(cam, fps: 12, seconds: 3), Framing());

        Assert.Equal(Box.Max.Z, plan.Keyframes[0].Location.Z, 6);   // starts at the top
        Assert.Equal(Box.Min.Z, plan.Keyframes[^1].Location.Z, 6);  // ends at the bottom
        for (int i = 1; i < plan.Keyframes.Count; i++)
            Assert.True(plan.Keyframes[i].Location.Z <= plan.Keyframes[i - 1].Location.Z + 1e-9);
        // Target rides the central axis at the camera's own height (level look).
        foreach (var k in plan.Keyframes)
        {
            Assert.Equal(Center.X, k.Target.X, 6);
            Assert.Equal(Center.Y, k.Target.Y, 6);
            Assert.Equal(k.Location.Z, k.Target.Z, 6);
        }
    }

    [Fact]
    public void Helix_ConstantRadius_WhenScalesEqual()
    {
        var cam = new CameraSpec { Template = CameraTemplate.Helix, Helix = new HelixParams { Turns = 1.5, RadiusScale = 1, EndRadiusScale = 1 } };
        var plan = CameraPlanner.Plan(Spec(cam), Framing());
        double Horizontal(CameraKeyframe k) => Math.Sqrt(Math.Pow(k.Location.X - Center.X, 2) + Math.Pow(k.Location.Y - Center.Y, 2));
        double r0 = Horizontal(plan.Keyframes[0]);
        foreach (var k in plan.Keyframes) Assert.Equal(r0, Horizontal(k), 6);
    }

    // ---- flythrough ----

    [Fact]
    public void Flythrough_NeedsACenterlinePath()
    {
        var spec = Spec(new CameraSpec { Template = CameraTemplate.Flythrough });
        var ex = Assert.Throws<ArgumentException>(() => CameraPlanner.Plan(spec, Framing(path: null)));
        Assert.Contains("centerline", ex.Message);
    }

    [Fact]
    public void Flythrough_RunsAtConstantSpeed_AlongThePath()
    {
        // A straight run so smoothing/geometry don't perturb the spacing check.
        var path = new List<CaveVector3> { new(0, 0, 0), new(10, 0, 0), new(20, 0, 0), new(30, 0, 0) };
        var cam = new CameraSpec { Template = CameraTemplate.Flythrough, Flythrough = new FlythroughParams { SmoothingIterations = 0, ClearanceMeters = 0, LookAheadMeters = 5 } };
        var plan = CameraPlanner.Plan(Spec(cam, fps: 10, seconds: 1), Framing(path));

        var keys = plan.Keyframes;
        double firstStep = (keys[1].Location - keys[0].Location).Length;
        for (int i = 1; i < keys.Count; i++)
        {
            double step = (keys[i].Location - keys[i - 1].Location).Length;
            Assert.Equal(firstStep, step, 4); // equal arc-length spacing = constant speed
        }
        // Look-ahead: the target leads the camera down the path (+X).
        Assert.True(keys[0].Target.X > keys[0].Location.X);
    }

    [Fact]
    public void Flythrough_PushesOffTheNearestWall()
    {
        // One path point sitting 0.5 m from a wall; clearance 2 m must push it away (+Y).
        var path = new List<CaveVector3> { new(0, 0, 0), new(0, 0, 0.001) };
        var walls = new KdTree([new CaveVector3(0, -0.5, 0)]);
        var cam = new CameraSpec { Template = CameraTemplate.Flythrough, Flythrough = new FlythroughParams { SmoothingIterations = 0, ClearanceMeters = 2, LookAheadMeters = 1 } };
        var plan = CameraPlanner.Plan(Spec(cam), Framing(path, walls));

        Assert.True(plan.Keyframes[0].Location.Y > 0.0); // moved away from the wall at y=-0.5
    }

    // ---- viewpoints ----

    [Fact]
    public void Viewpoints_HitFirstAndLastFrame_AndHoldInPlace()
    {
        var vps = new ViewpointParams
        {
            Viewpoints =
            [
                new CameraViewpoint { Position = new(50, 0, 0), Target = Center, HoldSeconds = 1 },
                new CameraViewpoint { Position = new(0, 50, 0), Target = Center, FocalLength = 50 },
                new CameraViewpoint { Position = new(-50, 0, 10), Target = Center },
            ],
        };
        var plan = CameraPlanner.Plan(Spec(new CameraSpec { Template = CameraTemplate.Viewpoints, Viewpoints = vps }, fps: 12, seconds: 4), Framing());

        Assert.Equal(1, plan.Keyframes[0].Frame);
        Assert.Equal(plan.FrameCount, plan.Keyframes[^1].Frame);
        Assert.True(plan.KeyframeFocal); // one viewpoint set a focal length
        // The first viewpoint holds for ~1 s: two keyframes with the same pose.
        var atFirstPose = plan.Keyframes.Where(k => k.Location == new CaveVector3(50, 0, 0)).ToList();
        Assert.True(atFirstPose.Count >= 2);
        Assert.Equal(KeyInterpolation.Bezier, plan.Interpolation);
    }

    // ---- still set ----

    [Fact]
    public void StillSet_FramesEachStandardView_Discretely()
    {
        var cam = new CameraSpec
        {
            Template = CameraTemplate.StillSet,
            Stills = new StillSetParams { Views = [StandardView.Top, StandardView.Front, StandardView.Left] },
        };
        var plan = CameraPlanner.Plan(Spec(cam, kind: OutputKind.FrameSequence), Framing());

        Assert.Equal(3, plan.FrameCount);
        Assert.Equal(KeyInterpolation.Constant, plan.Interpolation);
        Assert.Equal([1, 2, 3], plan.Keyframes.Select(k => k.Frame));
        // Top view sits directly above the centre and looks at it.
        var top = plan.Keyframes[0];
        Assert.Equal(Center.X, top.Location.X, 6);
        Assert.Equal(Center.Y, top.Location.Y, 6);
        Assert.True(top.Location.Z > Center.Z);
        Assert.Equal(Center, top.Target);
    }

    // ---- longest-path extraction (flythrough route) ----

    [Fact]
    public void LongestPath_ReturnsTheGraphDiameter()
    {
        // A straight chain of 4 stations joined by real legs → the whole chain is the path.
        var model = ChainModel([new(0, 0, 0), new(10, 0, 0), new(20, 0, 0), new(30, 0, 0)]);
        var polyline = CenterlineGraph.Build(model).LongestPathPolyline();

        Assert.Equal(4, polyline.Count);
        // The diameter spans both ends; either orientation is a valid longest path.
        var ends = new[] { polyline[0], polyline[^1] };
        Assert.Contains(new CaveVector3(0, 0, 0), ends);
        Assert.Contains(new CaveVector3(30, 0, 0), ends);
    }

    private static CaveModel ChainModel(IReadOnlyList<CaveVector3> points)
    {
        var stations = new List<CaveStation>();
        for (int i = 0; i < points.Count; i++)
            stations.Add(new CaveStation { Id = (uint)i, Name = $"s{i}", Position = points[i] });
        var shots = new List<CaveShot>();
        for (int i = 0; i < points.Count - 1; i++)
            shots.Add(new CaveShot { FromPosition = points[i], ToPosition = points[i + 1] });
        return new CaveModel { Stations = stations, Shots = shots };
    }
}
