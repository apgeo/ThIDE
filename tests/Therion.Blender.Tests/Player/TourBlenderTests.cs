// A tour in a real Blender: a draft of the whole film for a tiny invented cave with two side
// passages, through the render service. Every shot is a segment with frames of its own, and the
// film is as long as the tour says. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1;
// THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class TourBlenderTests
{
    [BlenderFact]
    public async Task ATourOfATinyCave_IsDrafted_OneSegmentAShot_AsLongAsItSays()
    {
        string root = PlayerRunner.NewCase("tour-draft");
        string lox = Path.Combine(root, "tiny.lox");
        LoxWriter.WriteFile(TourCave.BuildTiny(), lox);
        // The tour of the file as the service will read it.
        var tour = TourBuilder.Build(GeometryStage.Build(CaveModelReader.ReadFile(lox)), new TourSpec
        {
            MinLength = 5, Fps = 2, TourSeconds = 24, Language = "en",
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "tour", OutputDirectory = Path.Combine(root, "out") },
        });
        Assert.Empty(StoryboardValidator.Validate(tour.Storyboard));
        Assert.Equal(["Main passage 1", "B1", "Main passage 2", "B2", "Main passage 3"], tour.Storyboard.Shots.Select(s => s.Name));
        Assert.True(tour.Seconds <= 24.0);
        int frames = (int)Math.Round(tour.Seconds * 2);

        var service = new BlenderRenderService(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), Path.Combine(root, "jobs"),
            blenderOverride: () => Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));
        var source = new RenderSource(new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
        string kept = Path.Combine(root, "frames");
        var result = await service.RenderStoryboardAsync(
            tour.Storyboard, source, new RenderOptions { Draft = true, FramesDirectory = kept, KeepFrames = true });

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  log: {result.JobLogPath}");
        Assert.Equal(["s01", "s02", "s03", "s04", "s05"], Directory.GetDirectories(kept).Select(Path.GetFileName).Order());
        Assert.Equal(frames, result.FrameCount);
        Assert.Equal(frames, Directory.GetFiles(kept, "frame_*.png", SearchOption.AllDirectories).Length);
        // The second shot is the first side passage: its frames are its seconds.
        Assert.Equal(
            (int)Math.Round(tour.Storyboard.Shots[1].Seconds!.Value * 2), Directory.GetFiles(Path.Combine(kept, "s02"), "frame_*.png").Length);
        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths.First(p => p.EndsWith(".mp4", StringComparison.Ordinal)));
        Assert.Equal(frames, movie.Frames);
    }
}
