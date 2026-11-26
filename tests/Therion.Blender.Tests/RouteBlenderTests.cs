// A route camera in a real Blender, on the invented Small cave: out along the main passage, into
// the side passage to its end and back. The camera is where the plan puts it on every frame, and
// a short clip of it renders. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1;
// THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Tests.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests;

public class RouteBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("route-small", InventedCave.Build(InventedCaveSize.Small)));

    private static SceneSpec Route(int fps, double seconds, OutputSpec output) => PlanFixtures.Preset("Full flythrough") with
    {
        Labels = new LabelsSpec(),
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
        Camera = new CameraSpec
        {
            Template = CameraTemplate.Route,
            Route = new RouteParams { From = "main2", Via = ["side3"], To = "main9", HoldSeconds = 0.5 },
        },
        Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
        Output = output,
    };

    [BlenderFact]
    public async Task TheCameraFliesTheRoute_PoseByPose()
    {
        var (plan, job) = PlayerRunner.WriteJob("route-track", Small.Value, Route(10, 6, new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64 }));
        Assert.Equal(60, plan.Camera.Track.Count);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = Enumerable.Range(1, 60).ToList() });
        Assert.All(plan.Camera.Track, pose =>
        {
            int frame = ScenePlanValidator.FrameAt(pose.T, plan.Timeline.Fps);
            var seen = report.Frame(frame).Camera!;
            var location = new CaveVector3(seen.Location[0], seen.Location[1], seen.Location[2]);
            Assert.True((location - pose.Eye).Length < 1e-4, $"frame {frame}: position {location}, planned {pose.Eye}");
        });
    }

    [BlenderFact]
    public async Task ARouteClipRenders()
    {
        var spec = Route(10, 0.6, new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "route" });
        var (plan, job) = PlayerRunner.WriteJob("route-render", Small.Value, spec);
        var result = await new BlenderRunner(new RealBlenderProcessLauncher()).RunAsync(
            SceneProbeRunner.Blender,
            new RenderJob(job.ScriptPath, job.Folder, plan.Timeline.FrameCount, spec.Output with { OutputDirectory = job.Folder }));

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  logs: {job.Folder}");
        Assert.Equal(6, result.FrameCount);
        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths[0]);
        Assert.Equal(6, movie.Frames);
    }
}
