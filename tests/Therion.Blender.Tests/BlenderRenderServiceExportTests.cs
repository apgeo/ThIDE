// What the render service exports for someone to run in Blender themselves: the player's folder
// with the plan a render of the same spec plays, one script that carries all of it, the job
// folder for Blender's window, and the script generator's script when that builder is asked for.
// On an invented cave with invented images; no Blender runs here.

using System.Runtime.CompilerServices;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

public class BlenderRenderServiceExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-export-" + Guid.NewGuid().ToString("N"));

    public BlenderRenderServiceExportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private RenderSource Cave()
    {
        string lox = Path.Combine(_root, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        return new RenderSource(new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
    }

    private string JobRoot => Path.Combine(_root, "jobs");

    /// <summary>A scene with everything only the player draws: texts, annotations with a
    /// photo, a reveal, and an image the scene is lit by, which is turned.</summary>
    private SceneSpec Everything() => BuiltInPresets.OrbitShowcase().Spec with
    {
        Lighting = new LightingSpec
        {
            Rig = LightingRig.HdriFile,
            HdriPath = Player.TestImages.Write(Path.Combine(_root, "skies"), "evening sky.hdr", Player.TestImages.RadianceHdr(16, 8, Player.TestImages.Compass)),
            HdriRotation = 90,
        },
        Labels = new LabelsSpec
        {
            Overlays = new OverlaySpec
            {
                Title = "{cave}", Subtitle = "an invented survey", ScaleBar = true, Credits = "the invented team", FadeFromBlack = 0.25,
                Captions = [new CaptionSpec { Text = "Sala Mare", Start = 0.25, End = 0.75 }],
            },
        },
        Annotations =
        [
            new AnnotationSpec { Kind = AnnotationKind.Callout, Station = "main0", Text = "Intrarea" },
            new AnnotationSpec
            {
                Kind = AnnotationKind.Photo, Station = "main6",
                Image = Player.TestImages.Write(Path.Combine(_root, "trip"), "the flag.png", Player.TestImages.FlagPng()),
            },
        ],
        Reveal = new RevealSpec { By = RevealBy.Distance, From = "main0" },
        Animation = new AnimationSpec { Fps = 4, DurationSeconds = 1 },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 64, Height = 64, BaseName = "clip" },
    };

    /// <summary>A stand-in for Blender that keeps the plan it was started on and reports a
    /// failure: these tests want the plan of a render, not its outputs.</summary>
    private sealed class PlanKeeper : IBlenderProcessLauncher
    {
        public string? Plan { get; private set; }

        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            Plan ??= File.ReadAllText(Path.Combine(cwd, ScenePlanSerializer.FileName));
            return new Stopped();
        }

        private sealed class Stopped : IBlenderProcess
        {
            public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                yield return "THIDE:error=the stand-in renders nothing";
            }

            public int ExitCode => 64;
            public void KillTree() { }
            public void Dispose() { }
        }
    }

    private sealed class FakeProbe : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => new BlenderVersion(4, 5, 0);
    }

    private BlenderRenderService Service(IBlenderProcessLauncher? blender = null)
        => new(new BlenderLocator(new FakeProbe(), () => ["/fake/blender"]), new BlenderRunner(blender ?? new PlanKeeper(), _ => 50_000_000_000L), JobRoot);

    /// <summary>A plan as text, with its output folder taken out: where a render writes is the
    /// one thing an export and a render do not share.</summary>
    private static string WithoutItsFolder(string planJson)
    {
        var plan = ScenePlanSerializer.Read(planJson);
        return ScenePlanSerializer.Write(plan with { Output = plan.Output with { Directory = "." } });
    }

    private static List<string> FilesIn(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
        .Order(StringComparer.Ordinal)
        .ToList();

    [Fact]
    public async Task AnExport_IsThePlayersFolder_AndItsPlanIsThePlanARenderOfTheSameSpecPlays()
    {
        var spec = Everything();
        string folder = Path.Combine(_root, "exported");
        var phases = new List<RenderPhase>();

        var export = await Service().ExportAsync(spec, Cave(), folder, new ExportOptions(), new SyncProgress<RenderProgress>(p => phases.Add(p.Phase)));

        Assert.Equal(Path.Combine(folder, "render.py"), export.ScriptPath);
        Assert.Equal((SceneBuilder.Player, false, folder), (export.Builder, export.SingleFile, export.Directory));
        Assert.Empty(export.ScriptArguments);
        Assert.Empty(export.LeftOut);
        Assert.True(export.TextsInSecondStage);
        Assert.Equal(4, export.FrameCount);
        Assert.Equal([RenderPhase.ConvertingGeometry, RenderPhase.GeneratingScript, RenderPhase.Done], phases);

        // What a render bundle holds: the plan, the player, the mesh and the images at their
        // plan paths, the README, the run scripts, the licence, and the captions as subtitles.
        var files = FilesIn(folder);
        Assert.Equal(files, export.Files.Order(StringComparer.Ordinal));
        foreach (string expected in new[]
                 {
                     "LICENSE", "NOTICE", "README.txt", "render.cmd", "render.sh", "render.py", "scene-plan.json", "model.ply",
                     "hdri/evening_sky.hdr", "photos/the_flag.png", "out/clip.srt", "out/clip.vtt",
                     "thide_player/__init__.py", "thide_player/core.py", "thide_player/modules/texts.py",
                 })
            Assert.Contains(expected, files);
        Assert.DoesNotContain("scene-meta.json", files);
        Assert.Contains("sh render.sh", File.ReadAllText(Path.Combine(folder, "README.txt")));
        // The scratch of the conversion is gone.
        Assert.Empty(Directory.Exists(JobRoot) ? Directory.GetFileSystemEntries(JobRoot) : []);

        // The same scene as "Render": the plan the render service gives Blender for this spec.
        var blender = new PlanKeeper();
        await Service(blender).RenderAsync(spec with { Output = spec.Output with { OutputDirectory = Path.Combine(_root, "renders") } }, Cave());
        Assert.NotNull(blender.Plan);
        string exported = File.ReadAllText(Path.Combine(folder, "scene-plan.json"));
        Assert.Equal(WithoutItsFolder(blender.Plan!), WithoutItsFolder(exported));

        // And it is the scene only the player draws: the texts, the annotations, the reveal,
        // the image turned. What it renders stays in the folder.
        var plan = ScenePlanSerializer.Read(exported);
        Assert.True(plan.HasTexts);
        Assert.Equal(2, plan.Annotations!.Count);
        Assert.Contains(plan.Changes!, c => c.Reveal is not null);
        Assert.Equal(("hdri/evening_sky.hdr", 90.0), (plan.Look.World!.Image, plan.Look.World.Rotation));
        Assert.Equal(RenderBundleWriter.OutputDirectory, plan.Output.Directory);

        // The folder is written over when it is exported to again; what else is in it stays.
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");
        await Service().ExportAsync(spec with { Annotations = [] }, Cave(), folder, new ExportOptions { Language = "ro" });
        Assert.Contains("notes.txt", FilesIn(folder));
        Assert.Null(ScenePlanSerializer.ReadFile(Path.Combine(folder, "scene-plan.json")).Annotations);
        Assert.Contains("CUM SE RANDEAZĂ", File.ReadAllText(Path.Combine(folder, "README.txt")));
    }

    [Fact]
    public async Task OneFile_IsOneScript_ThatCarriesThePlanThePlayerAndEveryFileThePlanNames()
    {
        string folder = Path.Combine(_root, "one");

        var export = await Service().ExportAsync(Everything(), Cave(), folder, new ExportOptions { SingleFile = true });

        Assert.Equal((SceneBuilder.Player, true), (export.Builder, export.SingleFile));
        Assert.Equal(["render.py"], FilesIn(folder));
        Assert.Equal(["render.py"], export.Files);
        Assert.Equal(Path.Combine(folder, "render.py"), export.ScriptPath);
        string script = File.ReadAllText(export.ScriptPath);
        Assert.Contains("PLAN = r'''", script);
        Assert.Contains("\"thide_player.core\": r'''", script);
        foreach (string carried in new[] { "model.ply", "hdri/evening_sky.hdr", "photos/the_flag.png" })
            Assert.Contains($"    \"{carried}\": \"", script);
        Assert.Contains("\"text\": \"an invented survey\"", script);
        Assert.Empty(Directory.Exists(JobRoot) ? Directory.GetFileSystemEntries(JobRoot) : []);
    }

    [Fact]
    public async Task AnExportToLookAt_IsTheJobFolderARenderPlays_StartedWithBuildOnly()
    {
        var spec = Everything();
        string folder = Path.Combine(_root, "gui"), renders = Path.Combine(_root, "my renders");

        var export = await Service().ExportAsync(
            spec with { Output = spec.Output with { OutputDirectory = renders } }, Cave(), folder, new ExportOptions { Interactive = true, SingleFile = true });

        Assert.Equal(SceneBuilder.Player, export.Builder);
        Assert.False(export.SingleFile);
        Assert.Equal(["--build-only"], export.ScriptArguments);
        Assert.Equal(Path.Combine(folder, "render.py"), export.ScriptPath);
        Assert.True(export.TextsInSecondStage);
        var files = FilesIn(folder);
        Assert.Equal(files, export.Files);
        Assert.Contains("scene-plan.json", files);
        Assert.Contains("thide_player/core.py", files);
        Assert.Contains("photos/the_flag.png", files);
        Assert.DoesNotContain("README.txt", files);
        Assert.False(Directory.Exists(renders));

        // The plan is the render's, to the last key: a render from Blender's window writes
        // where "Render" would.
        var blender = new PlanKeeper();
        await Service(blender).RenderAsync(spec with { Output = spec.Output with { OutputDirectory = renders } }, Cave());
        Assert.Equal(blender.Plan, File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
        Assert.Equal(renders, ScenePlanSerializer.ReadFile(Path.Combine(folder, "scene-plan.json")).Output.Directory);

        // Without a folder of its own to render to, it would render into the export's.
        string plain = Path.Combine(_root, "gui-plain");
        var texts = await Service().ExportAsync(
            spec with { Labels = new LabelsSpec() }, Cave(), plain, new ExportOptions { Interactive = true });
        Assert.False(texts.TextsInSecondStage);
        Assert.Equal(Path.Combine(plain, "out"), ScenePlanSerializer.ReadFile(Path.Combine(plain, "scene-plan.json")).Output.Directory);
    }

    [Fact]
    public async Task TheScriptGenerator_AskedFor_ExportsItsScript_AndSaysWhatItDoesNotShow()
    {
        var spec = Everything();
        string folder = Path.Combine(_root, "generated");

        var export = await Service().ExportAsync(spec, Cave(), folder, new ExportOptions { Builder = SceneBuilder.Generator });

        Assert.Equal((SceneBuilder.Generator, false), (export.Builder, export.SingleFile));
        Assert.Equal(
            ["labels.overlays.subtitle", "labels.overlays.credits", "labels.overlays.captions", "labels.overlays.fadeFromBlack",
             "annotations", "reveal", "lighting.hdriRotation"],
            export.LeftOut);
        Assert.Equal(["model.ply", "render.py", "scene-meta.json"], FilesIn(folder));
        string script = File.ReadAllText(export.ScriptPath);
        Assert.Contains("CAM_KEYS", script);
        Assert.DoesNotContain("PLY_BASE64 = ", script);

        // One file is its self-contained script with the meshes inside; to look at, its
        // script that renders nothing.
        var one = await Service().ExportAsync(spec, Cave(), Path.Combine(_root, "generated-one"), new ExportOptions { Builder = SceneBuilder.Generator, SingleFile = true });
        Assert.True(one.SingleFile);
        string single = File.ReadAllText(one.ScriptPath);
        Assert.Contains("SCENE_META_JSON = ", single);
        Assert.Contains("PLY_BASE64 = ", single);
        var look = await Service().ExportAsync(spec, Cave(), Path.Combine(_root, "generated-gui"), new ExportOptions { Builder = SceneBuilder.Generator, Interactive = true });
        Assert.Empty(look.ScriptArguments);
        Assert.Contains("thide(\"phase\", \"interactive\")", File.ReadAllText(look.ScriptPath));
    }

    [Fact]
    public async Task AnImageThePlayerDoesNotLightASceneWith_StopsTheExport_BeforeAnythingIsWritten()
    {
        string picture = Player.TestImages.Write(Path.Combine(_root, "skies"), "sky.png", Player.TestImages.FlagPng());
        var spec = Everything() with { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = picture } };
        string folder = Path.Combine(_root, "refused");

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => Service().ExportAsync(spec, Cave(), folder, new ExportOptions()));

        Assert.Contains("lighting.hdriPath", refused.Message);
        Assert.Empty(FilesIn(folder));
        Assert.Empty(Directory.Exists(JobRoot) ? Directory.GetFileSystemEntries(JobRoot) : []);
    }

    [Fact]
    public async Task AServiceThatKnowsOnlyTheGeneratedScript_ExportsIt_AndSaysWhichBuilderThatIs()
    {
        IBlenderRenderService old = new OnlyAScript();

        var export = await old.ExportAsync(Everything(), Cave(), Path.Combine(_root, "old"), new ExportOptions { Interactive = true });

        Assert.Equal(SceneBuilder.Generator, export.Builder);
        Assert.EndsWith("render.py", export.ScriptPath);
        Assert.Contains("annotations", export.LeftOut);
        Assert.True(((OnlyAScript)old).Interactive);
    }

    private sealed class OnlyAScript : IBlenderRenderService
    {
        public bool Interactive { get; private set; }

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
        {
            Interactive = interactive;
            return Task.FromResult(Path.Combine(outputDir, "render.py"));
        }
    }
}
