// The Blender panel's two tabs on its ViewModel: the Scene tab edits a scene of its own, which the
// Storyboard tab never changes; on the Storyboard tab the settings edit the film (frame rate,
// output, terrain, fades) and the selected shot (the rest), and nothing of a shot while none is
// selected. Also: presets that cannot be saved or deleted say why, a user's preset with a built-in
// one's name, a composite's frame rate and output, cancelling an estimate, a relative render folder,
// and the names of the dropdowns' choices in the interface's language. Invented data only; a fake
// render service, no Blender, no view.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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

public sealed class BlenderPanelEditingTests : System.IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (string dir in _dirs)
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (System.UnauthorizedAccessException) { }
    }

    private sealed class FilmService : IBlenderRenderService, IRenderEstimator
    {
        public Storyboard? LastStoryboard { get; private set; }

        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/a.mp4"), FrameCount = 3 });

        public Task<RenderResult> RenderStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastStoryboard = storyboard;
            return Task.FromResult(new RenderResult { Succeeded = true, OutputPaths = ImmutableArray.Create("out/film.mp4"), FrameCount = 3 });
        }

        public Task<RenderEstimate> EstimateStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, CancellationToken ct = default)
            => Task.FromResult(new RenderEstimate { Frames = 1, SecondsPerFrame = 1, Measured = false, DiskBytes = 1, MemoryBytes = 1, Triangles = 1, TextObjects = 0 });

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => Task.FromResult(Path.Combine(outputDir, "render.py"));
    }

    /// <summary>Finds the model at once, or (<paramref name="hang"/>) only when cancelled, as a
    /// long export by Therion would.</summary>
    private sealed class Sources(bool hang = false) : IBlenderSourceProvider
    {
        public bool HasProject => false;
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => [];
        public async Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
        {
            if (hang) await Task.Delay(Timeout.Infinite, ct);
            return new RenderSource(new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []);
        }
    }

    private string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "thide-panel-editing-" + System.Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private static BlenderAnimationViewModel Vm(FilmService? service = null, PresetStore? store = null, int tab = BlenderAnimationViewModel.StoryboardTab, bool hang = false)
        => new(service ?? new FilmService(), new Sources(hang), store)
        {
            UseExternalFile = true, ExternalFilePath = "m.lox", RenderDirectory = "", PanelTab = tab,
        };

    private static void Add(BlenderAnimationViewModel vm, string preset)
    {
        vm.SelectedPreset = vm.Presets.First(p => p.Name == preset);
        vm.AddShotCommand.Execute(null);
    }

    // ---- the Scene tab's scene and the Storyboard tab's film ----

    [Fact]
    public void The_scene_tab_keeps_its_settings_across_a_visit_to_the_storyboard()
    {
        var vm = Vm(tab: BlenderAnimationViewModel.SceneTab);
        vm.Title = "My scene";
        vm.CameraTemplate = CameraTemplate.Helix;
        vm.PanelTab = BlenderAnimationViewModel.StoryboardTab;
        Add(vm, "Full flythrough");   // selects the new shot
        Assert.Equal(CameraTemplate.Flythrough, vm.CameraTemplate);
        vm.Samples = 5;               // an edit of the shot
        vm.PanelTab = BlenderAnimationViewModel.SceneTab;
        Assert.Equal("My scene", vm.Title);
        Assert.Equal(CameraTemplate.Helix, vm.CameraTemplate);
        Assert.NotEqual(5, vm.CurrentSpec.Engine.Samples);
    }

    [Fact]
    public void A_scene_tab_edit_does_not_leak_into_the_selected_shot()
    {
        var vm = Vm();
        Add(vm, "Orbit showcase");
        var row = vm.SelectedShot!;
        vm.PanelTab = BlenderAnimationViewModel.SceneTab;
        vm.Title = "Scene only";
        // Choosing a preset on the Scene tab changes the scene, not the shot.
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Helix descent");
        vm.PanelTab = BlenderAnimationViewModel.StoryboardTab;
        Assert.Same(row, vm.SelectedShot);
        Assert.Equal(CameraTemplate.Orbit, vm.CameraTemplate);
        vm.Samples = 7;   // an edit of the shot
        Assert.NotEqual("Scene only", row.Shot.Texts?.Title);
        Assert.Equal(CameraTemplate.Orbit, row.Shot.Camera!.Template);
        Assert.Equal(7, row.Shot.Engine!.Samples);
    }

    [Fact]
    public void The_film_has_its_own_frame_rate_and_output()
    {
        var vm = Vm(tab: BlenderAnimationViewModel.SceneTab);
        vm.Width = 640;
        vm.PanelTab = BlenderAnimationViewModel.StoryboardTab;
        // The film did not take the scene's size: it has its own.
        Assert.Equal(1920, vm.Width);
        vm.Width = 1280;
        vm.Fps = 24;
        vm.BaseName = "my-film";
        Assert.Equal((24, 1280, "my-film"), (vm.CurrentStoryboard.Fps, vm.CurrentStoryboard.Output.Width, vm.CurrentStoryboard.Output.BaseName));

        vm.PanelTab = BlenderAnimationViewModel.SceneTab;
        Assert.Equal(640, vm.Width);
        Assert.Equal(30, vm.Fps);
        Assert.Equal(1280, vm.CurrentStoryboard.Output.Width);
        vm.PanelTab = BlenderAnimationViewModel.StoryboardTab;
        Assert.Equal((24, 1280, "my-film"), (vm.Fps, vm.Width, vm.BaseName));
    }

    [Fact]
    public void Without_a_selected_shot_the_shots_settings_cannot_be_edited_and_change_no_shot()
    {
        var vm = Vm(tab: BlenderAnimationViewModel.SceneTab);
        Assert.True(vm.CanEditShotSettings);
        Assert.False(vm.ChooseShotHint);
        vm.PanelTab = BlenderAnimationViewModel.StoryboardTab;
        Assert.False(vm.CanEditShotSettings);
        Assert.True(vm.ChooseShotHint);

        Add(vm, "Orbit showcase");
        Assert.True(vm.CanEditShotSettings);
        Assert.False(vm.ChooseShotHint);
        var before = vm.Shots[0].Shot;

        // A preset chosen in the film is what the next shot is added from: no shot is edited.
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Helix descent");
        Assert.Null(vm.SelectedShot);
        Assert.False(vm.CanEditShotSettings);
        Assert.Equal(CameraTemplate.Helix, vm.CameraTemplate);
        vm.Samples = 3;
        Assert.Equal(before, vm.Shots[0].Shot);
    }

    [Fact]
    public void Re_selecting_a_shot_shows_the_films_terrain_and_fades()
    {
        var vm = Vm();
        Add(vm, "Orbit showcase");
        vm.SelectedShot = null;
        Add(vm, "Helix descent");
        vm.SurfaceMode = SurfaceMode.Textured;
        vm.FadeToBlack = 2;
        vm.SelectedShot = vm.Shots[0];
        Assert.Equal((SurfaceMode.Textured, 2.0), (vm.SurfaceMode, vm.FadeToBlack));
        // They are the film's: no shot holds them.
        Assert.All(vm.Shots, s => Assert.Equal(0, s.Shot.Texts?.FadeToBlack ?? 0));
        Assert.Equal(SurfaceMode.Textured, vm.CurrentStoryboard.Defaults.Surface!.Mode);
        Assert.Equal(2, vm.CurrentStoryboard.Texts!.FadeToBlack);
    }

    [Fact]
    public async Task Terrain_set_while_a_shot_is_selected_reaches_the_film()
    {
        var service = new FilmService();
        var vm = Vm(service);
        Add(vm, "Orbit showcase");
        Assert.NotNull(vm.SelectedShot);
        vm.SurfaceMode = SurfaceMode.Solid;
        vm.SurfaceOpacity = 0.7;
        await vm.RenderFilmCommand.ExecuteAsync(null);
        var film = service.LastStoryboard!;
        Assert.Equal((SurfaceMode.Solid, 0.7), (film.Defaults.Surface!.Mode, film.Defaults.Surface.Opacity));
    }

    [Fact]
    public async Task Fade_set_while_a_shot_is_selected_reaches_the_film()
    {
        var service = new FilmService();
        var vm = Vm(service);
        Add(vm, "Orbit showcase");
        vm.FadeFromBlack = 1.5;
        await vm.RenderFilmCommand.ExecuteAsync(null);
        Assert.Equal(1.5, service.LastStoryboard!.Texts!.FadeFromBlack);
        Assert.Equal(1.5, vm.FadeFromBlack);
    }

    [Fact]
    public void Selecting_a_shot_that_cannot_be_planned_shows_its_own_settings()
    {
        var vm = Vm();
        Add(vm, "Orbit showcase");
        var orbit = vm.SelectedShot!;
        vm.DurationSeconds = 0;               // shot 1 now invalid (no length)
        Assert.NotEmpty(vm.StoryboardBlockers);
        vm.SelectedShot = null;
        Add(vm, "Helix descent");             // shot 2, selected
        Assert.Equal(CameraTemplate.Helix, vm.CameraTemplate);
        vm.SelectedShot = orbit;              // back to shot 1: its own camera and length
        Assert.Equal((CameraTemplate.Orbit, 0.0), (vm.CameraTemplate, vm.DurationSeconds));
        vm.DurationSeconds = 5;               // the user fixes the length
        Assert.Equal(CameraTemplate.Orbit, orbit.Shot.Camera?.Template ?? CameraTemplate.Orbit);
        Assert.Equal(5, orbit.Shot.Seconds);
        Assert.Empty(vm.StoryboardBlockers);
    }

    [Fact]
    public void A_composite_brings_its_output_and_frame_rate_into_an_empty_film()
    {
        var store = new PresetStore(TempDir());
        var grand = BuiltInComposites.All.Single(c => c.Name == "Grand tour");
        store.SaveComposite(new CompositePreset
        {
            Name = "Mine",
            Storyboard = grand.Storyboard with { Fps = 24, Output = grand.Storyboard.Output with { Width = 1280, Height = 720, BaseName = "my-film" } },
        });
        var vm = Vm(store: store);
        vm.SelectedComposite = vm.Composites.Single(c => c.Name == "Mine");
        vm.AddCompositeCommand.Execute(null);
        var film = vm.CurrentStoryboard;
        Assert.Equal((24, 1280, 720, "my-film"), (film.Fps, film.Output.Width, film.Output.Height, film.Output.BaseName));
        // The settings show them.
        Assert.Equal((24, 1280, 720, "my-film"), (vm.Fps, vm.Width, vm.Height, vm.BaseName));
    }

    // ---- presets ----

    [Fact]
    public void Delete_preset_enablement_follows_the_new_selection()
    {
        var store = new PresetStore(TempDir());
        store.Save(new RenderPreset { Name = "Mine", Spec = BuiltInPresets.OrbitShowcase().Spec });
        var vm = Vm(store: store, tab: BlenderAnimationViewModel.SceneTab);
        var seen = new List<bool>();
        vm.DeletePresetCommand.CanExecuteChanged += (_, _) => seen.Add(vm.DeletePresetCommand.CanExecute(null));

        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Mine");
        // A button re-reads CanExecute when the event is raised: the last value it read is what it shows.
        Assert.NotEmpty(seen);
        Assert.True(seen[^1]);
        vm.SelectedPreset = vm.Presets.First(p => p.BuiltIn);
        Assert.False(seen[^1]);
    }

    [Fact]
    public void A_preset_cannot_be_saved_under_a_built_in_ones_name()
    {
        var vm = Vm(store: new PresetStore(TempDir()), tab: BlenderAnimationViewModel.SceneTab);
        vm.NewPresetName = "orbit SHOWCASE";
        Assert.Equal(Tr.Get("Blender_Preset_NameBuiltIn"), vm.PresetNameProblem);
        Assert.False(vm.SavePresetCommand.CanExecute(null));
        vm.NewPresetName = "Orbit of mine";
        Assert.Null(vm.PresetNameProblem);
        Assert.True(vm.SavePresetCommand.CanExecute(null));
    }

    [Fact]
    public void A_shot_added_from_a_user_preset_with_a_built_in_ones_name_uses_that_preset()
    {
        // A file saved before such names were refused.
        var store = new PresetStore(TempDir());
        store.Save(new RenderPreset { Name = "Orbit showcase", Spec = BuiltInPresets.HelixDescent().Spec });
        var vm = Vm(store: store);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Orbit showcase" && !p.BuiltIn);
        vm.AddShotCommand.Execute(null);
        Assert.Equal(CameraTemplate.Helix, vm.CameraTemplate);
        Assert.Null(vm.Shots[0].Shot.Preset);
        Assert.Equal(CameraTemplate.Helix, vm.Shots[0].Shot.Camera!.Template);
    }

    [Fact]
    public void Save_preset_into_an_unwritable_folder_says_why_instead_of_throwing()
    {
        string file = Path.Combine(TempDir(), "not-a-folder");
        File.WriteAllText(file, "x");
        var vm = Vm(store: new PresetStore(file), tab: BlenderAnimationViewModel.SceneTab);
        vm.NewPresetName = "Mine";
        var ex = Record.Exception(() => vm.SavePresetCommand.Execute(null));
        Assert.Null(ex);
        Assert.Contains("Mine", vm.LastError);
        Assert.Equal("Mine", vm.NewPresetName);   // kept, to try again
    }

    [Fact]
    public void Delete_preset_that_cannot_be_deleted_says_why_instead_of_throwing()
    {
        if (System.OperatingSystem.IsWindows()) return;   // a read-only folder is a POSIX permission
        string dir = TempDir();
        var store = new PresetStore(dir);
        store.Save(new RenderPreset { Name = "Mine", Spec = BuiltInPresets.OrbitShowcase().Spec });
        var vm = Vm(store: store, tab: BlenderAnimationViewModel.SceneTab);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "Mine");
        var mode = File.GetUnixFileMode(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // Root may delete anyway; then there is nothing to report.
            var ex = Record.Exception(() => vm.DeletePresetCommand.Execute(null));
            Assert.Null(ex);
            if (Directory.EnumerateFiles(dir).Any()) Assert.Contains("Mine", vm.LastError);
        }
        finally { File.SetUnixFileMode(dir, mode); }
    }

    [Fact]
    public void Two_presets_whose_names_differ_by_letters_outside_ASCII_or_case_are_both_kept()
    {
        var store = new PresetStore(TempDir());
        foreach (string name in new[] { "Șura Mare", "Țura mare", "My preset", "my-preset", "My Preset" })
            store.Save(new RenderPreset { Name = name, Spec = BuiltInPresets.OrbitShowcase().Spec });
        Assert.Equal(5, store.Load().Count);
        // Saved again, each replaces its own file; deleted, only itself goes.
        store.Save(new RenderPreset { Name = "my-preset", Spec = BuiltInPresets.HelixDescent().Spec });
        Assert.Equal(5, store.Load().Count);
        Assert.True(store.Delete("My preset"));
        Assert.Equal(["My Preset", "my-preset", "Șura Mare", "Țura mare"], store.Load().Select(p => p.Name));
        Assert.Equal(CameraTemplate.Helix, store.Load().Single(p => p.Name == "my-preset").Spec.Camera.Template);
        Assert.True(File.Exists(Path.Combine(store.Directory, "sura-mare" + PresetStore.Extension)));
    }

    // ---- long work, folders, the history ----

    [Fact]
    public async Task An_estimate_that_waits_for_the_model_is_cancelled()
    {
        var vm = Vm(hang: true);
        Add(vm, "Orbit showcase");
        var estimate = vm.EstimateFilmCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        vm.CancelCommand.Execute(null);
        await estimate.WaitAsync(System.TimeSpan.FromSeconds(10));
        Assert.False(vm.IsBusy);
        Assert.Equal(Tr.Get("Blender_Status_Cancelled"), vm.StatusMessage);
        Assert.False(vm.IsJobRunning);
    }

    [Fact]
    public void A_relative_render_folder_is_a_blocker()
    {
        var vm = Vm(tab: BlenderAnimationViewModel.SceneTab);
        vm.RenderDirectory = "renders";
        Assert.Contains(string.Format(Tr.Get("Blender_Blocker_RelativeFolder"), "renders"), vm.Blockers);
        Assert.False(vm.RenderCommand.CanExecute(null));
        vm.PanelTab = BlenderAnimationViewModel.StoryboardTab;
        Add(vm, "Orbit showcase");
        Assert.Contains(string.Format(Tr.Get("Blender_Blocker_RelativeFolder"), "renders"), vm.StoryboardBlockers);
        vm.RenderDirectory = Path.Combine(Path.GetTempPath(), "renders");
        Assert.Empty(vm.StoryboardBlockers);
    }

    [Fact]
    public async Task Each_finished_job_has_a_copy_button_of_its_own()
    {
        var vm = Vm(tab: BlenderAnimationViewModel.SceneTab);
        await vm.RenderCommand.ExecuteAsync(null);
        await vm.RenderCommand.ExecuteAsync(null);
        Assert.Equal(["Blender.Job.2.Copy", "Blender.Job.1.Copy"], vm.Jobs.Select(j => j.CopyId));
    }
}

/// <summary>The choices of the panel's dropdowns, in the interface's language.</summary>
[Collection(UiLanguageCollection.Name)]
public sealed class BlenderPanelOptionNameTests
{
    private static readonly System.Type[] Enums =
    [
        typeof(CameraTemplate), typeof(SurfaceMode), typeof(LightingRig), typeof(RenderEngineKind), typeof(GpuMode), typeof(SceneBuilder),
        typeof(RevealChoice), typeof(OutputKind), typeof(VideoContainer), typeof(LeadMarkerSizing), typeof(LabelRoofSource), typeof(TransitionKind),
    ];

    private static readonly string[] Prefixes =
    [
        "Opt_Blender_Camera_", "Opt_Blender_Surface_", "Opt_Blender_Light_", "Opt_Blender_Engine_", "Opt_Blender_Gpu_", "Opt_Blender_Builder_",
        "Opt_Blender_Reveal_", "Opt_Blender_Output_", "Opt_Blender_Container_", "Opt_Blender_LeadSize_", "Opt_Blender_LabelRoof_", "Opt_Blender_Transition_",
    ];

    [Fact]
    public void Every_choice_of_every_dropdown_has_a_name_in_both_languages()
    {
        var missing = new List<string>();
        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("ro-RO") })
            UiLanguageCollection.In(culture, () =>
            {
                for (int i = 0; i < Enums.Length; i++)
                    foreach (string value in System.Enum.GetNames(Enums[i]))
                        if (Tr.Find(Prefixes[i] + value) is not { Length: > 0 }) missing.Add($"{culture.Name}: {Prefixes[i]}{value}");
            });
        Assert.Empty(missing);
    }

    [Fact]
    public void A_shots_camera_is_named_in_the_interfaces_language()
    {
        UiLanguageCollection.In(new CultureInfo("ro-RO"), () =>
        {
            var row = new StoryboardShotViewModel(new ShotSpec { Camera = new CameraSpec { Template = CameraTemplate.Orbit } }, _ => null, () => { });
            Assert.Equal("Orbită", row.Kind);
            Assert.Equal("Fără", ThIDE.Converters.LocKeyConverter.Label("Opt_Blender_Surface_", nameof(SurfaceMode.Off)));
        });
    }

    [Fact]
    public void The_film_languages_and_the_branch_choices_follow_a_language_switch()
    {
        var vm = new BlenderAnimationViewModel();
        string english = "", romanian = "";
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () => english = vm.FilmLanguages[0].ToString() + BranchVisitItem.All[2]);
        UiLanguageCollection.In(new CultureInfo("ro-RO"), () => romanian = vm.FilmLanguages[0].ToString() + BranchVisitItem.All[2]);
        Assert.NotEqual(english, romanian);
    }
}
