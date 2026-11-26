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
using Therion.Cave3D;
using ThIDE.Resources;
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
        public RenderOptions? LastOptions { get; private set; }
        public int RenderCalls { get; private set; }

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastSpec = spec;
            LastOptions = options;
            RenderCalls++;
            progress?.Report(new RenderProgress(RenderPhase.Rendering, "rendering", 0.5, 1, 2, Device: "CPU"));
            return Task.FromResult(Result);
        }

        public SceneSpec? LastExportSpec { get; private set; }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
        {
            LastExportSpec = spec;
            return Task.FromResult(Path.Combine(outputDir, "render.py"));
        }
    }

    private sealed class FakeSources : IBlenderSourceProvider
    {
        public List<ModelArtifact> Discovered { get; } = [];
        public bool Project { get; init; }
        public ModelSourceRequest? LastRequest { get; private set; }
        public bool HasProject => Project;
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => Discovered;
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(
                new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []))
            .ContinueWith(t => { LastRequest = request; return t.Result; }, TaskScheduler.Default);
    }

    private static BlenderAnimationViewModel Vm(FakeRenderService? svc = null, FakeSources? src = null, PresetStore? store = null)
        => new(svc ?? new FakeRenderService(), src ?? new FakeSources(), store);

    private static BlenderAnimationViewModel WithSource(BlenderAnimationViewModel vm)
    {
        vm.UseExternalFile = true;
        vm.ExternalFilePath = "m.lox";
        return vm;
    }

    // ---- source ----

    [Fact]
    public async Task With_a_project_open_the_projects_model_comes_first_and_renders_from_the_workspace()
    {
        var src = new FakeSources { Project = true };
        src.Discovered.Add(new ModelArtifact("/invented/cave.lox", CaveSourceFormat.Lox, 10, System.DateTimeOffset.UnixEpoch));
        var vm = Vm(src: src);

        vm.ReloadArtifactsCommand.Execute(null);

        Assert.Equal(2, vm.Artifacts.Count);
        Assert.Null(vm.Artifacts[0].Artifact);
        Assert.Equal(vm.Artifacts[0], vm.SelectedArtifact);
        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Null(src.LastRequest!.ExternalFilePath);
        Assert.True(src.LastRequest.AllowReExport);

        vm.SelectedArtifact = vm.Artifacts[1];
        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal("/invented/cave.lox", src.LastRequest!.ExternalFilePath);
    }

    [Fact]
    public void Without_a_project_only_the_build_outputs_are_listed()
    {
        var src = new FakeSources();
        src.Discovered.Add(new ModelArtifact("/invented/cave.lox", CaveSourceFormat.Lox, 10, System.DateTimeOffset.UnixEpoch));
        var vm = Vm(src: src);

        vm.ReloadArtifactsCommand.Execute(null);

        Assert.NotNull(Assert.Single(vm.Artifacts).Artifact);
    }

    // ---- render folder ----

    [Fact]
    public async Task Render_WritesIntoTheRenderFolder_NotTheTemporaryJobFolder()
    {
        var svc = new FakeRenderService();
        var vm = WithSource(Vm(svc));
        Assert.Equal(BlenderAnimationViewModel.DefaultRenderDirectory(), vm.RenderDirectory);
        Assert.False(vm.RenderDirectory!.StartsWith(Path.GetTempPath(), System.StringComparison.Ordinal));

        vm.RenderDirectory = " /invented/renders ";
        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Equal("/invented/renders", svc.LastSpec!.Output.OutputDirectory);
    }

    [Fact]
    public async Task Render_WithAnEmptyRenderFolder_LeavesTheOutputsInTheJobFolder()
    {
        var svc = new FakeRenderService();
        var vm = WithSource(Vm(svc));

        vm.RenderDirectory = "";
        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Equal("", svc.LastSpec!.Output.OutputDirectory);
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

    [Fact]
    public async Task An_agents_render_does_not_block_the_panel_and_lands_in_the_history()
    {
        var queue = new Therion.Blender.Execution.RenderQueue();
        var vm = WithSource(new BlenderAnimationViewModel(new FakeRenderService(), new FakeSources(), null, queue: queue));
        Assert.True(vm.RenderCommand.CanExecute(null));

        var release = new TaskCompletionSource<RenderResult>();
        var agent = queue.RunAsync(new RenderJobRequest { Name = "orbit (agent r1)", Owner = "agent" }, (_, _) => release.Task);
        Assert.True(vm.AgentRendering);
        // The panel's render would wait for the agent's in the queue instead of being refused.
        Assert.True(vm.RenderCommand.CanExecute(null));

        release.SetResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/orbit.mp4") });
        await agent;
        for (int i = 0; i < 200 && vm.Jobs.Count == 0; i++) await Task.Delay(10);  // the history hears it on the UI's context
        Assert.False(vm.AgentRendering);
        var entry = Assert.Single(vm.Jobs);
        Assert.Equal("orbit (agent r1)", entry.Name);
        Assert.True(entry.Succeeded);

        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Jobs.Count);
    }

    [Fact]
    public async Task ASelfContainedScript_CarriesTheMeshesOnlyWhenAskedTo_AndNeverWithoutBeingSelfContained()
    {
        var svc = new FakeRenderService();
        var vm = WithSource(Vm(svc));
        vm.ExportDirectory = "exports";
        Assert.False(vm.SelfContained);
        Assert.False(vm.EmbedMesh);

        // Self-contained, the meshes beside it: as the one option did before.
        vm.SelfContained = true;
        Assert.Equal((true, false), (vm.CurrentSpec.Source.SelfContained, vm.CurrentSpec.Source.EmbedMesh));
        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.Equal((true, false), (svc.LastExportSpec!.Source.SelfContained, svc.LastExportSpec.Source.EmbedMesh));

        // The meshes inside it too: one file.
        vm.EmbedMesh = true;
        Assert.Equal((true, true), (vm.CurrentSpec.Source.SelfContained, vm.CurrentSpec.Source.EmbedMesh));
        Assert.Empty(vm.Blockers);
        await vm.ExportScriptCommand.ExecuteAsync(null);
        Assert.Equal((true, true), (svc.LastExportSpec!.Source.SelfContained, svc.LastExportSpec.Source.EmbedMesh));

        // Not self-contained, the meshes cannot be inside it: the second option goes off with the first.
        vm.SelfContained = false;
        Assert.False(vm.EmbedMesh);
        Assert.Equal((false, false), (vm.CurrentSpec.Source.SelfContained, vm.CurrentSpec.Source.EmbedMesh));
        vm.EmbedMesh = true;
        Assert.False(vm.CurrentSpec.Source.EmbedMesh);
        Assert.Empty(vm.Blockers);

        // Both come back from a preset as it has them: a built-in one has neither.
        vm.SelfContained = true;
        vm.EmbedMesh = true;
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.Equal((false, false), (vm.SelfContained, vm.EmbedMesh));
        Assert.Equal((false, false), (vm.CurrentSpec.Source.SelfContained, vm.CurrentSpec.Source.EmbedMesh));
    }

    [Fact]
    public void LeadMarkerSize_AndLabelRoof_ReachTheSpec_AndComeBackFromAPreset()
    {
        var vm = Vm();
        vm.LeadMarkerSizing = LeadMarkerSizing.CameraDistance;
        vm.LabelRoof = LabelRoofSource.WallsOnly;
        Assert.Equal(LeadMarkerSizing.CameraDistance, vm.CurrentSpec.Labels.Leads.Sizing);
        Assert.Equal(LabelRoofSource.WallsOnly, vm.CurrentSpec.Labels.Roof);

        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.Equal(LeadMarkerSizing.Auto, vm.LeadMarkerSizing);
        Assert.Equal(LabelRoofSource.SurveyFirst, vm.LabelRoof);
    }

    [Fact]
    public void Terrain_IsOffByDefault_ReachesTheSpec_AndComesBackFromAPreset()
    {
        var vm = Vm();
        Assert.Equal(SurfaceMode.Off, vm.SurfaceMode);
        Assert.Equal(System.Enum.GetValues<SurfaceMode>(), vm.SurfaceModes);

        vm.SurfaceMode = SurfaceMode.Textured;
        Assert.Equal(SurfaceMode.Textured, vm.CurrentSpec.Surface.Mode);

        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.Equal(SurfaceMode.Off, vm.SurfaceMode);
        Assert.Equal(SurfaceMode.Off, vm.CurrentSpec.Surface.Mode);
    }

    [Fact]
    public void TerrainOpacity_IsUsableOnlyWithATerrain_ReachesTheSpec_AndComesBackFromAPreset()
    {
        var vm = Vm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(vm.HasSurface);
        Assert.Equal(0.4, vm.SurfaceOpacity);

        vm.SurfaceMode = SurfaceMode.Solid;
        Assert.True(vm.HasSurface);
        Assert.Contains(nameof(vm.HasSurface), changed);

        // A slider's step lands a little off a whole percent; the spec keeps the percent.
        vm.SurfaceOpacity = 0.1 + 0.2 + 0.4;
        Assert.Equal(0.7, vm.CurrentSpec.Surface.Opacity);
        Assert.Equal(SurfaceMode.Solid, vm.CurrentSpec.Surface.Mode);

        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.Equal(0.4, vm.SurfaceOpacity);
        Assert.False(vm.HasSurface);
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
    public async Task Render_LeavesTheBuilderToBeChosen_UnlessOneIsPicked_AndIsNotADraft()
    {
        var svc = new FakeRenderService();
        var vm = WithSource(Vm(svc));
        Assert.Equal([SceneBuilder.Auto, SceneBuilder.Player, SceneBuilder.Generator], vm.Builders);
        Assert.Equal(SceneBuilder.Auto, vm.Builder);

        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal(new RenderOptions { Owner = "panel" }, svc.LastOptions);

        vm.Builder = SceneBuilder.Generator;
        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal(new RenderOptions { Builder = SceneBuilder.Generator, Owner = "panel" }, svc.LastOptions);
        // The builder is how a render is made, not what it shows: a preset neither holds nor changes it.
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Helix descent");
        Assert.Equal(SceneBuilder.Generator, vm.Builder);
    }

    [Fact]
    public async Task Preview_RendersADraft_OfTheSameScene_AndSaysSo()
    {
        var svc = new FakeRenderService
        {
            Result = new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/render-draft.mp4"), Builder = SceneBuilder.Player },
        };
        var vm = WithSource(Vm(svc));
        vm.Builder = SceneBuilder.Generator;
        Assert.True(vm.PreviewCommand.CanExecute(null));

        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Equal(new RenderOptions { Builder = SceneBuilder.Generator, Draft = true, Owner = "panel" }, svc.LastOptions);
        Assert.Equal(vm.CurrentSpec.Camera, svc.LastSpec!.Camera);
        Assert.Contains("out/render-draft.mp4", vm.Outputs);
        Assert.Equal(
            (string.Format(Tr.Get("Blender_Job_Preview"), "cave-render"), Tr.Get("Blender_Status_PreviewDone")),
            (vm.Jobs[0].Name, vm.Jobs[0].Status));
        Assert.False(vm.IsBusy);

        // Nothing to preview without a model, as there is nothing to render.
        var empty = Vm(svc);
        Assert.False(empty.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheResult_SaysWhichBuilderBuiltTheScene_WhenItWasNotThePlayer_AndWhatIsNotShown()
    {
        var svc = new FakeRenderService
        {
            Result = new RenderResult
            {
                Succeeded = true, OutputPaths = ImmutableArray.Create("out/render.mp4"), Builder = SceneBuilder.Generator,
                NotCarried = ["lighting.hdriPath"], LeftOut = ["labels.overlays.credits", "labels.overlays.endCard"], Warnings = ["a notice"],
            },
        };
        var vm = WithSource(Vm(svc));

        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "a notice",
                string.Format(Tr.Get("Blender_Builder_Fallback"), "lighting.hdriPath"),
                string.Format(Tr.Get("Blender_Builder_LeftOutGenerator"), "labels.overlays.credits, labels.overlays.endCard"),
            ],
            vm.Warnings);

        // A preview that cannot show everything says what it leaves out; a plain result says nothing.
        svc.Result = new RenderResult { Succeeded = true, Builder = SceneBuilder.Player, LeftOut = ["lighting.hdriPath"] };
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.Equal([string.Format(Tr.Get("Blender_Builder_LeftOutPreview"), "lighting.hdriPath")], vm.Warnings);
        svc.Result = new RenderResult { Succeeded = true, Builder = SceneBuilder.Player };
        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Empty(vm.Warnings);
    }

    [Fact]
    public async Task ARenderThePlayerRefuses_SaysWhatItDoesNotCarry_InTheUsersLanguage()
    {
        var svc = new FakeRenderService
        {
            Result = new RenderResult
            {
                FailureKind = RenderFailureKind.BuilderRefused, ErrorMessage = "The player cannot build this scene: …",
                NotCarried = ["lighting.hdriPath", "labels.events"],
            },
        };
        var vm = WithSource(Vm(svc));
        vm.Builder = SceneBuilder.Player;

        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(Tr.Get("Blender_Fail_BuilderRefused"), "lighting.hdriPath, labels.events"), vm.LastError);
        Assert.Empty(vm.Outputs);
    }

    [Fact]
    public void Credits_AndAnEndCard_ReachTheSpec_AndComeBackFromAPreset()
    {
        var store = new PresetStore(Path.Combine(Path.GetTempPath(), "thide-vm-texts-" + Guid.NewGuid().ToString("N")));
        try
        {
            var vm = Vm(store: store);
            Assert.Equal(("", "", 4.0), (vm.Credits, vm.EndCardText, vm.EndCardSeconds));
            Assert.Null(vm.CurrentSpec.Labels.Overlays.Credits);
            Assert.Null(vm.CurrentSpec.Labels.Overlays.EndCard);

            vm.Credits = "  Survey: the invented team ";
            vm.EndCardText = "The end";
            vm.EndCardSeconds = 6;
            Assert.Equal("Survey: the invented team", vm.CurrentSpec.Labels.Overlays.Credits);
            Assert.Equal(new EndCardSpec { Text = "The end", Seconds = 6 }, vm.CurrentSpec.Labels.Overlays.EndCard);
            Assert.Empty(vm.Blockers.Where(b => b.StartsWith("labels.", StringComparison.Ordinal)));

            vm.NewPresetName = "With texts";
            vm.SavePresetCommand.Execute(null);
            vm.Credits = "";
            vm.EndCardText = " ";
            Assert.Null(vm.CurrentSpec.Labels.Overlays.Credits);
            Assert.Null(vm.CurrentSpec.Labels.Overlays.EndCard);

            vm.SelectedPreset = vm.Presets.Single(p => p.Name == "With texts");
            Assert.Equal(("Survey: the invented team", "The end", 6.0), (vm.Credits, vm.EndCardText, vm.EndCardSeconds));
            // What the panel has no field for stays in the scene: an end card's subtitle. (And
            // the title, which has one now, is the preset's.)
            vm.SelectedPreset = new PresetItem("by hand", null, false, new RenderPreset
            {
                Name = "by hand",
                Spec = vm.CurrentSpec with
                {
                    Labels = new LabelsSpec
                    {
                        Overlays = new OverlaySpec { Title = "Invented Cave", EndCard = new EndCardSpec { Text = "End", Subtitle = "thanks", Seconds = 2 } },
                    },
                },
            });
            vm.EndCardSeconds = 3;
            Assert.Equal(new EndCardSpec { Text = "End", Subtitle = "thanks", Seconds = 3 }, vm.CurrentSpec.Labels.Overlays.EndCard);
            Assert.Equal("Invented Cave", vm.CurrentSpec.Labels.Overlays.Title);

            // A time that is no time is said before the render, not by Blender.
            vm.EndCardSeconds = 0;
            Assert.Contains(vm.Blockers, b => b.StartsWith("labels.overlays.endCard.seconds", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(store.Directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task TheSecondStage_IsShownAsComposing()
    {
        var svc = new ComposingService();
        var vm = WithSource(new BlenderAnimationViewModel(svc, new FakeSources(), null));
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.StatusMessage)) seen.Add(vm.StatusMessage); };

        await vm.RenderCommand.ExecuteAsync(null);
        await Task.Delay(50);   // Progress<T> posts its reports

        Assert.Contains($"{Tr.Get("Blender_Phase_Composing")} 3/8", seen);
        Assert.Contains($"{Tr.Get("Blender_Phase_Rendering")} 2/4", seen);
    }

    private sealed class ComposingService : IBlenderRenderService
    {
        public async Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new RenderProgress(RenderPhase.Rendering, "rendering", 0.5, 2, 4));
            await Task.Delay(20, ct);
            progress?.Report(new RenderProgress(RenderPhase.Composing, "composing", 0.4, 3, 8));
            await Task.Delay(20, ct);
            return new RenderResult { Succeeded = true };
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => Task.FromResult(Path.Combine(outputDir, "render.py"));
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
    public async Task Render_WhenBlenderCannotBeStarted_NamesThePathWithoutRawExceptionOrStoreHint()
    {
        var svc = new FakeRenderService
        {
            Result = new RenderResult
            {
                Succeeded = false, FailureKind = RenderFailureKind.Crashed, StartFailedPath = "/nonexistent/blender",
                ErrorMessage = "Could not start Blender at '/nonexistent/blender': An error occurred trying to start process",
            },
        };
        var vm = WithSource(Vm(svc));

        await vm.RenderCommand.ExecuteAsync(null);

        Assert.Contains("/nonexistent/blender", vm.LastError);
        Assert.DoesNotContain("Store", vm.LastError);
        Assert.DoesNotContain("An error occurred", vm.LastError);
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
