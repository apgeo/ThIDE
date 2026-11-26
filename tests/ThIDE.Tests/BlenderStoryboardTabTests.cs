// The Storyboard tab of the Blender panel, on its ViewModel: shots added from a preset or a
// composite preset, reordered and removed; the settings editing the selected shot; Draft all,
// Draft shot and Render going to the render service as the panel's, with the estimate shown
// first; the queue listed with a cancel, and a render a restart resumed told apart from an agent's.
// A fake render service; no Blender, no view.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class BlenderStoryboardTabTests
{
    private sealed class FilmService : IBlenderRenderService, IRenderEstimator
    {
        public Storyboard? LastStoryboard { get; private set; }
        public RenderOptions? LastOptions { get; private set; }
        public List<string> Calls { get; } = [];

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => throw new System.InvalidOperationException("the tab renders films");

        public Task<RenderResult> RenderStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            Calls.Add("render");
            LastStoryboard = storyboard;
            LastOptions = options;
            return Task.FromResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/film.mp4"), FrameCount = 3 });
        }

        public Task<RenderEstimate> EstimateStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, CancellationToken ct = default)
        {
            Calls.Add("estimate");
            return Task.FromResult(new RenderEstimate
            {
                Frames = 900, SecondsPerFrame = 2, Measured = false, DiskBytes = 1_500_000_000, MemoryBytes = 2_000_000_000, Triangles = 1000, TextObjects = 2,
            });
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => Task.FromResult(Path.Combine(outputDir, "render.py"));
    }

    private sealed class Sources : IBlenderSourceProvider
    {
        public bool HasProject => false;
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => [];
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []));
    }

    private static BlenderAnimationViewModel Vm(FilmService? service = null, PresetStore? store = null, RenderQueue? queue = null)
        => new(service ?? new FilmService(), new Sources(), store, queue: queue)
        {
            UseExternalFile = true, ExternalFilePath = "m.lox", RenderDirectory = "", PanelTab = BlenderAnimationViewModel.StoryboardTab,
        };

    private static void Add(BlenderAnimationViewModel vm, string preset)
    {
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == preset);
        vm.AddShotCommand.Execute(null);
    }

    [Fact]
    public void Shots_are_added_from_presets_reordered_and_removed()
    {
        var vm = Vm();
        Add(vm, "Orbit showcase");
        vm.SelectedShot = null;
        Add(vm, "Helix descent");
        Assert.Equal(["Orbit showcase", "Helix descent"], vm.Shots.Select(s => s.Name));
        Assert.Equal(TransitionKind.Dissolve, vm.Shots[1].TransitionKind);
        Assert.Equal("orbit", vm.Shots[0].Kind.ToLowerInvariant());
        // 12 s and 15 s, the second dissolving in over a second of the first.
        Assert.Equal(12 + 15 - 1, vm.FilmSeconds, 3);
        Assert.Equal((0.0, 12 / 26.0), (vm.Shots[0].StripStart, vm.Shots[0].StripEnd), new ToleranceComparer());
        Assert.Equal((11 / 26.0, 1.0), (vm.Shots[1].StripStart, vm.Shots[1].StripEnd), new ToleranceComparer());

        vm.MoveShotUpCommand.Execute(vm.Shots[1]);
        Assert.Equal(["Helix descent", "Orbit showcase"], vm.Shots.Select(s => s.Name));
        // The first shot begins the film.
        Assert.Null(vm.Shots[0].Shot.In);
        Assert.Equal(1, vm.Shots[0].Number);
        Assert.Equal("Blender.Shot.2.Remove", vm.Shots[1].RemoveId);
        Assert.Empty(vm.StoryboardBlockers);

        vm.RemoveShotCommand.Execute(vm.Shots[0]);
        Assert.Equal(["Orbit showcase"], vm.Shots.Select(s => s.Name));
        Assert.Null(vm.Shots[0].Shot.In);
    }

    [Fact]
    public void The_settings_edit_the_selected_shot()
    {
        var vm = Vm();
        Add(vm, "Orbit showcase");
        vm.SelectedShot = null;
        Add(vm, "Helix descent");
        vm.SelectedShot = vm.Shots[1];
        Assert.Equal(CameraTemplate.Helix, vm.CameraTemplate);
        Assert.Equal(15, vm.DurationSeconds);

        vm.DurationSeconds = 6;
        vm.ShowStationLabels = true;
        var shot = vm.Shots[1].Shot;
        Assert.Null(shot.Preset);
        Assert.Equal(6, shot.Seconds);
        Assert.Equal(CameraTemplate.Helix, shot.Camera!.Template);
        Assert.True(shot.Labels!.Stations.Show);
        // The other shot is as it was, and the film is still valid.
        Assert.Equal("Orbit showcase", vm.Shots[0].Shot.Preset);
        Assert.Empty(vm.StoryboardBlockers);

        vm.SelectedShot = vm.Shots[0];
        Assert.Equal(CameraTemplate.Orbit, vm.CameraTemplate);
        Assert.Equal(6, vm.Shots[1].Seconds);
    }

    [Fact]
    public void A_composite_adds_its_shots_and_the_films_texts()
    {
        var vm = Vm();
        vm.SelectedComposite = vm.Composites.Single(c => c.Name == "Grand tour");
        vm.AddCompositeCommand.Execute(null);
        Assert.Equal(["Outside", "Tour"], vm.Shots.Select(s => s.Name));
        Assert.Equal(Tr.Get("Blender_Shot_Tour"), vm.Shots[1].Kind);
        Assert.Equal("{cave}: grand tour", vm.CurrentStoryboard.Name);
        Assert.NotNull(vm.CurrentStoryboard.Texts!.EndCard);
        Assert.Empty(vm.StoryboardBlockers);

        // A second one is joined by a dip.
        vm.SelectedComposite = vm.Composites.Single(c => c.Name == "Showcase");
        vm.AddCompositeCommand.Execute(null);
        Assert.Equal(5, vm.Shots.Count);
        Assert.Equal(TransitionKind.Dip, vm.Shots[2].TransitionKind);
    }

    [Fact]
    public async Task Draft_all_and_draft_shot_go_to_the_service_as_the_panels()
    {
        var service = new FilmService();
        var vm = Vm(service);
        Add(vm, "Orbit showcase");
        vm.SelectedShot = null;
        Add(vm, "Helix descent");
        Assert.True(vm.DraftAllCommand.CanExecute(null));

        await vm.DraftAllCommand.ExecuteAsync(null);
        Assert.Equal(2, service.LastStoryboard!.Shots.Count);
        Assert.Equal(("panel", true), (service.LastOptions!.Owner, service.LastOptions.Draft));
        Assert.Equal(["render"], service.Calls);

        vm.SelectedShot = vm.Shots[1];
        await vm.DraftShotCommand.ExecuteAsync(null);
        var alone = Assert.Single(service.LastStoryboard!.Shots);
        Assert.Null(alone.In);
        Assert.EndsWith(".s02", service.LastStoryboard.Output.BaseName, System.StringComparison.Ordinal);
        Assert.Contains("out/film.mp4", vm.Outputs);
    }

    [Fact]
    public async Task Render_shows_the_estimate_before_it_renders()
    {
        var service = new FilmService();
        var vm = Vm(service);
        Add(vm, "Orbit showcase");

        await vm.RenderFilmCommand.ExecuteAsync(null);

        Assert.Equal(["estimate", "render"], service.Calls);
        Assert.False(service.LastOptions!.Draft);
        Assert.Contains("900", vm.FilmEstimate, System.StringComparison.Ordinal);
        Assert.Contains("0:30:00", vm.FilmEstimate, System.StringComparison.Ordinal);

        await vm.EstimateFilmCommand.ExecuteAsync(null);
        Assert.Equal(["estimate", "render", "estimate"], service.Calls);
    }

    [Fact]
    public async Task A_users_preset_is_written_into_the_shot_before_the_film_is_queued()
    {
        string dir = Path.Combine(Path.GetTempPath(), "thide-storyboard-tab-" + System.Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new PresetStore(dir);
            store.Save(BuiltInPresets.HelixDescent() with { Name = "My helix" });
            store.SaveComposite(BuiltInComposites.Showcase() with { Name = "My film" });
            var service = new FilmService();
            var vm = Vm(service, store);
            Assert.Contains(vm.Composites, c => c.Name == "My film");
            Add(vm, "My helix");

            await vm.DraftAllCommand.ExecuteAsync(null);

            var shot = Assert.Single(service.LastStoryboard!.Shots);
            Assert.Null(shot.Preset);
            Assert.Equal(CameraTemplate.Helix, shot.Camera!.Template);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task The_queue_is_listed_with_a_cancel_and_a_resumed_render_is_not_an_agents()
    {
        var queue = new RenderQueue();
        var vm = Vm(queue: queue);
        var release = new TaskCompletionSource<RenderResult>();
        var resumed = queue.RunAsync(new RenderJobRequest { Name = "film", Owner = RenderQueue.RestartOwner }, (_, ct) => release.Task.WaitAsync(ct));

        Assert.True(vm.ResumedRendering);
        Assert.False(vm.AgentRendering);
        Assert.Equal(Tr.Get("Blender_Status_RestartRendering"), vm.StatusMessage);
        var row = Assert.Single(vm.QueueRows);
        Assert.Equal(("film", Tr.Get("Blender_Queue_OwnerRestart"), Tr.Get("Blender_Queue_Running"), true), (row.Name, row.Owner, row.State, row.CanCancel));
        Assert.Equal("Blender.Queue.1.Cancel", row.CancelId);

        vm.CancelQueuedCommand.Execute(row);
        try { await resumed; } catch (System.OperationCanceledException) { }
        for (int i = 0; i < 200 && vm.QueueRows.Any(r => r.CanCancel); i++) await Task.Delay(10);
        Assert.Equal(Tr.Get("Blender_Status_Cancelled"), Assert.Single(vm.QueueRows).State);
    }

    private sealed class ToleranceComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) a, (double, double) b) => System.Math.Abs(a.Item1 - b.Item1) < 1e-9 && System.Math.Abs(a.Item2 - b.Item2) < 1e-9;
        public int GetHashCode((double, double) obj) => 0;
    }
}
