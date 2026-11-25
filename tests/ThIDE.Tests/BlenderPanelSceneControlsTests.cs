// The Blender Animation panel's controls for what a scene can have beside its camera and its
// output: the light (an image all round the scene), the reveal, the annotations of a file, and
// the texts that are switched on rather than typed. And the two commands that hand the scene to
// someone else, "Export script…" and "Open in Blender…", which give them the scene "Render"
// shows. The view model is driven as the view drives it; no view, no Blender. Every file here is
// invented and written by the test.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

// The assertions compare what the view model said with the same resource read now, so no test
// that switches the UI language may run beside these.
[Collection(UiLanguageCollection.Name)]
public class BlenderPanelSceneControlsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-vm-scene-" + Guid.NewGuid().ToString("N"));

    public BlenderPanelSceneControlsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ---- fakes ----

    private sealed class FakeRenderService : IBlenderRenderService
    {
        public SceneSpec? LastSpec { get; private set; }
        public SceneSpec? LastExportSpec { get; private set; }
        public ExportOptions? LastExportOptions { get; private set; }
        public string? LastExportDirectory { get; private set; }

        /// <summary>What an export answers beside its script; the script's path is filled in.</summary>
        public RenderExport Export { get; set; } = new() { ScriptPath = "", Directory = "" };

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastSpec = spec;
            return Task.FromResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/render.mp4"), FrameCount = 2 });
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => throw new InvalidOperationException("The panel exports through ExportAsync.");

        public Task<RenderExport> ExportAsync(SceneSpec spec, RenderSource source, string outputDir, ExportOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastExportSpec = spec;
            LastExportOptions = options;
            LastExportDirectory = outputDir;
            return Task.FromResult(Export with
            {
                ScriptPath = Path.Combine(outputDir, "render.py"),
                Directory = outputDir,
                Builder = options.Builder == SceneBuilder.Generator ? SceneBuilder.Generator : SceneBuilder.Player,
                ScriptArguments = options.Interactive && options.Builder != SceneBuilder.Generator ? ["--build-only"] : [],
            });
        }
    }

    private sealed class FakeSources : IBlenderSourceProvider
    {
        public bool HasProject => false;
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => [];
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(
                new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []));
    }

    private sealed class FakeGui : IBlenderGuiLauncher
    {
        public string? Launched { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }
        public bool Launch(string scriptPath, IReadOnlyList<string>? scriptArguments = null)
        {
            Launched = scriptPath;
            Arguments = scriptArguments;
            return true;
        }
    }

    private BlenderAnimationViewModel Vm(FakeRenderService? service = null, bool store = false, FakeGui? gui = null)
        => new(service ?? new FakeRenderService(), new FakeSources(), store ? new PresetStore(Path.Combine(_root, "presets")) : null, gui: gui)
        {
            UseExternalFile = true,
            ExternalFilePath = "m.lox",
        };

    private string Write(string name, string text)
    {
        string path = Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A picture of one pixel.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // ---- the light ----

    [Fact]
    public void TheLampsReach_IsTheSpecs_GoesWithTheHeadLamp_AndComesBackFromAPreset()
    {
        var vm = Vm(store: true);
        Assert.False(vm.LampReach);
        Assert.False(vm.IsHeadlamp);

        vm.LightRig = LightingRig.Headlamp;
        vm.LampReach = true;
        Assert.True(vm.IsHeadlamp);
        Assert.Equal((LightingRig.Headlamp, true), (vm.CurrentSpec.Lighting.Rig, vm.CurrentSpec.Lighting.LampReach));
        Assert.Empty(vm.Blockers);

        // A preset saved from the panel has it and brings it back; one that has not takes it away.
        vm.NewPresetName = "From far away";
        vm.SavePresetCommand.Execute(null);
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.False(vm.LampReach);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "From far away");
        Assert.Equal((LightingRig.Headlamp, true), (vm.LightRig, vm.LampReach));
        Assert.NotEmpty(Tr.Get("Blender_UI_LampReach"));
        Assert.NotEqual("Blender_UI_LampReach_Tip", Tr.Get("Blender_UI_LampReach_Tip"));
    }

    [Fact]
    public void TheLight_ReachesTheSpec_AndAnImageIsUsableOnlyWithItsRig()
    {
        var vm = Vm();
        var preset = BuiltInPresets.OrbitShowcase().Spec.Lighting;
        Assert.Equal((preset.Rig, preset.Strength, "", 0.0), (vm.LightRig, vm.LightStrength, vm.HdriPath, vm.HdriRotation));
        Assert.False(vm.IsHdri);
        Assert.Equal(Enum.GetValues<LightingRig>(), vm.LightRigs);

        vm.LightRig = LightingRig.Headlamp;
        vm.LightStrength = 2.5;
        Assert.Equal((LightingRig.Headlamp, 2.5), (vm.CurrentSpec.Lighting.Rig, vm.CurrentSpec.Lighting.Strength));
        Assert.Empty(vm.Blockers);

        // The rig of an image needs its image: a file that is there, of a kind the player
        // lights a scene by.
        vm.LightRig = LightingRig.HdriFile;
        Assert.True(vm.IsHdri);
        Assert.Contains(vm.Blockers, b => b.StartsWith("lighting.hdriPath", StringComparison.Ordinal));
        Assert.False(vm.RenderCommand.CanExecute(null));

        string sky = WriteBytes("skies/evening sky.hdr", [1, 2, 3]);
        vm.HdriPath = "  " + sky + " ";
        vm.HdriRotation = 90;
        Assert.Empty(vm.Blockers);
        Assert.True(vm.RenderCommand.CanExecute(null));
        Assert.Equal((LightingRig.HdriFile, sky, 90.0, 2.5),
            (vm.CurrentSpec.Lighting.Rig, vm.CurrentSpec.Lighting.HdriPath, vm.CurrentSpec.Lighting.HdriRotation, vm.CurrentSpec.Lighting.Strength));

        vm.HdriPath = Path.Combine(_root, "skies", "gone.exr");
        Assert.Equal([string.Format(Tr.Get("Blender_Blocker_FileNotFound"), vm.HdriPath)], vm.Blockers);

        // A photograph is no such image for the player; the script generator loads any picture.
        vm.HdriPath = WriteBytes("skies/panorama.jpg", [1, 2, 3]);
        Assert.Equal([Tr.Get("Blender_Blocker_HdriKind")], vm.Blockers);
        vm.Builder = SceneBuilder.Generator;
        Assert.Empty(vm.Blockers);
        vm.Builder = SceneBuilder.Auto;
        Assert.Single(vm.Blockers);

        // Another rig, and the image is no one's business; it stays for when its rig comes back.
        vm.LightRig = LightingRig.SunSky;
        Assert.Empty(vm.Blockers);
        Assert.EndsWith("panorama.jpg", vm.CurrentSpec.Lighting.HdriPath);
    }

    [Fact]
    public async Task BrowsingForAnImage_LightsTheSceneByIt_AndAPresetKeepsIt()
    {
        string sky = WriteBytes("skies/evening.hdr", [1, 2, 3]);
        var vm = Vm(store: true);

        // Without a file dialog, and with one that is closed, nothing changes.
        await vm.BrowseHdriCommand.ExecuteAsync(null);
        var asked = new List<BlenderFileKind>();
        string? chosen = null;
        vm.PickFile = kind => { asked.Add(kind); return Task.FromResult(chosen); };
        await vm.BrowseHdriCommand.ExecuteAsync(null);
        Assert.Equal([BlenderFileKind.HdriImage], asked);
        Assert.False(vm.IsHdri);
        Assert.Equal("", vm.HdriPath);

        chosen = sky;
        await vm.BrowseHdriCommand.ExecuteAsync(null);
        Assert.Equal((LightingRig.HdriFile, sky), (vm.LightRig, vm.HdriPath));
        Assert.Empty(vm.Blockers);
        vm.HdriRotation = -45;

        // The image is part of the scene: a preset saved from the panel has it, and brings it back.
        vm.NewPresetName = "Under a sky";
        vm.SavePresetCommand.Execute(null);
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.Equal((false, "", 0.0), (vm.IsHdri, vm.HdriPath, vm.HdriRotation));
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Under a sky");
        Assert.Equal((LightingRig.HdriFile, sky, -45.0), (vm.LightRig, vm.HdriPath, vm.HdriRotation));
        Assert.Equal(sky, vm.CurrentSpec.Lighting.HdriPath);
    }

    // ---- the reveal ----

    [Fact]
    public void AReveal_IsOffUnlessChosen_ReachesTheSpec_AndKeepsOnlyWhatGoesWithItsKind()
    {
        var vm = Vm();
        Assert.Equal(RevealChoice.Off, vm.RevealMode);
        Assert.Null(vm.CurrentSpec.Reveal);
        Assert.False(vm.HasReveal);
        Assert.Equal([RevealChoice.Off, RevealChoice.Distance, RevealChoice.Order, RevealChoice.Date], vm.RevealChoices);

        vm.RevealMode = RevealChoice.Distance;
        vm.RevealFrom = " main0 ";
        Assert.True(vm.HasReveal && vm.RevealsByDistance && !vm.RevealsByDate);
        Assert.Equal(new RevealSpec { By = RevealBy.Distance, From = "main0" }, vm.CurrentSpec.Reveal);
        Assert.Empty(vm.Blockers);

        // By order there is no station to measure from, and no file of dates.
        vm.RevealMode = RevealChoice.Order;
        Assert.Equal(new RevealSpec { By = RevealBy.Order }, vm.CurrentSpec.Reveal);
        Assert.False(vm.RevealsByDistance || vm.RevealsByDate);

        // By date, with the file that has the dates; one that is not there is said before Blender starts.
        vm.RevealMode = RevealChoice.Date;
        Assert.Equal(new RevealSpec { By = RevealBy.Date }, vm.CurrentSpec.Reveal);
        string dates = WriteBytes("cave.3d", [1, 2, 3]);
        vm.RevealDatesPath = dates;
        Assert.Equal((RevealBy.Date, dates, null), (vm.CurrentSpec.Reveal!.By, vm.CurrentSpec.Reveal.DatesPath, vm.CurrentSpec.Reveal.From));
        Assert.Empty(vm.Blockers);
        vm.RevealDatesPath = Path.Combine(_root, "gone.3d");
        Assert.Equal([string.Format(Tr.Get("Blender_Blocker_FileNotFound"), vm.RevealDatesPath)], vm.Blockers);
        vm.RevealMode = RevealChoice.Distance;
        Assert.Empty(vm.Blockers);
        Assert.Equal("main0", vm.CurrentSpec.Reveal!.From);
        Assert.Null(vm.CurrentSpec.Reveal.DatesPath);

        vm.RevealMode = RevealChoice.Off;
        Assert.Null(vm.CurrentSpec.Reveal);
    }

    [Fact]
    public void TheTimesOfAReveal_ItsEdge_AndItsGlow_ReachTheSpec_AndAreChecked()
    {
        var vm = Vm();
        vm.DurationSeconds = 10;
        vm.RevealMode = RevealChoice.Distance;
        Assert.True(vm.RevealToClipEnd);
        Assert.False(vm.RevealHasItsOwnEnd);
        Assert.Equal((0.0, null, 0.03, true), (vm.CurrentSpec.Reveal!.Start, vm.CurrentSpec.Reveal.End, vm.CurrentSpec.Reveal.Edge, vm.CurrentSpec.Reveal.Glow));

        vm.RevealStart = 1.5;
        vm.RevealEdge = 0;
        vm.RevealGlow = false;
        Assert.Equal((1.5, null, 0.0, false), (vm.CurrentSpec.Reveal!.Start, vm.CurrentSpec.Reveal.End, vm.CurrentSpec.Reveal.Edge, vm.CurrentSpec.Reveal.Glow));

        // An end of its own starts at the end of the clip, and is then the user's.
        vm.RevealToClipEnd = false;
        Assert.True(vm.RevealHasItsOwnEnd);
        Assert.Equal(10.0, vm.RevealEnd);
        Assert.Equal(10.0, vm.CurrentSpec.Reveal!.End);
        vm.RevealEnd = 8;
        Assert.Equal(8.0, vm.CurrentSpec.Reveal!.End);
        Assert.Empty(vm.Blockers);
        vm.RevealEnd = 1;
        Assert.Contains(vm.Blockers, b => b.StartsWith("reveal.end", StringComparison.Ordinal));
        Assert.False(vm.RenderCommand.CanExecute(null));
        vm.RevealToClipEnd = true;
        Assert.Null(vm.CurrentSpec.Reveal!.End);
        Assert.Empty(vm.Blockers);
        vm.RevealStart = 12;
        Assert.Contains(vm.Blockers, b => b.StartsWith("reveal.start", StringComparison.Ordinal));
    }

    [Fact]
    public void AReveal_ComesBackFromAPreset_WithWhatThePanelDoesNotShowOfIt()
    {
        var vm = Vm(store: true);
        var mapReveal = vm.Presets.Single(p => p.Name == BuiltInPresets.MapReveal().Name);
        var revealed = mapReveal.Preset.Spec.Reveal!;

        vm.SelectedPreset = mapReveal;
        Assert.Equal(RevealChoice.Distance, vm.RevealMode);
        Assert.Equal((revealed.Start, revealed.End is null, revealed.End ?? vm.DurationSeconds, revealed.Edge, revealed.Glow),
            (vm.RevealStart, vm.RevealToClipEnd, vm.RevealEnd, vm.RevealEdge, vm.RevealGlow));
        Assert.Equal(revealed, vm.CurrentSpec.Reveal);

        // Off and on again, the color of its glow and its counter of the years are as the
        // preset had them: the panel does not show them, and does not lose them.
        var odd = revealed with { GlowColor = new ColorRgb(0.1, 0.2, 0.3), Counter = false, By = RevealBy.Date };
        new PresetStore(Path.Combine(_root, "presets")).Save(new RenderPreset { Name = "History", Spec = mapReveal.Preset.Spec with { Reveal = odd } });
        vm = Vm(store: true);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "History");
        Assert.Equal(RevealChoice.Date, vm.RevealMode);
        vm.RevealMode = RevealChoice.Off;
        Assert.Null(vm.CurrentSpec.Reveal);
        vm.RevealMode = RevealChoice.Order;
        Assert.Equal(odd with { By = RevealBy.Order }, vm.CurrentSpec.Reveal);

        // A preset without one turns it off.
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn && p.Preset.Spec.Reveal is null);
        Assert.Equal(RevealChoice.Off, vm.RevealMode);
        Assert.Null(vm.CurrentSpec.Reveal);
    }

    [Fact]
    public async Task BrowsingForTheDates_NamesTheFile()
    {
        var vm = Vm();
        vm.RevealMode = RevealChoice.Date;
        string dates = WriteBytes("cave.3d", [1, 2, 3]);
        vm.PickFile = kind => Task.FromResult<string?>(kind == BlenderFileKind.RevealDates ? dates : null);

        await vm.BrowseRevealDatesCommand.ExecuteAsync(null);

        Assert.Equal(dates, vm.RevealDatesPath);
        Assert.Equal(dates, vm.CurrentSpec.Reveal!.DatesPath);
    }

    // ---- the annotations ----

    private const string Notes = """
        [
          { "id": "way-in", "kind": "callout", "station": "main0", "text": "Intrarea" },
          { "kind": "label", "station": "main6", "text": "Sala Mare", "from": 4, "to": 9 },
          { "kind": "label", "station": "main9", "text": "Sifonul" },
          { "kind": "marker", "at": [10, 20, -5] },
          { "kind": "photo", "station": "main6", "image": "pictures/hall.png", "within": 40 }
        ]
        """;

    private static string Count(int all, int labels, int callouts, int markers, int photos)
        => Tr.Plural("Blender_Annotations_Count", all) + ": " + string.Format(Tr.Get("Blender_Annotations_Kinds"), labels, callouts, markers, photos);

    [Fact]
    public void Annotations_AreLoadedFromAFile_Counted_AndCleared()
    {
        string picture = WriteBytes("trip/pictures/hall.png", OnePixelPng);
        string file = Write("trip/notes.json", Notes);
        var vm = Vm();
        Assert.Equal(Tr.Get("Blender_Annotations_None"), vm.AnnotationsSummary);
        Assert.False(vm.HasAnnotations);
        Assert.False(vm.ClearAnnotationsCommand.CanExecute(null));

        // Nothing named, nothing loaded.
        vm.LoadAnnotationsCommand.Execute(null);
        Assert.Empty(vm.CurrentSpec.Annotations);

        vm.AnnotationsPath = file;
        vm.LoadAnnotationsCommand.Execute(null);

        Assert.Equal(Count(5, 2, 1, 1, 1), vm.AnnotationsSummary);
        Assert.Empty(vm.AnnotationProblems);
        Assert.Empty(vm.Blockers);
        Assert.True(vm.HasAnnotations);
        var annotations = vm.CurrentSpec.Annotations;
        Assert.Equal(5, annotations.Count);
        Assert.Equal(new AnnotationSpec { Id = "way-in", Kind = AnnotationKind.Callout, Station = "main0", Text = "Intrarea" }, annotations[0]);
        // A photo's image is named from where the file lies.
        Assert.Equal((AnnotationKind.Photo, picture, 40.0), (annotations[4].Kind, annotations[4].Image, annotations[4].Within!.Value));

        // The file was changed: loading it again takes what it holds now, in place of what was there.
        File.WriteAllText(file, """[{ "kind": "marker", "station": "main3" }]""");
        vm.LoadAnnotationsCommand.Execute(null);
        Assert.Equal(Count(1, 0, 0, 1, 0), vm.AnnotationsSummary);
        Assert.Equal("main3", Assert.Single(vm.CurrentSpec.Annotations).Station);

        Assert.True(vm.ClearAnnotationsCommand.CanExecute(null));
        vm.ClearAnnotationsCommand.Execute(null);
        Assert.Empty(vm.CurrentSpec.Annotations);
        Assert.Equal(Tr.Get("Blender_Annotations_None"), vm.AnnotationsSummary);
        Assert.False(vm.ClearAnnotationsCommand.CanExecute(null));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void AnnotationsThatAreWrong_AreSaidBesideThem_AndStopTheRender()
    {
        string file = Write("trip/notes.json", """
            [
              { "kind": "label", "station": "main0" },
              { "id": "hall", "kind": "photo", "station": "main6", "image": "pictures/gone.png" },
              { "kind": "photo", "station": "main7", "image": "pictures/text.png" }
            ]
            """);
        WriteBytes("trip/pictures/text.png", "not a picture"u8.ToArray());
        var vm = Vm();

        vm.AnnotationsPath = file;
        vm.LoadAnnotationsCommand.Execute(null);

        // What the validator says of them, and what is wrong with the photos' files; all of it
        // is also under "Fix before rendering".
        Assert.Equal(Count(3, 1, 0, 0, 2), vm.AnnotationsSummary);
        Assert.Equal(3, vm.AnnotationProblems.Count);
        Assert.StartsWith("annotations[0].text", vm.AnnotationProblems[0]);
        Assert.Equal(string.Format(Tr.Get("Blender_Annotations_PhotoMissing"), "hall", "gone.png"), vm.AnnotationProblems[1]);
        Assert.StartsWith(string.Format(Tr.Get("Blender_Annotations_PhotoRefused"), "annotation-3", "text.png", "").TrimEnd('.', ' '), vm.AnnotationProblems[2]);
        Assert.All(vm.AnnotationProblems, problem => Assert.Contains(problem, vm.Blockers));
        Assert.False(vm.RenderCommand.CanExecute(null));
        vm.ExportDirectory = Path.Combine(_root, "exports");
        Assert.False(vm.ExportScriptCommand.CanExecute(null));

        // Cleared, the scene renders again.
        vm.ClearAnnotationsCommand.Execute(null);
        Assert.Empty(vm.AnnotationProblems);
        Assert.Empty(vm.Blockers);
        Assert.True(vm.RenderCommand.CanExecute(null));
        Assert.True(vm.ExportScriptCommand.CanExecute(null));
    }

    [Fact]
    public void AFileThatIsNotAListOfAnnotations_LeavesTheScenesAsTheyAre_AndSaysWhy()
    {
        WriteBytes("trip/pictures/hall.png", OnePixelPng);
        string good = Write("trip/notes.json", Notes);
        string misspelt = Write("trip/misspelt.json", """[{ "kind": "label", "staton": "main0", "text": "x" }]""");
        var vm = Vm();
        vm.AnnotationsPath = good;
        vm.LoadAnnotationsCommand.Execute(null);

        vm.AnnotationsPath = misspelt;
        vm.LoadAnnotationsCommand.Execute(null);

        Assert.Equal(5, vm.CurrentSpec.Annotations.Count);
        Assert.Equal(Count(5, 2, 1, 1, 1), vm.AnnotationsSummary);
        string said = Assert.Single(vm.AnnotationProblems);
        Assert.StartsWith(string.Format(Tr.Get("Blender_Annotations_NotAList"), "misspelt.json", ""), said);
        Assert.Contains("staton", said);
        // The scene itself is as good as it was: nothing stops its render.
        Assert.Empty(vm.Blockers);
        Assert.True(vm.RenderCommand.CanExecute(null));

        // A file that is not there, and one that is not JSON at all.
        vm.AnnotationsPath = Path.Combine(_root, "trip", "none.json");
        vm.LoadAnnotationsCommand.Execute(null);
        Assert.StartsWith(string.Format(Tr.Get("Blender_Annotations_NotRead"), "none.json", "").TrimEnd('.', ' '), Assert.Single(vm.AnnotationProblems));
        vm.AnnotationsPath = Write("trip/prose.json", "these are my notes");
        vm.LoadAnnotationsCommand.Execute(null);
        Assert.StartsWith(string.Format(Tr.Get("Blender_Annotations_NotAList"), "prose.json", ""), Assert.Single(vm.AnnotationProblems));
        Assert.DoesNotContain("these are my notes", vm.AnnotationProblems[0]);
        Assert.Equal(5, vm.CurrentSpec.Annotations.Count);

        // The good file again, and the complaint is gone.
        vm.AnnotationsPath = good;
        vm.LoadAnnotationsCommand.Execute(null);
        Assert.Empty(vm.AnnotationProblems);
    }

    [Fact]
    public async Task Annotations_AreKeptInAPreset_GoToTheRender_AndAPresetsOwnReplaceThoseOfAFile()
    {
        WriteBytes("trip/pictures/hall.png", OnePixelPng);
        string file = Write("trip/notes.json", Notes);
        var service = new FakeRenderService();
        var vm = Vm(service, store: true);
        vm.PickFile = kind => Task.FromResult<string?>(kind == BlenderFileKind.Annotations ? file : null);

        // Browsing for the file loads it.
        await vm.BrowseAnnotationsCommand.ExecuteAsync(null);
        Assert.Equal(file, vm.AnnotationsPath);
        Assert.Equal(5, vm.CurrentSpec.Annotations.Count);

        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal(5, service.LastSpec!.Annotations.Count);

        vm.NewPresetName = "With notes";
        vm.SavePresetCommand.Execute(null);

        // A built-in preset has none: the file's are gone with the scene they were part of,
        // and the field no longer names a file the scene does not have the annotations of.
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.Empty(vm.CurrentSpec.Annotations);
        Assert.Equal("", vm.AnnotationsPath);
        Assert.Equal(Tr.Get("Blender_Annotations_None"), vm.AnnotationsSummary);

        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "With notes");
        Assert.Equal(Count(5, 2, 1, 1, 1), vm.AnnotationsSummary);
        Assert.Equal("Sala Mare", vm.CurrentSpec.Annotations[1].Text);
        // Changing another setting leaves them where they are.
        vm.Samples = 32;
        Assert.Equal(5, vm.CurrentSpec.Annotations.Count);
    }

    // ---- the texts that are switched on ----

    [Fact]
    public void ASubtitle_TheScaleBar_TheNorthArrow_TheDepthLegend_AndTheFades_ReachTheSpec_AndComeBackFromAPreset()
    {
        var vm = Vm(store: true);
        var before = vm.CurrentSpec.Labels.Overlays;
        Assert.Equal((before.Subtitle ?? "", before.ScaleBar, before.NorthArrow, before.DepthLegend, before.FadeFromBlack, before.FadeToBlack),
            (vm.Subtitle, vm.ShowScaleBar, vm.ShowNorthArrow, vm.ShowDepthLegend, vm.FadeFromBlack, vm.FadeToBlack));

        vm.Title = "{cave}";
        vm.Subtitle = "  the 2026 survey ";
        vm.ShowScaleBar = true;
        vm.ShowNorthArrow = true;
        vm.ShowDepthLegend = true;
        vm.FadeFromBlack = 0.5;
        vm.FadeToBlack = 1.5;
        vm.Credits = "the team";

        var overlays = vm.CurrentSpec.Labels.Overlays;
        Assert.Equal(("{cave}", "the 2026 survey", true, true, true, 0.5, 1.5, "the team"),
            (overlays.Title, overlays.Subtitle, overlays.ScaleBar, overlays.NorthArrow, overlays.DepthLegend, overlays.FadeFromBlack, overlays.FadeToBlack, overlays.Credits));
        Assert.Empty(vm.Blockers);

        // What a preset has that the panel does not show stays while these are changed.
        Assert.Equal(before.TitleSeconds, overlays.TitleSeconds);
        Assert.Equal(before.Corner, overlays.Corner);

        // A fade that is no time is said before Blender starts.
        vm.FadeToBlack = -1;
        Assert.Contains(vm.Blockers, b => b.StartsWith("labels.overlays.fadeToBlack", StringComparison.Ordinal));
        vm.FadeToBlack = 1.5;

        vm.NewPresetName = "Dressed";
        vm.SavePresetCommand.Execute(null);
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn && p.Name != "Orbit showcase");
        vm.Subtitle = "";
        vm.ShowScaleBar = false;
        Assert.Null(vm.CurrentSpec.Labels.Overlays.Subtitle);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Dressed");
        Assert.Equal(("the 2026 survey", true, true, true, 0.5, 1.5),
            (vm.Subtitle, vm.ShowScaleBar, vm.ShowNorthArrow, vm.ShowDepthLegend, vm.FadeFromBlack, vm.FadeToBlack));
    }

    // ---- export and open in Blender: the scene "Render" shows ----

    [Fact]
    public async Task Export_IsThePlayersFolder_OneFileWhenAskedFor_AndTheScriptGeneratorsWhenItIsChosen()
    {
        var service = new FakeRenderService { Export = new RenderExport { ScriptPath = "", Directory = "", Notices = ["showing 200 of 512 station labels"] } };
        var vm = Vm(service);
        vm.Credits = "the team";
        vm.ExportDirectory = "  " + Path.Combine(_root, "exports") + " ";

        // The builder that is left to be chosen is the player: a folder, with the scene as it is in the panel.
        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.Equal((SceneBuilder.Auto, false, false), (service.LastExportOptions!.Builder, service.LastExportOptions.SingleFile, service.LastExportOptions.Interactive));
        Assert.Equal(Path.Combine(_root, "exports"), service.LastExportDirectory);
        Assert.Same(vm.CurrentSpec, service.LastExportSpec);
        Assert.Equal("the team", service.LastExportSpec!.Labels.Overlays.Credits);
        Assert.Equal([Path.Combine(_root, "exports", "render.py")], vm.Outputs);
        Assert.Equal(Tr.Get("Blender_Status_Exported"), vm.StatusMessage);
        Assert.Equal(["showing 200 of 512 station labels"], vm.Warnings);
        Assert.Equal(Tr.Culture.TwoLetterISOLanguageName, service.LastExportOptions.Language);

        // "Self-contained script" is the player's one file; its meshes are always inside, so
        // there is nothing to choose about them.
        vm.SelfContained = true;
        Assert.False(vm.CanEmbedMesh);
        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.True(service.LastExportOptions!.SingleFile);
        vm.Builder = SceneBuilder.Player;
        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.Equal((SceneBuilder.Player, true), (service.LastExportOptions!.Builder, service.LastExportOptions.SingleFile));

        // The script generator, chosen by name, exports as it always did: its two options are
        // the spec's, and the result says what its script does not draw.
        service.Export = new RenderExport { ScriptPath = "", Directory = "", LeftOut = ["labels.overlays.credits"] };
        vm.Builder = SceneBuilder.Generator;
        Assert.True(vm.CanEmbedMesh);
        vm.EmbedMesh = true;
        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.Equal((SceneBuilder.Generator, false), (service.LastExportOptions!.Builder, service.LastExportOptions.SingleFile));
        Assert.Equal((true, true), (service.LastExportSpec!.Source.SelfContained, service.LastExportSpec.Source.EmbedMesh));
        Assert.Equal([string.Format(Tr.Get("Blender_Builder_LeftOutGenerator"), "labels.overlays.credits")], vm.Warnings);
        vm.SelfContained = false;
        Assert.False(vm.CanEmbedMesh);
    }

    [Fact]
    public async Task OpenInBlender_OpensTheSceneThePlayerBuilds_AndSaysOnceThatItsTextsAreNotInTheWindow()
    {
        var service = new FakeRenderService { Export = new RenderExport { ScriptPath = "", Directory = "", TextsInSecondStage = true } };
        var gui = new FakeGui();
        var vm = Vm(service, gui: gui);
        vm.RenderDirectory = Path.Combine(_root, "renders");
        vm.SelfContained = true;

        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);

        // The scene of the render, to look at: built in Blender's window and left open. Never
        // one file, and a render started from the window writes where "Render" would.
        Assert.Equal((SceneBuilder.Auto, true, false), (service.LastExportOptions!.Builder, service.LastExportOptions.Interactive, service.LastExportOptions.SingleFile));
        Assert.Equal(Path.Combine(_root, "renders"), service.LastExportSpec!.Output.OutputDirectory);
        Assert.Equal(vm.CurrentSpec.Labels, service.LastExportSpec.Labels);
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "ThIDE", "blender-gui"), service.LastExportDirectory);
        Assert.Equal(Path.Combine(service.LastExportDirectory!, "render.py"), gui.Launched);
        Assert.Equal(["--build-only"], gui.Arguments);
        Assert.Equal(Tr.Get("Blender_Status_OpenedInBlender"), vm.StatusMessage);
        Assert.Equal([Tr.Get("Blender_OpenInBlender_TextsNote")], vm.Warnings);
        Assert.False(vm.IsBusy);

        // Opened again, it is said again once, not twice; a scene without texts says nothing.
        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);
        Assert.Single(vm.Warnings);
        service.Export = new RenderExport { ScriptPath = "", Directory = "" };
        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);
        Assert.Empty(vm.Warnings);

        // The script generator, when it is chosen: its own script, with nothing after it.
        service.Export = new RenderExport { ScriptPath = "", Directory = "", LeftOut = ["annotations"] };
        vm.Builder = SceneBuilder.Generator;
        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);
        Assert.Equal((SceneBuilder.Generator, true), (service.LastExportOptions!.Builder, service.LastExportOptions.Interactive));
        Assert.Empty(gui.Arguments!);
        Assert.Equal([string.Format(Tr.Get("Blender_Builder_LeftOutGenerator"), "annotations")], vm.Warnings);
    }

    [Fact]
    public async Task AnExportThatCannotBeMade_SaysWhy()
    {
        var vm = new BlenderAnimationViewModel(new Refusing(), new FakeSources(), null, gui: new FakeGui())
        {
            UseExternalFile = true,
            ExternalFilePath = "m.lox",
            ExportDirectory = Path.Combine(_root, "exports"),
        };

        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.Equal("The station 'main99' is not in the model.", vm.LastError);
        Assert.Equal(Tr.Get("Blender_Status_Failed"), vm.StatusMessage);
        Assert.Empty(vm.Outputs);
        Assert.False(vm.IsBusy);

        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);
        Assert.Equal("The station 'main99' is not in the model.", vm.LastError);
        Assert.False(vm.IsBusy);
    }

    private sealed class Refusing : IBlenderRenderService
    {
        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => throw new NotSupportedException();

        public Task<RenderExport> ExportAsync(SceneSpec spec, RenderSource source, string outputDir, ExportOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => Task.FromException<RenderExport>(new ArgumentException("The station 'main99' is not in the model."));
    }
}
