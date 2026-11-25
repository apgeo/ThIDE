// The render service with a real Blender, on an invented cave: a movie with texts through the
// player's two stages, the same movie written again with another title from the frames that were
// kept, a draft, and the script generator when it is asked for. Opt-in like the other Blender
// tests (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using System.Diagnostics;
using Therion.Blender.Execution;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

public class BlenderRenderServiceBlenderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-service-blender-" + Guid.NewGuid().ToString("N"));

    public BlenderRenderServiceBlenderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string OutDir => Path.Combine(_root, "out");

    private RenderSource Cave()
    {
        string lox = Path.Combine(_root, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        return new RenderSource(new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
    }

    private SceneSpec Orbit(OverlaySpec overlays) => BuiltInPresets.OrbitShowcase().Spec with
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
        Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
        Labels = new LabelsSpec { Overlays = overlays },
        Animation = new AnimationSpec { Fps = 8, DurationSeconds = 1 },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 160, Height = 90, BaseName = "clip", OutputDirectory = OutDir },
    };

    private BlenderRenderService Service() => new(
        new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), Path.Combine(_root, "jobs"),
        blenderOverride: () => Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));

    [BlenderFact]
    public async Task AMovieWithTexts_IsBuiltByThePlayer_InTwoStages_AndWrittenAgainWithAnotherTitleFromItsFrames()
    {
        var spec = Orbit(new OverlaySpec
        {
            Title = "Invented Cave", ScaleBar = true, NorthArrow = true, Credits = "the invented team",
            EndCard = new EndCardSpec { Text = "The end", Seconds = 1 },
            Captions = [new CaptionSpec { Text = "Sala Mare", Start = 0.25, End = 0.75 }],
        });
        string frames = Path.Combine(_root, "frames");
        var ticks = new List<RenderProgress>();

        var result = await Service().RenderAsync(
            spec, Cave(), new RenderOptions { FramesDirectory = frames, KeepFrames = true }, new SyncProgress<RenderProgress>(ticks.Add));

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  log: {result.JobLogPath}");
        Assert.Equal(SceneBuilder.Player, result.Builder);
        Assert.Equal("CPU", result.Device);
        Assert.Equal(16, result.FrameCount);
        Assert.Equal(["clip.mp4", "clip.srt", "clip.vtt"], result.OutputPaths.Select(Path.GetFileName));
        Assert.Equal(frames, result.FramesDirectory);
        Assert.Equal(8, Directory.GetFiles(frames, "frame_*.png").Length);
        Assert.Contains(ticks, t => t.Phase == RenderPhase.Rendering && t.Frame == 8 && t.FrameCount == 8);
        Assert.Contains(ticks, t => t.Phase == RenderPhase.Composing && t.Frame == 16 && t.FrameCount == 16);
        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths[0]);
        Assert.Equal([160, 90], movie.Size);
        Assert.Equal(16, movie.Frames);
        Assert.Equal(8.0, movie.Fps, 3);
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "jobs")));

        // Another title: the second stage alone, in a fraction of the time, and another movie.
        byte[] first = await File.ReadAllBytesAsync(result.OutputPaths[0]);
        var rendered = Directory.GetFiles(frames, "frame_*.png").ToDictionary(f => f, File.GetLastWriteTimeUtc);
        ticks.Clear();
        var clock = Stopwatch.StartNew();
        var retitled = await Service().RenderAsync(
            spec with { Labels = new LabelsSpec { Overlays = spec.Labels.Overlays with { Title = "Another Name" } } }, Cave(),
            new RenderOptions { FramesDirectory = frames, ComposeOnly = true }, new SyncProgress<RenderProgress>(ticks.Add));
        clock.Stop();

        Assert.True(retitled.Succeeded, $"{retitled.FailureKind}: {retitled.ErrorMessage}\n  log: {retitled.JobLogPath}");
        Assert.DoesNotContain(ticks, t => t.Phase == RenderPhase.Rendering && t.Frame is not null);
        Assert.NotEqual(first, await File.ReadAllBytesAsync(retitled.OutputPaths[0]));
        Assert.All(rendered, f => Assert.Equal(f.Value, File.GetLastWriteTimeUtc(f.Key)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"writing the movie again took {clock.Elapsed.TotalSeconds:F0} s");

        // A changed camera is another scene: its frames are not these, and the service says so.
        var moved = await Service().RenderAsync(
            spec with { Camera = spec.Camera with { FocalLength = 50 } }, Cave(), new RenderOptions { FramesDirectory = frames, ComposeOnly = true });
        Assert.Equal((false, RenderFailureKind.ScriptError), (moved.Succeeded, moved.FailureKind));
        Assert.Contains("were rendered from another scene", moved.ErrorMessage);
    }

    [BlenderFact]
    public async Task ASpecWithAnnotations_IsRenderedByThePlayer_WithItsPhotoInTheFramesItIsThereFor_AndInADraft()
    {
        string picture = Player.TestImages.Write(Path.Combine(_root, "trip"), "the flag.png", Player.TestImages.FlagPng());
        var spec = Orbit(new OverlaySpec()) with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 4, Denoise = false, Gpu = GpuMode.CpuOnly },
            // A grey cave: the only red and blue in the picture are the flag's.
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.5, 0.5, 0.5) },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 160, Height = 90, BaseName = "clip", OutputDirectory = OutDir },
            Annotations =
            [
                // A large picture above the middle of the passage, from the third frame to the sixth.
                new AnnotationSpec
                {
                    Id = "flag", Kind = AnnotationKind.Photo, Station = "main6", Image = picture, Size = 40, Offset = new CaveVector3(0, 0, 25),
                    Framed = true, From = 0.25, To = 0.75, Fade = 0,
                },
                new AnnotationSpec { Kind = AnnotationKind.Callout, Station = "main0", Text = "Intrarea" },
                // (White, not a marker's own orange-red: the flag's red is what is counted below.)
                new AnnotationSpec { Id = "end", Kind = AnnotationKind.Marker, Station = "main12", Within = 5000, Color = new ColorRgb(1, 1, 1) },
            ],
        };

        var result = await Service().RenderAsync(spec, Cave(), new RenderOptions());

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  log: {result.JobLogPath}");
        Assert.Equal((SceneBuilder.Player, 8), (result.Builder, result.FrameCount));
        Assert.Empty(result.Warnings);
        Assert.Empty(result.LeftOut);
        Assert.Equal(8, result.OutputPaths.Length);
        var red = new ColorRgb(1, 0, 0);
        var blue = new ColorRgb(0, 0, 1);
        for (int f = 1; f <= 8; f++)
        {
            var frame = Player.RenderedPicture.Read(result.OutputPaths[f - 1]);
            var (reds, blues) = (frame.Of(red), frame.Of(blue));
            // Two eighths of a second in, and gone at six eighths: frames 3 to 6.
            if (f is >= 3 and <= 6)
                Assert.True(reds.Count > 60 && blues.Count > 60 && reds.X < blues.X, $"frame {f}: the flag's red is {reds} and its blue {blues}");
            else
                Assert.True(reds.Count + blues.Count == 0, $"frame {f}: the flag is there: {reds}, {blues}");
        }
        // The copies of the photo went with the job's folder; the picture itself stays.
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "jobs")));
        Assert.True(File.Exists(picture));

        // A draft has it too, as a mosaic, and says so.
        var draft = await Service().RenderAsync(spec, Cave(), new RenderOptions { Draft = true });
        Assert.True(draft.Succeeded, $"{draft.FailureKind}: {draft.ErrorMessage}\n  log: {draft.JobLogPath}");
        Assert.Equal(["the draft shows a photo as a mosaic of its colours, 48 tiles along its longer side"], draft.Warnings);
        var drafted = Player.RenderedPicture.Read(draft.OutputPaths[3]);
        Assert.True(drafted.Of(red).Count > 15 && drafted.Of(blue).Count > 15, $"the draft's flag is {drafted.Of(red)} and {drafted.Of(blue)}");
        Assert.Equal(0, Player.RenderedPicture.Read(draft.OutputPaths[0]).Of(red).Count);
    }

    [BlenderFact]
    public async Task ADraft_IsTheQuickLook_AndTheScriptGeneratorStillRendersWhenAskedFor()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" });

        var draft = await Service().RenderAsync(spec, Cave(), new RenderOptions { Draft = true });
        Assert.True(draft.Succeeded, $"{draft.FailureKind}: {draft.ErrorMessage}\n  log: {draft.JobLogPath}");
        Assert.Equal((SceneBuilder.Player, "WORKBENCH"), (draft.Builder, draft.Device));
        Assert.Equal("clip-draft.mp4", Path.GetFileName(Assert.Single(draft.OutputPaths)));
        var movie = await SceneProbeRunner.ReadMovieAsync(draft.OutputPaths[0]);
        Assert.Equal([80, 44], movie.Size);   // half of 160 x 90, with even sides
        Assert.Equal(8, movie.Frames);

        var generated = await Service().RenderAsync(spec, Cave(), new RenderOptions { Builder = SceneBuilder.Generator });
        Assert.True(generated.Succeeded, $"{generated.FailureKind}: {generated.ErrorMessage}\n  log: {generated.JobLogPath}");
        Assert.Equal(SceneBuilder.Generator, generated.Builder);
        Assert.Equal("clip.mp4", Path.GetFileName(Assert.Single(generated.OutputPaths)));
        Assert.Equal(8, (await SceneProbeRunner.ReadMovieAsync(generated.OutputPaths[0])).Frames);
    }
}
