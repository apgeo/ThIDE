// Which of the two scene builders the render service uses, and what it does with each: the
// player with a scene plan (in two stages when the scene has texts or its frames are asked for),
// or the script generator with a script. On invented caves, with a stand-in for Blender that
// records how it was started and writes the files a render would; no Blender runs here.

using System.Runtime.CompilerServices;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

public class BlenderRenderServiceBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-builders-" + Guid.NewGuid().ToString("N"));

    public BlenderRenderServiceBuilderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private RenderSource Cave(InventedCaveSize size = InventedCaveSize.Small)
    {
        string lox = Path.Combine(_root, size + ".lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(size), lox);
        return new RenderSource(new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
    }

    private string OutDir => Path.Combine(_root, "out");

    private SceneSpec Orbit(OverlaySpec? overlays = null) => BuiltInPresets.OrbitShowcase().Spec with
    {
        Labels = new LabelsSpec { Overlays = overlays ?? new OverlaySpec() },
        Animation = new AnimationSpec { Fps = 4, DurationSeconds = 1 },   // 4 frames
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 64, Height = 64, BaseName = "clip", OutputDirectory = OutDir },
    };

    private static SceneSpec WithHdri(SceneSpec spec) => spec with { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" } };

    // ---- a stand-in for Blender ----

    /// <summary>One start of the stand-in: its arguments after "--", what lay in the job folder,
    /// and the plan it was to play (null for a generated script).</summary>
    private sealed record Start(IReadOnlyList<string> Arguments, string Script, IReadOnlyList<string> JobFiles, ScenePlan? Plan);

    private sealed class FakeBlender : IBlenderProcessLauncher
    {
        public List<Start> Starts { get; } = [];

        /// <summary>The stage that fails, when one is to.</summary>
        public string? FailAt { get; init; }

        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            int dashes = args.ToList().IndexOf("--");
            var own = dashes < 0 ? [] : args.Skip(dashes + 1).ToList();
            string script = File.ReadAllText(args[args.ToList().IndexOf("--python") + 1]);
            string planPath = Path.Combine(cwd, "scene-plan.json");
            var plan = File.Exists(planPath) ? ScenePlanSerializer.ReadFile(planPath) : null;
            var files = Directory.EnumerateFileSystemEntries(cwd).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
            Starts.Add(new Start(own, script, files!, plan));

            string stage = Value(own, "--stage") ?? "all";
            if (stage == FailAt) return new FakeProcess(["THIDE:error=the stand-in was told to fail"], 64);
            var lines = new List<string> { "THIDE:device=CPU" };
            if (plan is null)
            {
                // A generated script: it writes its video where its OUT_DIR says.
                File.WriteAllBytes(Path.Combine(OutputDirOf(script), "clip.mp4"), new byte[64]);
                lines.Add("THIDE:frame=4/4");
            }
            else
            {
                int frames = plan.Output.Kind == PlanOutputKind.Still ? 1 : plan.Timeline.FrameCount;
                if (stage == "frames")
                {
                    string folder = Value(own, "--frames")!;
                    Directory.CreateDirectory(folder);
                    for (int n = 1; n <= frames; n++) File.WriteAllBytes(Path.Combine(folder, $"frame_{n:0000}.png"), new byte[32]);
                    lines.Add($"THIDE:frame={frames}/{frames}");
                }
                else
                {
                    if (stage == "compose")
                    {
                        lines.Add("THIDE:phase=compose");
                        frames = plan.OutputFrameCount;
                        if (!own.Contains("--keep-frames")) Directory.Delete(Value(own, "--frames")!, recursive: true);
                    }
                    string extension = plan.Output.Kind == PlanOutputKind.Video ? ".mp4" : ".png";
                    File.WriteAllBytes(Path.Combine(plan.Output.Directory, plan.Output.BaseName + extension), new byte[64]);
                    lines.Add($"THIDE:frame={frames}/{frames}");
                }
            }
            lines.Add("THIDE:done=1");
            return new FakeProcess([.. lines], 0);
        }

        private static string? Value(IReadOnlyList<string> arguments, string name)
        {
            int at = arguments.ToList().IndexOf(name);
            return at < 0 ? null : arguments[at + 1];
        }

        private static string OutputDirOf(string script)
        {
            string line = script.Split('\n').First(l => l.StartsWith("OUT_DIR = ", StringComparison.Ordinal));
            return System.Text.Json.JsonSerializer.Deserialize<string>(line["OUT_DIR = ".Length..].Trim())!;
        }
    }

    private sealed class FakeProcess(string[] lines, int exitCode) : IBlenderProcess
    {
        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (string line in lines) { ct.ThrowIfCancellationRequested(); yield return line; await Task.Yield(); }
        }

        public int ExitCode => exitCode;
        public void KillTree() { }
        public void Dispose() { }
    }

    private sealed class FakeProbe : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => new BlenderVersion(4, 5, 0);
    }

    private string JobRoot => Path.Combine(_root, "jobs");

    private BlenderRenderService Service(FakeBlender blender, long freeBytes = 50_000_000_000L)
        => new(new BlenderLocator(new FakeProbe(), () => ["/fake/blender"]), new BlenderRunner(blender, _ => freeBytes), JobRoot);

    // ---- which builder ----

    [Fact]
    public void Choose_TakesThePlayerWhenItCarriesTheSpec_AndTheGeneratorWhenItDoesNot()
    {
        var carried = SceneBuilders.Choose(Orbit());
        Assert.Equal((SceneBuilder.Player, null), (carried.Builder, carried.Refusal));
        Assert.Empty(carried.NotCarried);

        var hdri = SceneBuilders.Choose(WithHdri(Orbit(new OverlaySpec { Title = "T", Credits = "the team", Captions = [new CaptionSpec { Text = "c", End = 1 }] })));
        Assert.Equal((SceneBuilder.Generator, null), (hdri.Builder, hdri.Refusal));
        Assert.Equal(["lighting.hdriPath"], hdri.NotCarried);
        // The generator draws the title in the scene, and none of the texts that came after it.
        Assert.Equal(["labels.overlays.credits", "labels.overlays.captions"], hdri.LeftOut);

        Assert.All(BuiltInPresets.All, preset => Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(preset.Spec).Builder));

        // Labels that are shown, hidden and faded by events are the player's too.
        var events = Orbit() with
        {
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true },
                Events = [new VisibilityEvent { Target = VisibilityTarget.StationLabels, ShowFrame = 10, FadeSeconds = 1 }],
            },
        };
        Assert.Equal((SceneBuilder.Player, null), (SceneBuilders.Choose(events).Builder, SceneBuilders.Choose(events).Refusal));
        Assert.Null(SceneBuilders.Choose(events, new RenderOptions { Builder = SceneBuilder.Player }).Refusal);
        Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(BuiltInPresets.MapReveal().Spec).Builder);
    }

    [Fact]
    public void Choose_ThePlayerAskedFor_RefusesWhatItCannotCarry_ByName()
    {
        var choice = SceneBuilders.Choose(WithHdri(Orbit()), new RenderOptions { Builder = SceneBuilder.Player });

        Assert.Equal(["lighting.hdriPath"], choice.NotCarried);
        Assert.Equal(
            "The player cannot build this scene: it does not carry lighting.hdriPath. " +
            "Choose the script generator, or let the builder be chosen automatically.", choice.Refusal);
        Assert.Null(SceneBuilders.Choose(Orbit(), new RenderOptions { Builder = SceneBuilder.Player }).Refusal);
    }

    [Fact]
    public void Choose_TheGeneratorAskedFor_IsTaken_AndSaysWhatItDoesNotDraw()
    {
        var spec = Orbit(new OverlaySpec
        {
            Title = "T", Subtitle = "s", TitleSeconds = 2, Corner = "c", CornerCaptions = [new CaptionSpec { Text = "x", End = 1 }],
            EndCard = new EndCardSpec { Text = "end" }, FadeFromBlack = 1, FadeToBlack = 1, ScaleBar = true,
        });

        var choice = SceneBuilders.Choose(spec, new RenderOptions { Builder = SceneBuilder.Generator });

        Assert.Equal((SceneBuilder.Generator, null), (choice.Builder, choice.Refusal));
        Assert.Empty(choice.NotCarried);
        Assert.Equal(
            ["labels.overlays.subtitle", "labels.overlays.titleSeconds", "labels.overlays.corner", "labels.overlays.cornerCaptions",
             "labels.overlays.endCard", "labels.overlays.fadeFromBlack", "labels.overlays.fadeToBlack"],
            choice.LeftOut);
        Assert.Empty(SceneBuilders.NotDrawnByTheGenerator(Orbit(new OverlaySpec { Title = "T", ScaleBar = true, NorthArrow = true, DepthLegend = true })));
    }

    [Fact]
    public void Choose_ADraftIsAlwaysThePlayers_AndTheFramesNeedThePlayer()
    {
        var draft = SceneBuilders.Choose(WithHdri(Orbit()), new RenderOptions { Builder = SceneBuilder.Generator, Draft = true });
        Assert.Equal((SceneBuilder.Player, null), (draft.Builder, draft.Refusal));
        Assert.Equal(["lighting.hdriPath"], draft.LeftOut);

        string? kept = SceneBuilders.Choose(Orbit(), new RenderOptions { Builder = SceneBuilder.Generator, KeepFrames = true }).Refusal;
        Assert.Equal("Keeping the frames needs the player: the script generator renders in one stage.", kept);
        string? composed = SceneBuilders.Choose(WithHdri(Orbit()), new RenderOptions { ComposeOnly = true }).Refusal;
        Assert.Equal("Writing the output from the frames that are there needs the player, which does not carry lighting.hdriPath.", composed);
        Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(Orbit(), new RenderOptions { KeepFrames = true }).Builder);
    }

    // ---- the service ----

    [Fact]
    public async Task ASpecThePlayerCarries_IsPlayed_FromAPlanInTheJobFolder_InOneStage()
    {
        var blender = new FakeBlender();
        var phases = new List<RenderPhase>();

        var result = await Service(blender).RenderAsync(Orbit(), Cave(), new SyncProgress<RenderProgress>(p => phases.Add(p.Phase)));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(SceneBuilder.Player, result.Builder);
        Assert.Empty(result.NotCarried);
        Assert.Empty(result.LeftOut);
        Assert.Equal([Path.Combine(OutDir, "clip.mp4")], result.OutputPaths);
        Assert.Equal(4, result.FrameCount);
        Assert.Null(result.FramesDirectory);

        var start = Assert.Single(blender.Starts);
        Assert.Empty(start.Arguments);
        Assert.Contains("from thide_player import core", start.Script);
        Assert.Equal(["model.ply", "render.py", "scene-meta.json", "scene-plan.json", "thide_player"], start.JobFiles);
        Assert.Equal(OutDir, start.Plan!.Output.Directory);
        Assert.False(start.Plan.HasTexts);
        Assert.DoesNotContain(RenderPhase.Composing, phases);
        // Nothing is left of the job: its outputs went elsewhere.
        Assert.Empty(Directory.GetDirectories(JobRoot));
    }

    [Fact]
    public async Task ASpecThePlayerCannotCarry_IsGenerated_AndTheResultSaysWhy()
    {
        var blender = new FakeBlender();
        var spec = WithHdri(Orbit(new OverlaySpec { Credits = "the team" }));

        var result = await Service(blender).RenderAsync(spec, Cave());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(SceneBuilder.Generator, result.Builder);
        Assert.Equal(["lighting.hdriPath"], result.NotCarried);
        Assert.Equal(["labels.overlays.credits"], result.LeftOut);
        var start = Assert.Single(blender.Starts);
        Assert.StartsWith("# Generated by ThIDE", start.Script);
        Assert.Null(start.Plan);
    }

    [Fact]
    public async Task TheBuilderAskedFor_IsTheOneThatBuilds_OrTheRenderIsRefusedBeforeBlenderStarts()
    {
        var blender = new FakeBlender();
        var service = Service(blender);

        var generated = await service.RenderAsync(Orbit(), Cave(), new RenderOptions { Builder = SceneBuilder.Generator });
        Assert.Equal((true, SceneBuilder.Generator), (generated.Succeeded, generated.Builder));
        Assert.Empty(generated.NotCarried);
        Assert.Null(Assert.Single(blender.Starts).Plan);

        var refused = await service.RenderAsync(WithHdri(Orbit()), Cave(), new RenderOptions { Builder = SceneBuilder.Player });
        Assert.Equal((false, RenderFailureKind.BuilderRefused), (refused.Succeeded, refused.FailureKind));
        Assert.Equal(["lighting.hdriPath"], refused.NotCarried);
        Assert.Contains("does not carry lighting.hdriPath", refused.ErrorMessage);
        Assert.Single(blender.Starts);

        var noFrames = await service.RenderAsync(Orbit(), Cave(), new RenderOptions { Builder = SceneBuilder.Generator, KeepFrames = true });
        Assert.Equal(RenderFailureKind.BuilderRefused, noFrames.FailureKind);
        Assert.Single(blender.Starts);
    }

    [Fact]
    public async Task ASceneWithTexts_IsRenderedInTwoStages_EachABlenderOfItsOwn_AndItsCaptionsAreWrittenAsSubtitles()
    {
        var blender = new FakeBlender();
        var spec = Orbit(new OverlaySpec
        {
            Title = "Invented Cave", EndCard = new EndCardSpec { Text = "The end", Seconds = 1 },
            Captions = [new CaptionSpec { Text = "Sala Mare", Start = 0.25, End = 0.75 }],
        });
        var ticks = new List<RenderProgress>();

        var result = await Service(blender).RenderAsync(spec, Cave(), new SyncProgress<RenderProgress>(ticks.Add));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(SceneBuilder.Player, result.Builder);
        Assert.Equal(2, blender.Starts.Count);
        string frames = blender.Starts[0].Arguments[3];
        Assert.Equal(["--stage", "frames", "--frames", frames], blender.Starts[0].Arguments);
        Assert.Equal(["--stage", "compose", "--frames", frames], blender.Starts[1].Arguments);
        Assert.Equal("frames", Path.GetFileName(frames));
        Assert.StartsWith(JobRoot, frames);
        // Four frames of the scene, and four more under the end card.
        Assert.Equal(8, result.FrameCount);
        Assert.Contains(ticks, t => t.Phase == RenderPhase.Rendering && t.Frame == 4);
        Assert.Contains(ticks, t => t.Phase == RenderPhase.Composing && t.Frame == 8);

        Assert.Equal(
            [Path.Combine(OutDir, "clip.mp4"), Path.Combine(OutDir, "clip.srt"), Path.Combine(OutDir, "clip.vtt")],
            result.OutputPaths);
        Assert.Equal("1\n00:00:00,250 --> 00:00:00,750\nSala Mare\n\n", await File.ReadAllTextAsync(Path.Combine(OutDir, "clip.srt")));
        Assert.StartsWith("WEBVTT\n", await File.ReadAllTextAsync(Path.Combine(OutDir, "clip.vtt")));
        Assert.Null(result.FramesDirectory);
        Assert.Empty(Directory.GetDirectories(JobRoot));
    }

    [Fact]
    public async Task WhenTheFirstStageFails_TheSecondIsNotStarted_AndItsLogIsKept()
    {
        var blender = new FakeBlender { FailAt = "frames" };

        var result = await Service(blender).RenderAsync(Orbit(new OverlaySpec { Title = "Invented Cave" }), Cave());

        Assert.Equal((false, RenderFailureKind.ScriptError), (result.Succeeded, result.FailureKind));
        Assert.Single(blender.Starts);
        Assert.Equal("job-frames.log", Path.GetFileName(result.JobLogPath));
        Assert.True(File.Exists(result.JobLogPath));
        // The frames nobody can go on from are not left behind with the log.
        string job = Assert.Single(Directory.GetDirectories(JobRoot));
        Assert.Equal(["job-frames.log"], Directory.GetFileSystemEntries(job).Select(Path.GetFileName));
    }

    [Fact]
    public async Task FramesThatAreAskedFor_AreKeptBesideTheOutput_AndMakeAnyRenderATwoStageOne()
    {
        var blender = new FakeBlender();

        var result = await Service(blender).RenderAsync(Orbit(), Cave(), new RenderOptions { KeepFrames = true });

        Assert.True(result.Succeeded, result.ErrorMessage);
        string frames = Path.Combine(OutDir, "clip-frames");
        Assert.Equal(frames, result.FramesDirectory);
        Assert.Equal(["--stage", "frames", "--frames", frames], blender.Starts[0].Arguments);
        Assert.Equal(["--stage", "compose", "--frames", frames, "--keep-frames"], blender.Starts[1].Arguments);
        Assert.Equal(4, Directory.GetFiles(frames, "frame_*.png").Length);
        Assert.Equal([Path.Combine(OutDir, "clip.mp4")], result.OutputPaths);
    }

    [Fact]
    public async Task AFramesFolderOfTheCallers_IsWhereAStoppedRenderLeavesItsFrames_AndWhereTheOutputIsWrittenAgainFrom()
    {
        string frames = Path.Combine(_root, "my frames");
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" });

        // The second stage fails: the frames stay where the caller keeps them, to go on from.
        var failing = new FakeBlender { FailAt = "compose" };
        var stopped = await Service(failing).RenderAsync(spec, Cave(), new RenderOptions { FramesDirectory = frames });
        Assert.False(stopped.Succeeded);
        Assert.Equal(frames, stopped.FramesDirectory);
        Assert.Equal(4, Directory.GetFiles(frames, "frame_*.png").Length);

        // Another title, and only the second stage: nothing is rendered, and the frames are not removed.
        var blender = new FakeBlender();
        var retitled = await Service(blender).RenderAsync(
            Orbit(new OverlaySpec { Title = "Another Name" }), Cave(), new RenderOptions { FramesDirectory = frames, ComposeOnly = true });
        Assert.True(retitled.Succeeded, retitled.ErrorMessage);
        var start = Assert.Single(blender.Starts);
        Assert.Equal(["--stage", "compose", "--frames", frames, "--keep-frames"], start.Arguments);
        Assert.Equal("Another Name", start.Plan!.Texts!.Items.Single(i => i.Role == "title").Text);
        Assert.Equal(frames, retitled.FramesDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() => Service(blender).RenderAsync(spec, Cave(), new RenderOptions { ComposeOnly = true }));
    }

    [Fact]
    public async Task ADraft_IsThePlayersQuickLook_WhateverBuilderWasAskedFor_AndSaysWhatItDoesNotShow()
    {
        var blender = new FakeBlender();
        var spec = WithHdri(Orbit(new OverlaySpec { Title = "Invented Cave" }));

        var result = await Service(blender).RenderAsync(spec, Cave(), new RenderOptions { Builder = SceneBuilder.Generator, Draft = true });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(SceneBuilder.Player, result.Builder);
        Assert.Equal(["lighting.hdriPath"], result.LeftOut);
        Assert.Empty(result.NotCarried);
        var plan = blender.Starts[0].Plan!;
        Assert.Equal((PlanEngine.Workbench, 32, 32, "clip-draft"), (plan.Look.Engine, plan.Look.Width, plan.Look.Height, plan.Output.BaseName));
        Assert.True(plan.HasTexts);
        Assert.Equal(2, blender.Starts.Count);
        Assert.Equal([Path.Combine(OutDir, "clip-draft.mp4")], result.OutputPaths);
    }

    [Fact]
    public async Task ThePlayersPath_SaysWhenStationLabelsWereThinnedOut_AsAGeneratedScriptDoes()
    {
        var blender = new FakeBlender();
        var spec = Orbit() with { Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 10 } } };

        var result = await Service(blender).RenderAsync(spec, Cave(InventedCaveSize.Medium));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(SceneBuilder.Player, result.Builder);
        string notice = Assert.Single(result.Warnings);
        Assert.Matches(@"^showing 10 of \d+ station labels$", notice);
        Assert.Equal(10, blender.Starts[0].Plan!.Labels!.Texts!.Items.Count);
    }

    [Fact]
    public async Task TheDiskCheck_CountsTheFramesOfATwoStageRender_BeforeItsLongFirstStage()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" }) with
        {
            Animation = new AnimationSpec { Fps = 25, DurationSeconds = 40 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 1920, Height = 1080, BaseName = "clip", OutputDirectory = OutDir },
        };
        long movie = DiskPreflight.EstimateBytes(1000, 1920, 1080, OutputKind.Video);
        long frames = DiskPreflight.EstimateBytes(1000, 1920, 1080, OutputKind.FrameSequence);
        Assert.True(frames > movie * 5);
        // Room for the movie three times over, and not for its frames.
        long free = movie * 3;

        var blender = new FakeBlender();
        var result = await Service(blender, free).RenderAsync(spec, Cave());
        Assert.Equal(RenderFailureKind.DiskSpace, result.FailureKind);
        Assert.Empty(blender.Starts);

        // The same movie without texts takes one stage and fits.
        var plain = await Service(blender, free).RenderAsync(spec with { Labels = new LabelsSpec() }, Cave());
        Assert.True(plain.Succeeded, plain.ErrorMessage);
    }

    [Fact]
    public void Pruning_RemovesTheFolderOfAPlayerJobThatWasKilled_AndKeepsOneThatHoldsOutputs()
    {
        var service = Service(new FakeBlender());
        string killed = Path.Combine(JobRoot, "job-1-killed"), holding = Path.Combine(JobRoot, "job-2-outputs");
        foreach (string job in new[] { killed, holding })
        {
            Directory.CreateDirectory(Path.Combine(job, "thide_player", "modules"));
            Directory.CreateDirectory(Path.Combine(job, "frames"));
            File.WriteAllText(Path.Combine(job, "thide_player", "core.py"), "");
            File.WriteAllText(Path.Combine(job, "frames", "frame_0001.png"), "");
            foreach (string name in new[] { "scene-plan.json", "render.py", "model.ply", "job.log", "job-frames.log" })
                File.WriteAllText(Path.Combine(job, name), "");
        }
        File.WriteAllText(Path.Combine(holding, "clip.mp4"), "a movie");
        foreach (string job in new[] { killed, holding }) Directory.SetLastWriteTimeUtc(job, DateTime.UtcNow.AddDays(-30));

        Assert.Equal(1, service.PruneJobFolders(TimeSpan.FromDays(7)));

        Assert.False(Directory.Exists(killed));
        Assert.True(File.Exists(Path.Combine(holding, "clip.mp4")));
    }
}
