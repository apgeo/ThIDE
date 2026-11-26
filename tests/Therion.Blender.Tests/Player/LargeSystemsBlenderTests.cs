// Large systems, frame ranges and the render worker, in a real Blender, on invented caves: the walls
// in parts render as the one mesh does; a render of frames in two ranges gives the frames an
// unbroken render gives, pixel for pixel, also with two Blenders on one frames folder at once; the
// render data Cycles keeps from frame to frame changes no frame; and a worker renders a bundle it
// takes from its inbox with its own player, stops on a cancel file and goes on with its frames.
// Opt-in: THIDE_BLENDER_SMOKE=1 (and THIDE_BLENDER_E2E=1 for the worker); THIDE_BLENDER_PATH picks
// the Blender. The numbers are written to the test output.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;
using Xunit.Abstractions;

namespace Therion.Blender.Tests.Player;

/// <summary>A real-Blender test of the whole render worker: needs THIDE_BLENDER_SMOKE=1 and THIDE_BLENDER_E2E=1.</summary>
public sealed class BlenderE2EFactAttribute : FactAttribute
{
    public BlenderE2EFactAttribute()
    {
        if (!BlenderFactAttribute.Enabled || Environment.GetEnvironmentVariable("THIDE_BLENDER_E2E") != "1")
            Skip = $"Needs a local Blender; set {BlenderFactAttribute.OptInVariable}=1 and THIDE_BLENDER_E2E=1 to run.";
    }
}

public sealed class LargeSystemsBlenderTests(ITestOutputHelper output)
{
    private static readonly Lazy<ProbeAssets> Large = new(() =>
        SceneProbeRunner.Prepare("large-systems", InventedCave.Build(InventedCaveSize.Large)));

    private static readonly Lazy<ProbeAssets> Medium = new(() =>
        SceneProbeRunner.Prepare("large-medium", InventedCave.Build(InventedCaveSize.Medium)));

    private static SceneSpec Orbit(int frames, int width = 160, int height = 90) => PlanFixtures.Preset("Orbit showcase") with
    {
        Labels = new LabelsSpec(),
        Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
        Animation = new AnimationSpec { Fps = 8, DurationSeconds = frames / 8.0 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = width, Height = height, BaseName = "clip" },
    };

    private static SceneSpec Cycles(SceneSpec spec, int samples = 4) => spec with
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = samples, Denoise = false, Gpu = GpuMode.CpuOnly },
    };

    private static ScenePlan Quick(ScenePlan plan) => ScenePlanBuilder.Draft(plan, scale: 1.0) with { Output = plan.Output };

    private static byte[] Pixels(string path) => Png.Decode(File.ReadAllBytes(path)).Rgba;

    private static string Frame(PlayerJob job, string folder, int number) => Path.Combine(job.Folder, folder, $"frame_{number:0000}.png");

    private static async Task<string> Run(PlayerJob job, params string[] arguments)
    {
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, arguments);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return log;
    }

    // ---- parts ----

    [BlenderFact]
    public async Task TheWallsInParts_RenderAsTheOneMeshDoes()
    {
        var assets = Large.Value;
        var parts = WallParts.Split(assets.Conversion.Geometry);
        var files = new List<WallPartFile>();
        foreach (var part in parts)
        {
            string path = Path.Combine(assets.Directory, WallPartFile.FileName(part.Index));
            Therion.CaveGeometry.Writers.PlyWriter.WriteFile(part.Mesh, path);
            files.Add(new WallPartFile(part.Index, path, part.Bounds, part.TriangleCount, part.Branches));
        }
        Assert.True(files.Count > 5, $"{files.Count} parts");

        foreach (var (engine, spec) in new[] { ("draft", Orbit(2)), ("cycles", Cycles(Orbit(2), samples: 2)) })
        {
            Func<ScenePlan, ScenePlan>? change = engine == "draft" ? Quick : null;
            var (_, whole) = PlayerRunner.WriteJob($"parts-whole-{engine}", assets, spec, change);
            var (partsPlan, inParts) = PlayerRunner.WriteJob($"parts-split-{engine}", assets,
                spec with { Source = spec.Source with { Parts = files } }, change);
            Assert.Equal(files.Count, partsPlan.Models.Count(m => m.Role == ScenePlanBuilder.WallsRole));

            var clock = Stopwatch.StartNew();
            string wholeLog = await Run(whole);
            var wholeTime = clock.Elapsed;
            clock.Restart();
            string partsLog = await Run(inParts);
            var partsTime = clock.Elapsed;
            Assert.Equal(PlayerRunner.Reported(wholeLog, "triangles"), PlayerRunner.Reported(partsLog, "triangles"));

            for (int frame = 1; frame <= 2; frame++)
            {
                var a = Pixels(Path.Combine(whole.Folder, $"clip_{frame:0000}.png"));
                var b = Pixels(Path.Combine(inParts.Folder, $"clip_{frame:0000}.png"));
                Assert.Equal(a.Length, b.Length);
                int differing = 0, largest = 0;
                for (int i = 0; i < a.Length; i += 4)
                {
                    int d = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
                    if (d > 2) differing++;
                    largest = Math.Max(largest, d);
                }
                output.WriteLine($"{engine} frame {frame}: {differing} of {a.Length / 4} pixels differ by more than 2 (largest {largest}); "
                                 + $"one mesh {wholeTime.TotalSeconds:0.0} s, {files.Count} parts {partsTime.TotalSeconds:0.0} s");
                Assert.True(differing <= a.Length / 4 / 500, $"{engine} frame {frame}: {differing} pixels differ");
            }
        }
    }

    // ---- frame ranges ----

    /// <summary>The medium cave with its leads as markers that grow with their distance from the
    /// camera: the driver that once read the frame before.</summary>
    private static SceneSpec Markers(int frames) => Cycles(Orbit(frames, 96, 64)) with
    {
        Labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, Sizing = LeadMarkerSizing.CameraDistance } },
    };

    [BlenderFact]
    public async Task FramesInTwoRanges_AreTheFramesOfAnUnbrokenRender_EvenWithTwoBlendersAtOnce()
    {
        var (plan, job) = PlayerRunner.WriteJob("frame-ranges", Medium.Value, Markers(8));
        Assert.Contains(plan.Labels!.Markers!.Items, m => m.Role == ScenePlanBuilder.LeadMarkerRole);

        await Run(job, "--stage", "frames", "--frames", Path.Combine(job.Folder, "whole"));
        // Two Blenders on one folder at once, each with a range of its own.
        string ranges = Path.Combine(job.Folder, "ranges");
        var first = PlayerRunner.RunAsync(job.ScriptPath, "--stage", "frames", "--frames", ranges, "--frame-range", "1-4");
        var second = PlayerRunner.RunAsync(job.ScriptPath, "--stage", "frames", "--frames", ranges, "--frame-range", "5-8");
        foreach (var (exitCode, log) in await Task.WhenAll(first, second))
        {
            Assert.True(exitCode == 0, PlayerRunner.Reported(log, "error").FirstOrDefault());
            Assert.Single(PlayerRunner.Reported(log, "frame-range"));
        }

        int identical = 0;
        for (int frame = 1; frame <= 8; frame++)
        {
            var whole = File.ReadAllBytes(Frame(job, "whole", frame));
            var ranged = File.ReadAllBytes(Frame(job, "ranges", frame));
            Assert.True(Pixels(Frame(job, "whole", frame)).AsSpan().SequenceEqual(Pixels(Frame(job, "ranges", frame))), $"frame {frame} differs");
            if (whole.AsSpan().SequenceEqual(ranged)) identical++;
        }
        output.WriteLine($"8 of 8 frames have the same pixels; {identical} of 8 files are the same bytes");
        Assert.Equal(File.ReadAllText(Path.Combine(job.Folder, "whole", "frames.json")), File.ReadAllText(Path.Combine(ranges, "frames.json")));
        Assert.False(File.Exists(Path.Combine(ranges, "frames.lock")));

        // A range again: nothing to render, and the other range's frames are left alone.
        string log2 = await Run(job, "--stage", "frames", "--frames", ranges, "--frame-range", "1-4");
        Assert.Equal(["4/4"], PlayerRunner.Reported(log2, "resumed"));
        Assert.Equal(8, Directory.GetFiles(ranges, "frame_*.png").Length);
        // The output from all of them, as from an unbroken render.
        await Run(job, "--stage", "compose", "--frames", ranges);
        Assert.True(File.Exists(Path.Combine(job.Folder, "clip_0008.png")));

        // A range is refused where it means nothing.
        var (bad, badLog) = await PlayerRunner.RunAsync(job.ScriptPath, "--stage", "frames", "--frames", ranges, "--frame-range", "5-9");
        Assert.NotEqual(0, bad);
        Assert.Contains("is not a range of frames 1 to 8", PlayerRunner.Reported(badLog, "error").Single());
        (bad, badLog) = await PlayerRunner.RunAsync(job.ScriptPath, "--frame-range", "1-4");
        Assert.NotEqual(0, bad);
        Assert.Contains("goes with --stage frames", PlayerRunner.Reported(badLog, "error").Single());
    }

    [BlenderFact]
    public async Task TheServiceRendersARange_IntoItsFramesFolder_AndTheMovieFromAllOfThem()
    {
        var assets = Medium.Value;
        var spec = Orbit(6, 96, 64) with
        {
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "ranged" },
        };
        string root = PlayerRunner.NewCase("service-ranges");
        var service = new BlenderRenderService(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), root,
            blenderOverride: () => SceneProbeRunner.Blender.Path);
        var source = new RenderSource(assets.Conversion.Manifest.Source);
        string frames = Path.Combine(root, "frames");
        foreach (var range in new[] { new FrameRange(4, 6), new FrameRange(1, 3) })
        {
            var result = await service.RenderAsync(spec with { Output = spec.Output with { OutputDirectory = root } }, source,
                new RenderOptions { Draft = true, FramesDirectory = frames, FrameRange = range });
            Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}");
            Assert.Equal(frames, result.FramesDirectory);
            Assert.False(File.Exists(Path.Combine(root, "ranged-draft.mp4")));
        }
        var movie = await service.RenderAsync(spec with { Output = spec.Output with { OutputDirectory = root } }, source,
            new RenderOptions { Draft = true, FramesDirectory = frames, ComposeOnly = true });
        Assert.True(movie.Succeeded, $"{movie.FailureKind}: {movie.ErrorMessage}");
        Assert.Equal(6, (await SceneProbeRunner.ReadMovieAsync(Assert.Single(movie.OutputPaths))).Frames);
    }

    // ---- persistent data ----

    [BlenderFact]
    public async Task TheRenderDataCyclesKeeps_ChangesNoFrame()
    {
        var spec = Cycles(Orbit(6, 160, 90), samples: 8) with { Labels = Markers(6).Labels };
        var (_, job) = PlayerRunner.WriteJob("persistent-data", Medium.Value, spec);
        var clock = Stopwatch.StartNew();
        string kept = await Run(job, "--stage", "frames", "--frames", Path.Combine(job.Folder, "kept"));
        var keptTime = clock.Elapsed;
        clock.Restart();
        string fresh = await Run(job, "--stage", "frames", "--frames", Path.Combine(job.Folder, "fresh"), "--no-persistent-data");
        var freshTime = clock.Elapsed;
        Assert.Equal(["1"], PlayerRunner.Reported(kept, "persistent-data"));
        Assert.Empty(PlayerRunner.Reported(fresh, "persistent-data"));
        for (int frame = 1; frame <= 6; frame++)
            Assert.True(Pixels(Frame(job, "kept", frame)).AsSpan().SequenceEqual(Pixels(Frame(job, "fresh", frame))), $"frame {frame} differs");
        output.WriteLine($"6 frames, the same pixels: kept {keptTime.TotalSeconds:0.0} s, fresh {freshTime.TotalSeconds:0.0} s");
    }

    // ---- the render worker ----

    private static string Lox(string folder, InventedCaveSize size)
    {
        string lox = Path.Combine(folder, size + ".lox");
        LoxWriter.WriteFile(InventedCave.Build(size), lox);
        return lox;
    }

    private static ResolvedModelSource Source(string lox) => new() { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };

    private static RenderWorker Worker(string root) => new(
        new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
        new RenderWorkerOptions
        {
            Inbox = Path.Combine(root, "inbox"), Outbox = Path.Combine(root, "outbox"), Scratch = Path.Combine(root, "scratch"),
            Poll = TimeSpan.FromMilliseconds(200),
        },
        () => SceneProbeRunner.Blender.Path);

    [BlenderE2EFact]
    public async Task TheWorker_RendersABundle_WithItsOwnPlayer_AndHandsBackTheMovie()
    {
        string root = PlayerRunner.NewCase("worker");
        var spec = Orbit(8, 96, 64) with
        {
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented", Captions = [new CaptionSpec { Start = 0, End = 0.5, Text = "hello" }] } },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "clip" },
        };
        var content = RenderBundleWriter.FromModel(Source(Lox(root, InventedCaveSize.Small)), spec, Path.Combine(root, "work"));
        content = content with { Plan = ScenePlanBuilder.Draft(content.Plan, scale: 1.0) with { Output = content.Plan.Output } };
        Directory.CreateDirectory(Path.Combine(root, "inbox"));
        string zip = Path.Combine(root, "inbox", "tour.zip");
        RenderBundleWriter.WriteZip(content, zip);
        // The bundle's own player is a trap: if it ran, it would leave a mark.
        string mark = Path.Combine(root, "bundle-code-ran");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            foreach (string path in new[] { "tour/render.py", "tour/thide_player/core.py" })
            {
                archive.GetEntry(path)!.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write("open(r'" + mark + "', 'w').write('ran')\n");
            }

        var record = await Worker(root).ProcessAsync(zip, CancellationToken.None);
        Assert.True(record.State == RenderJobState.Succeeded, $"{record.Result?.FailureKind}: {record.Result?.Error}");
        Assert.False(File.Exists(mark), "the bundle's own code ran");
        string outbox = Path.Combine(root, "outbox");
        Assert.Equal(["tour.log", "tour.mp4", "tour.srt", "tour.status.json", "tour.vtt"],
            Directory.GetFiles(outbox).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(8, (await SceneProbeRunner.ReadMovieAsync(Path.Combine(outbox, "tour.mp4"))).Frames);
        var status = RenderJobFile.ReadOne(File.ReadAllText(Path.Combine(outbox, "tour.status.json")))!;
        Assert.Equal(RenderJobState.Succeeded, status.State);
        Assert.Equal([Path.Combine(outbox, "tour.mp4"), Path.Combine(outbox, "tour.srt"), Path.Combine(outbox, "tour.vtt")], status.Result!.Outputs);
        Assert.False(File.Exists(zip));
        Assert.False(Directory.Exists(Path.Combine(root, "scratch", "tour")));
    }

    [BlenderE2EFact]
    public async Task AJobThatIsCancelled_StopsBlender_KeepsItsFrames_AndGoesOnWithThem()
    {
        string root = PlayerRunner.NewCase("worker-cancel");
        var storyboard = new Storyboard
        {
            Fps = 8,
            Defaults = new StoryboardDefaults { Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 16, Denoise = false, Gpu = GpuMode.CpuOnly } },
            Shots =
            [
                new ShotSpec { Preset = "Orbit showcase", Seconds = 4 },
                new ShotSpec { Preset = "Orbit showcase", Seconds = 2, In = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.5 } },
            ],
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 160, Height = 90, BaseName = "film" },
        };
        var content = RenderBundleWriter.FromStoryboard(Source(Lox(root, InventedCaveSize.Medium)), storyboard, Path.Combine(root, "work"));
        Directory.CreateDirectory(Path.Combine(root, "inbox"));
        string zip = Path.Combine(root, "inbox", "film.zip");
        RenderBundleWriter.WriteZip(content, zip);
        string statusFile = Path.Combine(root, "outbox", "film" + RenderWorker.StatusSuffix);
        string cancel = Path.Combine(root, "inbox", "film" + RenderWorker.CancelSuffix);

        var worker = Worker(root);
        var running = worker.ProcessAsync(zip, CancellationToken.None);
        // Stop it once a few frames of the first shot are there.
        for (int i = 0; i < 600; i++)
        {
            if (File.Exists(statusFile) && RenderJobFile.ReadOne(File.ReadAllText(statusFile)) is { Progress: { Segment: 1, Frame: >= 4 } }) break;
            await Task.Delay(100);
        }
        var seen = RenderJobFile.ReadOne(File.ReadAllText(statusFile))!;
        Assert.Equal((1, 2), (seen.Progress!.Segment, seen.Progress.SegmentCount));
        Assert.NotNull(seen.Progress.SecondsPerFrame);
        File.WriteAllText(cancel, "");
        var stopped = await running.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(RenderJobState.Cancelled, stopped.State);
        Assert.True(File.Exists(zip), "a stopped job keeps its bundle");
        int kept = Directory.GetFiles(Path.Combine(root, "scratch", "film", "frames", "s01"), "frame_*.png").Length;
        Assert.InRange(kept, 3, 31);
        Assert.Empty(worker.Pending());

        File.Delete(cancel);
        Assert.Equal([zip], worker.Pending());
        var done = await worker.ProcessAsync(zip, CancellationToken.None);
        Assert.True(done.State == RenderJobState.Succeeded, $"{done.Result?.FailureKind}: {done.Result?.Error}");
        string log = File.ReadAllText(Path.Combine(root, "outbox", "film.log"));
        Assert.Contains("THIDE:resumed=", log);
        output.WriteLine($"stopped with {kept} frames of shot 1; went on: {string.Join(", ", PlayerRunner.Reported(log, "resumed"))}; "
                         + $"{done.Result!.SecondsPerFrame:0.###} s a frame");
        Assert.Equal(content.Film!.Movie.OutputFrameCount, (await SceneProbeRunner.ReadMovieAsync(Path.Combine(root, "outbox", "film.mp4"))).Frames);
    }
}
