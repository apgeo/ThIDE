// The texts of the Blender Animation panel: the title, and the captions as rows (a text, the
// seconds it is shown from and to) with the strip that shows them along the clip. The view model
// is driven as the view drives it; no view, no Blender. The captions are kept the way the
// panel keeps its other settings: in the scene it edits, so a saved preset has them.

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
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.Views.Docking;

namespace ThIDE.Tests;

public class BlenderCaptionPanelTests : IDisposable
{
    private readonly string _presets = Path.Combine(Path.GetTempPath(), "thide-vm-captions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_presets)) Directory.Delete(_presets, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeRenderService : IBlenderRenderService
    {
        public SceneSpec? LastSpec { get; private set; }
        public RenderOptions? LastOptions { get; private set; }

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastSpec = spec;
            LastOptions = options;
            return Task.FromResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/render.mp4"), FrameCount = 2 });
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => Task.FromResult(Path.Combine(outputDir, "render.py"));
    }

    private sealed class FakeSources : IBlenderSourceProvider
    {
        public bool HasProject => false;
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => [];
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(
                new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []));
    }

    private BlenderAnimationViewModel Vm(FakeRenderService? service = null, bool store = false)
    {
        var vm = new BlenderAnimationViewModel(service ?? new FakeRenderService(), new FakeSources(), store ? new PresetStore(_presets) : null)
        {
            UseExternalFile = true,
            ExternalFilePath = "m.lox",
        };
        return vm;
    }

    private static IReadOnlyList<CaptionSpec> InTheSpec(BlenderAnimationViewModel vm) => vm.CurrentSpec.Labels.Overlays.Captions;

    private static IEnumerable<string> TextBlockers(BlenderAnimationViewModel vm)
        => vm.Blockers.Where(b => b.StartsWith("labels.", StringComparison.Ordinal));

    /// <summary>Adds a caption and types its text, as the user does.</summary>
    private static CaptionRowViewModel Add(BlenderAnimationViewModel vm, string text)
    {
        vm.AddCaptionCommand.Execute(null);
        var row = vm.Captions[^1];
        row.Text = text;
        return row;
    }

    // ---- the title ----

    [Fact]
    public void TheTitle_IsThePresets_ReachesTheSpec_AndKeepsWhatThePresetSaysOfIt()
    {
        var vm = Vm();

        // The panel opens on "Orbit showcase", which writes the cave's name at the top.
        Assert.Equal(TextPlaceholders.Cave, vm.Title);
        Assert.Equal("{cave}", vm.CurrentSpec.Labels.Overlays.Title);

        vm.Title = "  Peștera Inventată ";
        Assert.Equal("Peștera Inventată", vm.CurrentSpec.Labels.Overlays.Title);
        vm.Title = " ";
        Assert.Null(vm.CurrentSpec.Labels.Overlays.Title);
        Assert.False(vm.CurrentSpec.Labels.Overlays.Any);
        Assert.Empty(TextBlockers(vm));

        // "Helix descent" opens on a card: another title is still a card, for as long.
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Helix descent");
        Assert.Equal("{cave}", vm.Title);
        vm.Title = "Avenul Inventat";
        Assert.Equal(("Avenul Inventat", BuiltInPresets.TitleCardSeconds), (vm.CurrentSpec.Labels.Overlays.Title, vm.CurrentSpec.Labels.Overlays.TitleSeconds));
    }

    // ---- adding, editing, removing ----

    [Fact]
    public void ACaption_IsAddedAfterTheLastOne_ThreeSecondsLong_AndIsInTheSceneOnceItHasAText()
    {
        var vm = Vm();
        Assert.Empty(vm.Captions);
        Assert.False(vm.HasCaptions);
        Assert.Equal(12, vm.DurationSeconds);

        vm.AddCaptionCommand.Execute(null);

        var first = Assert.Single(vm.Captions);
        Assert.Equal(("", 0.0, 3.0, 1), (first.Text, first.Start, first.End, first.Number));
        Assert.True(vm.HasCaptions);
        // A row without a text is not a caption yet: nothing in the scene, and nothing to block a render.
        Assert.Empty(InTheSpec(vm));
        Assert.False(first.IsShown);
        Assert.Empty(TextBlockers(vm));
        Assert.True(vm.RenderCommand.CanExecute(null));

        first.Text = "  Sala Mare ";
        Assert.Equal([new CaptionSpec { Text = "Sala Mare", Start = 0, End = 3 }], InTheSpec(vm));
        Assert.True(first.IsShown);

        var second = Add(vm, "Sifonul: capăt");
        Assert.Equal((3.0, 6.0, 2), (second.Start, second.End, second.Number));
        Assert.Equal(
            [new CaptionSpec { Text = "Sala Mare", Start = 0, End = 3 }, new CaptionSpec { Text = "Sifonul: capăt", Start = 3, End = 6 }],
            InTheSpec(vm));
        Assert.Empty(TextBlockers(vm));
    }

    [Fact]
    public void ANewCaption_DoesNotRunPastTheEndOfTheClip_UnlessItStartsThere()
    {
        var vm = Vm();
        vm.DurationSeconds = 7;

        var first = Add(vm, "one");
        first.End = 6;
        var second = Add(vm, "two");
        var third = Add(vm, "three");

        // One second is left of the clip; after that, a caption starts where the clip ends.
        Assert.Equal((6.0, 7.0), (second.Start, second.End));
        Assert.Equal((7.0, 10.0), (third.Start, third.End));
        // The newest goes after whichever ends last, not after the last row.
        first.End = 20.126;
        Assert.Equal(20.13, Add(vm, "four").Start);
    }

    [Fact]
    public void EditingARow_ChangesTheScene_AndRemovingOne_RenumbersTheRest()
    {
        var vm = Vm();
        var first = Add(vm, "Intrarea");
        var second = Add(vm, "Galeria");
        var third = Add(vm, "Sifonul");
        Assert.Equal(
            ["Blender.Caption.1.Text", "Blender.Caption.2.Start", "Blender.Caption.3.End", "Blender.Caption.3.Remove"],
            [first.TextId, second.StartId, third.EndId, third.RemoveId]);

        second.Start = 4.5;
        second.End = 5.25;
        second.Text = "Galeria Mare";
        Assert.Equal(new CaptionSpec { Text = "Galeria Mare", Start = 4.5, End = 5.25 }, InTheSpec(vm)[1]);

        var renumbered = new List<string?>();
        third.PropertyChanged += (_, e) => renumbered.Add(e.PropertyName);
        vm.RemoveCaptionCommand.Execute(first);

        Assert.Equal([second, third], vm.Captions);
        Assert.Equal(["Galeria Mare", "Sifonul"], InTheSpec(vm).Select(c => c.Text));
        Assert.Equal((1, 2), (second.Number, third.Number));
        Assert.Equal("Blender.Caption.2.Remove", third.RemoveId);
        // The view is told: the ids of the row's controls are bound.
        Assert.Contains(nameof(CaptionRowViewModel.Number), renumbered);
        Assert.Contains(nameof(CaptionRowViewModel.TextId), renumbered);

        // Removing what is not there, or nothing, changes nothing.
        vm.RemoveCaptionCommand.Execute(first);
        vm.RemoveCaptionCommand.Execute(null);
        Assert.Equal(2, vm.Captions.Count);

        vm.RemoveCaptionCommand.Execute(second);
        vm.RemoveCaptionCommand.Execute(third);
        Assert.Empty(vm.Captions);
        Assert.False(vm.HasCaptions);
        Assert.Empty(InTheSpec(vm));
        Assert.Equal("{cave}", vm.CurrentSpec.Labels.Overlays.Title);
    }

    [Fact]
    public void ACaptionWithoutATime_IsSaidBeforeTheRender_AndBlocksIt()
    {
        var vm = Vm();
        var row = Add(vm, "Sala Mare");
        Assert.True(vm.RenderCommand.CanExecute(null));

        row.End = row.Start;
        Assert.Contains(vm.Blockers, b => b.StartsWith("labels.overlays.captions[0].end", StringComparison.Ordinal));
        Assert.False(vm.RenderCommand.CanExecute(null));
        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.False(row.IsShown);

        row.End = 2;
        row.Start = -1;
        Assert.Contains(vm.Blockers, b => b.StartsWith("labels.overlays.captions[0].start", StringComparison.Ordinal));

        row.Start = 0.5;
        Assert.Empty(TextBlockers(vm));
        Assert.True(vm.RenderCommand.CanExecute(null));
        Assert.True(row.IsShown);
    }

    // ---- the strip ----

    [Fact]
    public void TheStrip_ShowsEachCaptionWhereItsSecondsFall_AlongTheClip()
    {
        var vm = Vm();
        Assert.Equal(12, vm.CaptionStripSeconds);
        Assert.Equal(1, vm.ClipEndOnStrip);

        var first = Add(vm, "Intrarea");
        var second = Add(vm, "Galeria");
        second.Start = 6;
        second.End = 9;
        Assert.Equal((0.0, 0.25, true), (first.StripStart, first.StripEnd, first.IsShown));
        Assert.Equal((0.5, 0.75, true), (second.StripStart, second.StripEnd, second.IsShown));

        // A shorter clip: the same seconds are further along it.
        var told = new List<string?>();
        vm.PropertyChanged += (_, e) => told.Add(e.PropertyName);
        vm.DurationSeconds = 10;
        Assert.Equal((0.6, 0.9), (second.StripStart, second.StripEnd));
        Assert.Equal(10, vm.CaptionStripSeconds);
        Assert.Contains(nameof(BlenderAnimationViewModel.CaptionStripSeconds), told);

        // A caption that runs past the end of the clip: the strip reaches as far as it does,
        // and says where the clip ends.
        second.End = 20;
        Assert.Equal(20, vm.CaptionStripSeconds);
        Assert.Equal(0.5, vm.ClipEndOnStrip);
        Assert.Equal((0.3, 1.0), (second.StripStart, second.StripEnd));
        Assert.Equal((0.0, 0.15), (first.StripStart, first.StripEnd));
        Assert.Contains(nameof(BlenderAnimationViewModel.ClipEndOnStrip), told);

        // A row that is not shown has no place on it.
        second.End = 5;
        Assert.Equal((false, 0.0, 0.0), (second.IsShown, second.StripStart, second.StripEnd));
        var blank = Add(vm, " ");
        Assert.False(blank.IsShown);
        Assert.Equal(10, vm.CaptionStripSeconds);
        Assert.Equal(1, vm.ClipEndOnStrip);
    }

    [Theory]
    [InlineData(0.25, 0.5, 200, 50, 50)]      // a quarter along, a quarter wide
    [InlineData(0, 1, 200, 0, 200)]           // the whole strip
    [InlineData(0.5, 0.501, 200, 100, 3)]     // a blink is still seen
    [InlineData(0.999, 1, 200, 197, 3)]       // and stays on the strip
    [InlineData(-1, 2, 200, 0, 200)]          // shares outside the strip are brought inside
    [InlineData(0.6, 0.2, 200, 120, 3)]       // an end before the start is no width
    [InlineData(double.NaN, double.NaN, 200, 0, 3)]
    [InlineData(0.25, 0.5, 0, 0, 0)]          // no strip yet
    [InlineData(0.25, 0.5, 2, 0, 2)]          // a strip narrower than the least width
    public void TheStripsPanel_PlacesAChildByItsShares(double start, double end, double width, double x, double childWidth)
    {
        var (at, wide) = CaptionStripPanel.Span(start, end, width);

        Assert.Equal(x, at, 6);
        Assert.Equal(childWidth, wide, 6);
    }

    // ---- kept with the scene ----

    [Fact]
    public void Captions_AreSavedWithAPreset_AndComeBackWithIt_AndAnotherPresetBringsItsOwn()
    {
        var vm = Vm(store: true);
        Add(vm, "Sala Mare").End = 2.5;
        var second = Add(vm, "Sifonul: capăt");
        second.Start = 4;
        second.End = 6;
        vm.Title = "Peștera Inventată";

        vm.NewPresetName = "Cu subtitrări";
        vm.SavePresetCommand.Execute(null);

        // In the file of the preset, as any setting of the scene is.
        var saved = Assert.Single(new PresetStore(_presets).Load());
        Assert.Equal(
            [new CaptionSpec { Text = "Sala Mare", Start = 0, End = 2.5 }, new CaptionSpec { Text = "Sifonul: capăt", Start = 4, End = 6 }],
            saved.Spec.Labels.Overlays.Captions);
        Assert.Equal("Peștera Inventată", saved.Spec.Labels.Overlays.Title);

        // A built-in preset has none: its rows replace these.
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Full flythrough");
        Assert.Empty(vm.Captions);
        Assert.False(vm.HasCaptions);
        Assert.Empty(InTheSpec(vm));
        Assert.Equal("{cave}", vm.Title);

        // The saved one brings them back, as rows that can be edited.
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Cu subtitrări");
        Assert.Equal(
            [("Sala Mare", 0.0, 2.5, 1), ("Sifonul: capăt", 4.0, 6.0, 2)],
            vm.Captions.Select(c => (c.Text, c.Start, c.End, c.Number)));
        Assert.Equal("Peștera Inventată", vm.Title);
        Assert.True(vm.Captions.All(c => c.IsShown));
        Assert.Equal((4 / 12.0, 0.5), (vm.Captions[1].StripStart, vm.Captions[1].StripEnd));
        vm.Captions[0].Text = "Sala cea Mare";
        Assert.Equal("Sala cea Mare", InTheSpec(vm)[0].Text);
        Assert.Equal("Sifonul: capăt", InTheSpec(vm)[1].Text);

        // A new view model over the same store (the panel after ThIDE is started again) has
        // them in the preset, to be chosen.
        var again = Vm(store: true);
        Assert.Empty(again.Captions);
        again.SelectedPreset = again.Presets.Single(p => p.Name == "Cu subtitrări");
        Assert.Equal(["Sala Mare", "Sifonul: capăt"], again.Captions.Select(c => c.Text));
    }

    [Fact]
    public void APresetsCaptions_ThatThePanelDidNotMake_AreItsRows_AndTheOtherTextsOfThePresetStay()
    {
        var vm = Vm();
        vm.SelectedPreset = new PresetItem("by hand", null, false, new RenderPreset
        {
            Name = "by hand",
            Spec = BuiltInPresets.OrbitShowcase().Spec with
            {
                Labels = new LabelsSpec
                {
                    Overlays = new OverlaySpec
                    {
                        Title = "Invented Cave", Subtitle = "survey 2026", ScaleBar = true, FadeToBlack = 1,
                        Captions = [new CaptionSpec { Text = "{cave}: the entrance", Start = 1, End = 2 }],
                        CornerCaptions = [new CaptionSpec { Text = "day 1", Start = 0, End = 5 }],
                    },
                },
            },
        });

        var row = Assert.Single(vm.Captions);
        Assert.Equal(("{cave}: the entrance", 1.0, 2.0), (row.Text, row.Start, row.End));

        Add(vm, "the pitch");
        var overlays = vm.CurrentSpec.Labels.Overlays;
        Assert.Equal(["{cave}: the entrance", "the pitch"], overlays.Captions.Select(c => c.Text));
        // What the panel has no field for is the preset's still.
        Assert.Equal(("Invented Cave", "survey 2026", true, 1.0), (overlays.Title, overlays.Subtitle, overlays.ScaleBar, overlays.FadeToBlack));
        Assert.Equal("day 1", Assert.Single(overlays.CornerCaptions).Text);
    }

    [Fact]
    public async Task Captions_GoToTheRender_AndToAPreview_AsTheSceneHasThem()
    {
        var service = new FakeRenderService();
        var vm = Vm(service);
        Add(vm, "Sala Mare");
        vm.AddCaptionCommand.Execute(null); // a row nobody typed into

        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal([new CaptionSpec { Text = "Sala Mare", Start = 0, End = 3 }], service.LastSpec!.Labels.Overlays.Captions);
        Assert.False(service.LastOptions!.Draft);

        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.Equal("Sala Mare", Assert.Single(service.LastSpec!.Labels.Overlays.Captions).Text);
        Assert.True(service.LastOptions!.Draft);
        // The rows are as they were after a render.
        Assert.Equal(2, vm.Captions.Count);
    }
}
