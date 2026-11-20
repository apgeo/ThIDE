// The camera of the player in a real Blender, frame by frame: a track with a pose for every frame
// is reproduced exactly, whether its poses aim the camera at targets or turn it by orientations,
// straight down and rolled included; between two oriented poses Blender takes the short way. The
// plans are invented; the scene is built and not rendered. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerCameraBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("player-camera-small", InventedCave.Build(InventedCaveSize.Small)));

    // What a camera reproduced exactly means: within float32 of the plan.
    private const double EyeTolerance = 0.001;          // metres
    private const double ForwardTolerance = 0.005;      // degrees
    private const double UpTolerance = 0.01;            // degrees

    private const int Fps = 10;

    private static SceneSpec Orbit => PlanFixtures.Preset("Orbit showcase") with
    {
        Labels = new LabelsSpec(),
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
    };

    /// <summary>The orbit's plan of the small cave with <paramref name="track"/> as its camera.</summary>
    private static (ScenePlan Plan, PlayerJob Job) Job(string name, IReadOnlyList<PlanCameraKey> track, int frames,
        PlanInterpolation interpolation = PlanInterpolation.Linear)
        => PlayerRunner.WriteJob(name, Small.Value, Orbit, plan => plan with
        {
            Timeline = new PlanTimeline { Fps = Fps, FrameCount = frames },
            Camera = plan.Camera with { Interpolation = interpolation, FStop = null, Track = track },
        });

    private static double Seconds(int frame) => (frame - 1) / (double)Fps;

    /// <summary>An invented flight over the cave: a turn round its middle, climbing.</summary>
    private static CaveVector3 EyeAt(int frame)
    {
        var centre = Small.Value.Framing.LocalBounds.Center;
        double far = Small.Value.Framing.LocalBounds.Diagonal, a = frame * 0.21;
        return centre + new CaveVector3(far * Math.Cos(a), far * Math.Sin(a), far * (0.3 + 0.01 * frame));
    }

    /// <summary>Heading, pitch and roll in degrees for each frame: level, tilted, rolled, straight
    /// down at several headings (the top of the picture is the only thing that tells them apart),
    /// straight down and rolled, straight up, and across north and south.</summary>
    private static readonly (double Heading, double Pitch, double Roll)[] Views =
    [
        (0, 0, 0), (45, -10, 0), (90, -30, 0), (135, -60, 0), (180, -89.5, 0),
        (0, -90, 0), (60, -90, 0), (179, -90, 0), (181, -90, 0), (300, -90, 0),
        (300, -90, 25), (300, -90, -40), (20, -45, 15), (350, -45, 15), (10, -45, -15),
        (170, -20, 0), (190, -20, 0), (190, 0, 90), (190, 0, 180), (250, 90, 0),
        (250, 89.9, 0), (250, 30, -5), (359.9, -5, 0), (0.1, -5, 0),
    ];

    private static (CaveVector3 Forward, CaveVector3 Up) View(int index)
        => PlanQuaternionTests.View(Views[index].Heading, Views[index].Pitch, Views[index].Roll);

    [BlenderFact]
    public async Task APoseForEveryFrame_TurnedByItsOrientation_IsWhereTheCameraIs_StraightDownAndRolledIncluded()
    {
        int frames = Views.Length;
        var track = Enumerable.Range(1, frames).Select(frame =>
        {
            var (forward, up) = View(frame - 1);
            // The target is only where the camera focuses: here somewhere off its line of sight.
            return new PlanCameraKey(Seconds(frame), EyeAt(frame), EyeAt(frame) + new CaveVector3(1, 2, -3))
            {
                Orientation = PlanQuaternion.FromView(forward, up),
            };
        }).ToList();
        var (_, job) = Job("camera-oriented", track, frames);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        Assert.Empty(report.Object("Camera").Constraints);
        for (int frame = 1; frame <= frames; frame++)
        {
            var seen = report.Frame(frame).Camera!;
            var (forward, up) = View(frame - 1);
            AssertCamera(EyeAt(frame), forward, up, seen, $"frame {frame} {Views[frame - 1]}");
            AssertNear(track[frame - 1].Target, seen.Target!, EyeTolerance, $"frame {frame}: the target");
        }
    }

    [BlenderFact]
    public async Task APoseForEveryFrame_AimedAtItsTarget_IsWhereTheCameraIs_WithALevelHorizon()
    {
        // The views that are not straight up or down: a target can aim only those.
        var aimed = Enumerable.Range(0, Views.Length).Where(i => Math.Abs(Views[i].Pitch) <= 60).ToList();
        int frames = aimed.Count;
        var track = Enumerable.Range(1, frames).Select(frame =>
        {
            var (forward, _) = View(aimed[frame - 1]);
            return new PlanCameraKey(Seconds(frame), EyeAt(frame), EyeAt(frame) + forward * (5 + frame));
        }).ToList();
        var (_, job) = Job("camera-aimed", track, frames);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        Assert.Equal("TRACK_TO", Assert.Single(report.Object("Camera").Constraints).Type);
        for (int frame = 1; frame <= frames; frame++)
        {
            var (forward, _) = View(aimed[frame - 1]);
            // A level horizon: the top of the picture is up, as far as the line of sight lets it be.
            var level = CaveVector3.UnitZ - forward * forward.Z;
            AssertCamera(EyeAt(frame), forward, level / level.Length, report.Frame(frame).Camera!, $"frame {frame}");
        }
    }

    [BlenderFact]
    public async Task BetweenTwoOrientations_TheCameraTurnsTheShortWay_AsTheTrackInterpolates()
    {
        // Keys four frames apart. Straight down from heading 170 to 190 the two quaternions FromView
        // gives lie on opposite sides (W >= 0 for both): taken as they are, half way would be
        // heading 0, the long way round.
        (double, double, double)[] keyed = [(150, -90, 0), (170, -90, 0), (190, -90, 0), (230, -40, 20), (260, -10, 0)];
        const int step = 4;
        var turns = keyed.Select(v => PlanQuaternion.FromView(
            PlanQuaternionTests.View(v.Item1, v.Item2, v.Item3).Forward, PlanQuaternionTests.View(v.Item1, v.Item2, v.Item3).Up)).ToList();
        Assert.True(Dot(turns[1], turns[2]) < 0, "the case needs two quaternions on opposite sides");
        var track = turns.Select((turn, i) => new PlanCameraKey(Seconds(1 + i * step), EyeAt(1), EyeAt(1) + new CaveVector3(0, 0, -10))
        {
            Orientation = turn,
        }).ToList();
        int frames = 1 + (keyed.Length - 1) * step;

        var linear = await PlayerRunner.ProbeAsync(
            Job("camera-oriented-linear", track, frames).Job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });
        var held = await PlayerRunner.ProbeAsync(
            Job("camera-oriented-constant", track, frames, PlanInterpolation.Constant).Job.ScriptPath,
            new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        for (int frame = 1; frame <= frames; frame++)
        {
            int key = (frame - 1) / step;
            double u = (frame - 1 - key * step) / (double)step;
            // Blender interpolates the four numbers one by one, each turn taken nearer the one before.
            var from = turns[key];
            var to = key + 1 < turns.Count ? Nearer(turns[key + 1], from) : from;
            var between = Normalized(new PlanQuaternion(
                from.W + (to.W - from.W) * u, from.X + (to.X - from.X) * u, from.Y + (to.Y - from.Y) * u, from.Z + (to.Z - from.Z) * u));
            AssertCamera(EyeAt(1), between.Forward, between.Up, linear.Frame(frame).Camera!, $"linear, frame {frame}");
            AssertCamera(EyeAt(1), from.Forward, from.Up, held.Frame(frame).Camera!, $"constant, frame {frame}");
        }
        // Half way from heading 170 to 190, straight down: heading 180, the top of the picture south.
        var half = linear.Frame(1 + step + step / 2).Camera!;
        Assert.True(AngleDegrees(new CaveVector3(0, -1, 0), half.Up) < UpTolerance, $"half way the top is {Vector(half.Up)}");
    }

    [BlenderFact]
    public async Task ATrackWithOrientationsOnSomePosesOnly_OrOneThatIsNotAUnitQuaternion_IsRefused()
    {
        var track = Enumerable.Range(1, 3).Select(frame =>
            new PlanCameraKey(Seconds(frame), EyeAt(frame), EyeAt(frame) + new CaveVector3(0, 0, -10)) { Orientation = PlanQuaternion.Identity }).ToList();
        var (_, job) = Job("camera-oriented-refused", track, 3);
        string planPath = Path.Combine(job.Folder, ScenePlanSerializer.FileName);
        string plan = File.ReadAllText(planPath);
        string last = ", \"orientation\": [1, 0, 0, 0] }\n    ]";
        Assert.Contains(last, plan);

        File.WriteAllText(planPath, plan.Replace(last, " }\n    ]"));
        Assert.Equal("camera.track[2].orientation: either every pose has an orientation or none has", await Refused(job));
        // As the validator says it: at the first pose that differs from the first.
        string one = ", \"orientation\": [1, 0, 0, 0]";
        File.WriteAllText(planPath, plan.Remove(plan.IndexOf(one, StringComparison.Ordinal), one.Length));
        Assert.Equal("camera.track[1].orientation: either every pose has an orientation or none has", await Refused(job));

        File.WriteAllText(planPath, plan.Replace(last, ", \"orientation\": [1, 0, 0.1, 0] }\n    ]"));
        Assert.StartsWith("camera.track[2].orientation: not a unit quaternion", await Refused(job));

        File.WriteAllText(planPath, plan.Replace(last, ", \"orientation\": [1, 0, 0] }\n    ]"));
        Assert.Equal("camera.track[2].orientation: four finite numbers are needed (w, x, y, z)", await Refused(job));
    }

    // ---- helpers ----

    private static async Task<string> Refused(PlayerJob job)
    {
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.True(exitCode == 64, $"exit code {exitCode}, expected 64; logs: {job.Folder}");
        Assert.Empty(PlayerRunner.Reported(log, "built"));
        return Assert.Single(PlayerRunner.Reported(log, "error"));
    }

    private static void AssertCamera(CaveVector3 eye, CaveVector3 forward, CaveVector3 up, ProbeCamera seen, string where)
    {
        AssertNear(eye, seen.Location, EyeTolerance, where + ": the eye");
        double offForward = AngleDegrees(forward, seen.Forward), offUp = AngleDegrees(up, seen.Up);
        Assert.True(offForward <= ForwardTolerance, $"{where}: the camera looks {offForward:F4}° off ({Vector(seen.Forward)} for {forward})");
        Assert.True(offUp <= UpTolerance, $"{where}: the top of the picture is {offUp:F4}° off ({Vector(seen.Up)} for {up})");
    }

    private static double Dot(PlanQuaternion a, PlanQuaternion b) => a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static PlanQuaternion Nearer(PlanQuaternion q, PlanQuaternion to)
        => Dot(q, to) < 0 ? new PlanQuaternion(-q.W, -q.X, -q.Y, -q.Z) : q;

    private static PlanQuaternion Normalized(PlanQuaternion q)
        => new(q.W / q.Length, q.X / q.Length, q.Y / q.Length, q.Z / q.Length);

    private static CaveVector3 Vector(double[] v) => new(v[0], v[1], v[2]);

    private static void AssertNear(CaveVector3 expected, double[] actual, double tolerance, string what)
        => Assert.True((expected - Vector(actual)).Length <= tolerance, $"{what}: expected {expected}, found {Vector(actual)}");

    /// <summary>The angle between two directions, from the cross product as well as the dot
    /// product: the arc cosine alone cannot tell a thousandth of a degree from nothing.</summary>
    private static double AngleDegrees(CaveVector3 a, double[] b)
    {
        var v = Vector(b);
        return Math.Atan2(a.Cross(v).Length, a.Dot(v)) * 180.0 / Math.PI;
    }
}
