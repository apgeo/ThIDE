// The built-in composite presets in a real Blender: a draft of each, made into a film of the tiny
// invented tour cave by the render service (the tour of Grand tour, the passage surveyed last of
// Exploration history), with a segment of frames for every shot and a movie as long as they make.
// Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class CompositeBlenderTests
{
    private const int Fps = 2;

    [BlenderTheory]
    [InlineData("Showcase")]
    [InlineData("Grand tour")]
    [InlineData("Exploration history")]
    public async Task ABuiltInComposite_IsDraftedForATinyCave_OneSegmentAShot(string name)
    {
        string root = PlayerRunner.NewCase("composite-" + name.Replace(' ', '-').ToLowerInvariant());
        // Its main passage and one side passage surveyed in 1990, the other side passage in 2001.
        var cave = RevealFixtures.Dated(TourCave.BuildTiny(), (_, i) => i >= 8 ? new DateOnly(2001, 5, 1) : new DateOnly(1990, 8, 1));
        string model = Path.Combine(root, "tiny.3d");
        File.WriteAllBytes(model, RevealFixtures.As3d(cave));

        var composite = BuiltInComposites.ByName(name)!;
        var storyboard = composite.Storyboard with
        {
            Fps = Fps,
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "film", OutputDirectory = Path.Combine(root, "out") },
        };
        // The shots it becomes, as the service will make them of the file.
        var geometry = GeometryStage.Build(CaveModelReader.ReadFile(model));
        var shots = StoryboardExpander.Expand(storyboard, geometry).Storyboard.Shots;
        // The tour of a cave this small is its main passage: its side passages are under the 50 m it skips.
        if (name == "Grand tour") Assert.All(shots.Skip(1), s => Assert.Null(s.Camera!.Route!.Branches));
        else Assert.Equal(storyboard.Shots.Count, shots.Count);

        var service = new BlenderRenderService(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), Path.Combine(root, "jobs"),
            blenderOverride: () => Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));
        var source = new RenderSource(new ResolvedModelSource { Path = model, Format = CaveSourceFormat.Survex3d, Kind = ModelSourceKind.ExternalFile });
        string kept = Path.Combine(root, "frames");
        var result = await service.RenderStoryboardAsync(storyboard, source, new RenderOptions { Draft = true, FramesDirectory = kept, KeepFrames = true });

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  log: {result.JobLogPath}");
        var segments = Directory.GetDirectories(kept).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(Enumerable.Range(0, shots.Count).Select(StoryboardPlanner.SegmentId), segments);
        int rendered = 0, overlap = 0;
        for (int i = 0; i < shots.Count; i++)
        {
            int frames = Directory.GetFiles(Path.Combine(kept, segments[i]!), "frame_*.png").Length;
            Assert.Equal((int)Math.Round(shots[i].Seconds!.Value * Fps), frames);
            rendered += frames;
            if (i > 0 && shots[i].In is { Kind: TransitionKind.Dissolve } dissolve) overlap += (int)Math.Round(dissolve.Seconds * Fps);
        }
        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths.First(p => p.EndsWith(".mp4", StringComparison.Ordinal)));
        Assert.Equal(result.FrameCount, movie.Frames);
        // The shots end to end, less what dissolves overlap, and an end card's frames after them.
        Assert.True(movie.Frames >= rendered - overlap, $"{movie.Frames} frames for {rendered} rendered, {overlap} overlapping");
        if (name == "Exploration history")
            Assert.Contains(result.Warnings, w => w.Contains("surveyed last (2001)", StringComparison.Ordinal));
    }
}
