// ViewModel tests for the Blender Animation panel (BA-B12). Exercises preset application,
// knob → SceneSpec projection, validation → blockers, source gating, and the render/export/
// save-preset commands with a fake render service + source provider (no Blender, no view).

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class BlenderAnimationViewModelTests
{
    private sealed class FakeRenderService : IBlenderRenderService
    {
        public RenderResult Result { get; set; } = new()
        {
            Succeeded = true,
            OutputPaths = ImmutableArray.Create("out/render.mp4"),
            Device = "CPU",
            FrameCount = 2,
        };
        public SceneSpec? LastSpec { get; private set; }
        public int RenderCalls { get; private set; }

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastSpec = spec;
            RenderCalls++;
            progress?.Report(new RenderProgress(RenderPhase.Rendering, "rendering", 0.5, 1, 2, Device: "CPU"));
            return Task.FromResult(Result);
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(Path.Combine(outputDir, "render.py"));
    }

    private sealed class FakeSources : IBlenderSourceProvider
    {
        public List<ModelArtifact> Discovered { get; } = [];
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => Discovered;
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(
                new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []));
    }

    private static BlenderAnimationViewModel Vm(FakeRenderService? svc = null, FakeSources? src = null, PresetStore? store = null)
        => new(svc ?? new FakeRenderService(), src ?? new FakeSources(), store);

    private static BlenderAnimationViewModel WithSource(BlenderAnimationViewModel vm)
    {
        vm.UseExternalFile = true;
        vm.ExternalFilePath = "m.lox";
        return vm;
    }

    // ---- presets + knobs ----

    [Fact]
    public void Constructor_LoadsBuiltInPresets_AndDefaultKnobs()
    {
        var vm = Vm();
        Assert.Equal(BuiltInPresets.All.Count, vm.Presets.Count(p => p.BuiltIn));
        Assert.Equal(CameraTemplate.Orbit, vm.CameraTemplate); // OrbitShowcase default
        Assert.Equal(OutputKind.Video, vm.OutputKind);
    }

    [Fact]
    public void SelectingPreset_AppliesItsPresentation()
    {
        var vm = Vm();
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Documentation stills");
        Assert.Equal(CameraTemplate.StillSet, vm.CameraTemplate);
        Assert.Equal(OutputKind.FrameSequence, vm.OutputKind);
        Assert.Equal(CameraTemplate.StillSet, vm.CurrentSpec.Camera.Template);
    }

    [Fact]
    public void EditingKnob_ProjectsOntoSpec_AndFrameCount()
    {
        var vm = Vm();
        vm.Fps = 24;
        vm.DurationSeconds = 5;
        vm.Width = 1280;
        Assert.Equal(1280, vm.CurrentSpec.Output.Width);
        Assert.Equal(120, vm.EstimatedFrameCount); // 24 × 5
    }

    [Fact]
    public void ShowStationLabels_TogglesTheLabelSpec()
    {
        var vm = Vm();
        vm.ShowStationLabels = true;
        Assert.True(vm.CurrentSpec.Labels.Stations.Show);
    }

    // ---- validation + gating ----

    [Fact]
    public void InvalidSamples_ProduceABlocker_AndBlockRender()
    {
        var vm = WithSource(Vm());
        Assert.True(vm.RenderCommand.CanExecute(null)); // valid + has source

        vm.Samples = 0;
        Assert.Contains(vm.Blockers, b => b.StartsWith("engine.samples"));
        Assert.False(vm.RenderCommand.CanExecute(null));

        vm.Samples = 64;
        Assert.DoesNotContain(vm.Blockers, b => b.StartsWith("engine.samples"));
    }

    [Fact]
    public void NoSource_IsABlocker()
    {
        var vm = Vm(); // no source selected
        Assert.Contains(vm.Blockers, b => b == ThIDE.Resources.Tr.Get("Blender_Blocker_NoSource"));
        Assert.False(vm.RenderCommand.CanExecute(null));
    }

    [Fact]
    public void StillSetWithVideoOutput_IsABlocker()
    {
        var vm = WithSource(Vm());
        vm.CameraTemplate = CameraTemplate.StillSet; // Video output ⇒ invalid (needs FrameSequence)
        Assert.Contains(vm.Blockers, b => b.StartsWith("output.kind"));
    }

    // ---- commands ----

    [Fact]
    public async Task Render_Success_PopulatesOutputs_AndClearsBusy()
    {
        var svc = new FakeRenderService();
        var vm = WithSource(Vm(svc));

        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Equal(1, svc.RenderCalls);
        Assert.False(vm.IsBusy);
        Assert.Contains("out/render.mp4", vm.Outputs);
        Assert.Equal("CPU", vm.Device);
        Assert.Null(vm.LastError);
        // The spec the service received reflects the edited knobs.
        Assert.Equal(CameraTemplate.Orbit, svc.LastSpec!.Camera.Template);
    }

    [Fact]
    public async Task Render_Failure_SurfacesTheError()
    {
        // A generic (non-Blender-install) failure surfaces the service's own message verbatim.
        // The BlenderNotFound/TooOld localized-message path is covered by BlenderPanelB13Tests.
        var svc = new FakeRenderService
        {
            Result = new RenderResult { Succeeded = false, FailureKind = RenderFailureKind.ScriptError, ErrorMessage = "PLY import produced no object" },
        };
        var vm = WithSource(Vm(svc));

        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Equal("PLY import produced no object", vm.LastError);
        Assert.Empty(vm.Outputs);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Export_WritesScriptPath()
    {
        var vm = WithSource(Vm());
        vm.ExportDirectory = "C:/exports";
        Assert.True(vm.ExportScriptCommand.CanExecute(null));

        await vm.ExportScriptCommand.ExecuteAsync(null);

        Assert.Contains(vm.Outputs, o => o.EndsWith("render.py"));
    }

    [Fact]
    public void SavePreset_PersistsAUserPreset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thide-vm-presets-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PresetStore(dir);
            var vm = Vm(store: store);
            vm.NewPresetName = "My Cave";
            Assert.True(vm.SavePresetCommand.CanExecute(null));

            vm.SavePresetCommand.Execute(null);

            Assert.Contains(vm.Presets, p => p is { Name: "My Cave", BuiltIn: false });
            Assert.Single(new PresetStore(dir).Load());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Artifacts_AreDiscovered_IntoTheDropdown()
    {
        var src = new FakeSources();
        src.Discovered.Add(new ModelArtifact("/b/cave.lox", CaveSourceFormat.Lox, 1234, System.DateTimeOffset.UtcNow));
        var vm = Vm(src: src);
        Assert.Single(vm.Artifacts);
        Assert.Contains("cave.lox", vm.Artifacts[0].DisplayName);
    }
}
