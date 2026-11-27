// The Storyboard tab of the Blender panel: a film as a list of shots. A shot is added from a
// preset (a camera move with its look) or a composite preset adds its shots (Showcase, Grand
// tour, Exploration history); the shots are reordered and removed here. On this tab the panel's
// settings edit the film and the shot that is selected, never the Scene tab's scene, which is
// kept aside while this tab is shown: the frame rate, the output, the terrain and the fades are
// the film's; the rest (camera, light, engine, reveal, labels, texts) is the selected shot's,
// loaded into them when it is selected and written back into it on every change, and cannot be
// edited while no shot is selected. Draft all, Draft shot
// and Render go through the render queue as the panel's (owner "panel"); the estimate is shown
// before a render starts. The queue itself is listed, with a cancel for each job. The film's
// title, end card and language, and saving it as a composite, are in the .Film part; the branch
// table of a tour shot is in the .Branches part.
//
// The VM holds no film logic: Storyboard, StoryboardValidator and StoryboardExpander compute.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class BlenderAnimationViewModel
{
    /// <summary>The panel's tabs: one scene, or a film of several shots.</summary>
    public const int SceneTab = 0, StoryboardTab = 1;

    /// <summary>The tab shown: <see cref="SceneTab"/> or <see cref="StoryboardTab"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStoryboardTab), nameof(IsSceneTab), nameof(CanEditShotSettings), nameof(ChooseShotHint))]
    private int _panelTab;

    public bool IsStoryboardTab => PanelTab == StoryboardTab;
    public bool IsSceneTab => !IsStoryboardTab;

    /// <summary>True when the settings a shot holds (camera, light, engine, reveal, labels, texts)
    /// can be edited: on the Scene tab, or for the selected shot.</summary>
    public bool CanEditShotSettings => IsSceneTab || HasSelectedShot;

    /// <summary>True on the Storyboard tab with no shot selected: the panel says to select one.</summary>
    public bool ChooseShotHint => IsStoryboardTab && !HasSelectedShot;

    /// <summary>The film's shots, in order.</summary>
    public ObservableCollection<StoryboardShotViewModel> Shots { get; } = [];

    /// <summary>The shot the settings edit; null edits the scene alone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedShot), nameof(CanEditShotSettings), nameof(ChooseShotHint))]
    private StoryboardShotViewModel? _selectedShot;

    public bool HasSelectedShot => SelectedShot is not null;

    /// <summary>The composite presets: the built-in ones, then the user's.</summary>
    public ObservableCollection<CompositeItem> Composites { get; } = [];

    [ObservableProperty] private CompositeItem? _selectedComposite;

    /// <summary>What is wrong with the film as it stands; Draft and Render wait for none.</summary>
    public ObservableCollection<string> StoryboardBlockers { get; } = [];

    /// <summary>The film's estimate in plain words, once one was made.</summary>
    [ObservableProperty] private string? _filmEstimate;

    /// <summary>The jobs in the render queue: the running one, those waiting, the last ended.</summary>
    public ObservableCollection<QueueRowViewModel> QueueRows { get; } = [];

    /// <summary>How the selected shot begins.</summary>
    public IReadOnlyList<TransitionKind> TransitionKinds { get; } = Enum.GetValues<TransitionKind>();

    // What the film has that no shot does, and that the settings edit on this tab: its frame rate
    // and output, and (in its defaults and texts below) the terrain and the fades.
    private int _filmFps;
    private OutputSpec _filmOutput = new();

    // The Scene tab's scene, kept while this tab is shown: the settings then show the film.
    private SceneSpec? _sceneKept;
    private string _sceneKeptAnnotationsPath = "";

    /// <summary>How long the film is, as its shots say (a tour's is at most its shot's).</summary>
    public double FilmSeconds => Math.Max(Shots.Sum(s => s.Seconds) - Shots.Skip(1).Sum(s => s.Shot.In is { Kind: TransitionKind.Dissolve } d ? d.Seconds : 0), 1e-3);

    // What the film has that no shot does: its name, its texts and the defaults, from a composite.
    private string? _filmName;
    private OverlaySpec? _filmTexts;
    private StoryboardDefaults _filmDefaults = new();

    /// <summary>The film the tab describes: its shots, with its own frame rate and output.</summary>
    public Storyboard CurrentStoryboard => new()
    {
        Name = _filmName,
        Fps = _filmFps,
        Language = FilmLanguage,
        Defaults = _filmDefaults,
        Texts = _filmTexts,
        Shots = Shots.Select(s => s.Shot).ToList(),
        Output = _filmOutput with
        {
            // A film is a video or frames, written where the renders go.
            Kind = _filmOutput.Kind == OutputKind.Still ? OutputKind.Video : _filmOutput.Kind,
            OutputDirectory = RenderDirectory?.Trim() ?? "",
        },
    };

    private RenderPreset? FindPreset(string name) => Presets.FirstOrDefault(p => p.Name == name)?.Preset;

    private void LoadComposites()
    {
        Composites.Clear();
        foreach (var composite in BuiltInComposites.All) Composites.Add(new CompositeItem(composite));
        if (_presetStore is not null)
            foreach (var composite in _presetStore.LoadComposites()) Composites.Add(new CompositeItem(composite));
        SelectedComposite ??= Composites.FirstOrDefault();
    }

    // ---- the shots ----

    /// <summary>Adds a shot of the selected preset after the selected shot (or at the end); it
    /// begins with a dissolve unless it is the first.</summary>
    [RelayCommand(CanExecute = nameof(CanAddShot))]
    private void AddShot()
    {
        if (SelectedPreset is not { } preset) return;
        var spec = preset.Preset.Spec;
        var shot = new ShotSpec
        {
            Name = preset.Name,
            Preset = preset.Name,
            Seconds = spec.Animation.DurationSeconds,
            In = Shots.Count == 0 ? null : new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 1 },
        };
        // A preset of the user's that has a built-in one's name (saved before such names were
        // refused) would be found as the built-in by its name: it is written into the shot.
        if (!preset.BuiltIn && BuiltInPresets.ByName(preset.Name) is not null)
            shot = StoryboardExpander.InlinePreset(shot, preset.Preset, _filmDefaults);
        int at = SelectedShot is { } selected ? Shots.IndexOf(selected) + 1 : Shots.Count;
        Shots.Insert(at, NewShotRow(shot));
        ShotsChanged();
        SelectedShot = Shots[at];
    }

    private bool CanAddShot => SelectedPreset is not null && SelectedPreset.Preset.Spec.Camera.Template != CameraTemplate.StillSet;

    /// <summary>Adds the shots of the selected composite at the end. Into an empty film it also
    /// brings the composite's name, texts, defaults, language, frame rate and output, so that it
    /// renders here as it does from the command line and the agent tools.</summary>
    [RelayCommand]
    private void AddComposite()
    {
        if (SelectedComposite is not { } item) return;
        var film = item.Composite.Storyboard;
        bool empty = Shots.Count == 0;
        if (empty)
        {
            _filmName = film.Name;
            _filmTexts = film.Texts;
            _filmDefaults = film.Defaults;
            _filmFps = film.Fps;
            // Where it is written is the panel's (the render folder).
            _filmOutput = film.Output with { OutputDirectory = "" };
            SetFilmLanguage(film.Language);
            FilmTextsChanged();
        }
        for (int i = 0; i < film.Shots.Count; i++)
        {
            var shot = film.Shots[i];
            // Joined to the shots before by a dip.
            if (i == 0 && Shots.Count > 0) shot = shot with { In = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.6 } };
            Shots.Add(NewShotRow(shot));
        }
        ShotsChanged();
        // The settings show the film's frame rate, output, terrain and fades it brought.
        if (empty) ShowFilmSettings();
    }

    [RelayCommand]
    private void MoveShotUp(StoryboardShotViewModel? row) => Move(row, -1);

    [RelayCommand]
    private void MoveShotDown(StoryboardShotViewModel? row) => Move(row, +1);

    private void Move(StoryboardShotViewModel? row, int by)
    {
        if (row is null) return;
        int from = Shots.IndexOf(row), to = from + by;
        if (from < 0 || to < 0 || to >= Shots.Count) return;
        Shots.Move(from, to);
        // The film begins with its first shot: that one has nothing to dip or dissolve from.
        if (Shots[0].Shot.In is not null) Shots[0].Shot = Shots[0].Shot with { In = null };
        ShotsChanged();
    }

    [RelayCommand]
    private void RemoveShot(StoryboardShotViewModel? row)
    {
        if (row is null || !Shots.Remove(row)) return;
        if (SelectedShot == row) SelectedShot = null;
        if (Shots.Count > 0 && Shots[0].Shot.In is not null) Shots[0].Shot = Shots[0].Shot with { In = null };
        if (Shots.Count == 0)
        {
            (_filmName, _filmTexts, _filmDefaults) = (null, null, new StoryboardDefaults());
            SetFilmLanguage(null);
            FilmTextsChanged();
            ShowFilmSettings();
        }
        ShotsChanged();
    }

    private StoryboardShotViewModel NewShotRow(ShotSpec shot) => new(shot, FindPreset, ShotsChanged);

    /// <summary>Selects a shot: the settings edit it from now on.</summary>
    [RelayCommand]
    private void SelectShot(StoryboardShotViewModel? row) => SelectedShot = row;

    partial void OnSelectedShotChanged(StoryboardShotViewModel? oldValue, StoryboardShotViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        DraftShotCommand.NotifyCanExecuteChanged();
        ClearBranchTable();
    }

    partial void OnSelectedShotChanged(StoryboardShotViewModel? value) => ShowFilmSettings();

    partial void OnPanelTabChanged(int oldValue, int newValue)
    {
        if (newValue == StoryboardTab && oldValue != StoryboardTab)
        {
            // The scene is kept as it is; the settings show the film and its selected shot.
            _sceneKept = _spec;
            _sceneKeptAnnotationsPath = AnnotationsPath;
            ShowFilmSettings();
        }
        else if (newValue != StoryboardTab && oldValue == StoryboardTab && _sceneKept is { } scene)
        {
            _sceneKept = null;
            ShowSpec(scene, _sceneKeptAnnotationsPath);
        }
        RevalidateStoryboard();
    }

    /// <summary>
    /// On this tab, the settings show the film: its frame rate, output, terrain and fades, with
    /// what the selected shot shows over its preset and the film's defaults (even a shot that
    /// cannot be planned as it stands: its own settings, never another's). With no shot selected
    /// they show the preset a shot would be added from, and edit nothing of it.
    /// </summary>
    private void ShowFilmSettings()
    {
        if (!IsStoryboardTab) return;
        var scene = _sceneKept ?? _spec;
        int at = SelectedShot is { } row ? Shots.IndexOf(row) : -1;
        var shot = at >= 0
            ? StoryboardPlanner.ShotSettings(CurrentStoryboard, at, FindPreset)
            : SelectedPreset?.Preset.Spec ?? scene;
        // The shot's texts; the end card is the film's, and is edited with the film's title.
        var texts = (at >= 0 ? shot.Labels.Overlays : new OverlaySpec()) with
        {
            EndCard = null,
            FadeFromBlack = _filmTexts?.FadeFromBlack ?? 0,
            FadeToBlack = _filmTexts?.FadeToBlack ?? 0,
        };
        ShowSpec(shot with
        {
            Source = scene.Source,
            Animation = shot.Animation with { Fps = _filmFps },
            Output = _filmOutput,
            Surface = _filmDefaults.Surface ?? new SurfaceSpec(),
            Labels = shot.Labels with { Overlays = texts },
        }, "");
    }

    /// <summary>The knobs take <paramref name="spec"/>'s values, one after another; the spec is
    /// not rebuilt from the half of them that have theirs.</summary>
    private void ShowSpec(SceneSpec spec, string annotationsPath)
    {
        _suspend = true;
        try
        {
            _spec = spec;
            AnnotationsPath = annotationsPath;
            PushSpecToKnobs(_spec);
        }
        finally { _suspend = false; }
        Revalidate();
    }

    /// <summary>After a change of the settings on this tab: the film takes its frame rate,
    /// output, terrain and fades, and the selected shot the rest.</summary>
    private void WriteSettingsToFilm()
    {
        if (!IsStoryboardTab) return;
        var spec = _spec;
        _filmFps = spec.Animation.Fps;
        _filmOutput = spec.Output;
        // No terrain is said as none, as a storyboard without one says it.
        var surface = _filmDefaults.Surface is null && spec.Surface == new SurfaceSpec() ? null : spec.Surface;
        if (surface != _filmDefaults.Surface) _filmDefaults = _filmDefaults with { Surface = surface };
        var overlays = spec.Labels.Overlays;
        if (overlays.FadeFromBlack != (_filmTexts?.FadeFromBlack ?? 0) || overlays.FadeToBlack != (_filmTexts?.FadeToBlack ?? 0))
            SetFilmTexts(t => t with { FadeFromBlack = overlays.FadeFromBlack, FadeToBlack = overlays.FadeToBlack });
        if (SelectedShot is { } row) WriteSettingsToShot(row);
        else ShotsChanged();
    }

    /// <summary>The selected shot takes what the settings say of it.</summary>
    private void WriteSettingsToShot(StoryboardShotViewModel row)
    {
        var old = row.Shot;
        var spec = _spec;
        row.Shot = old with
        {
            Preset = null,
            Seconds = spec.Animation.DurationSeconds,
            Camera = spec.Camera,
            Engine = spec.Engine,
            Materials = spec.Materials,
            Lighting = spec.Lighting,
            Labels = spec.Labels with { Overlays = new OverlaySpec() },
            // The end card and the fades are the film's.
            Texts = spec.Labels.Overlays with { EndCard = null, FadeFromBlack = 0, FadeToBlack = 0 },
            Annotations = spec.Annotations,
            Reveal = spec.Reveal is { } reveal ? ShotReveal.Of(reveal) : old.Reveal is { Mode: ShotRevealMode.Hold } ? ShotReveal.Hold : ShotReveal.None,
        };
        ShotsChanged();
    }

    /// <summary>The shots changed: they take their numbers and places on the strip, and the film
    /// is checked again.</summary>
    private void ShotsChanged()
    {
        double film = FilmSeconds, at = 0;
        for (int i = 0; i < Shots.Count; i++)
        {
            var row = Shots[i];
            if (i > 0 && row.Shot.In is { Kind: TransitionKind.Dissolve } dissolve) at -= dissolve.Seconds;
            at = Math.Max(at, 0);
            row.Place(i + 1, at / film, Math.Min(1, (at + row.Seconds) / film));
            at += row.Seconds;
        }
        OnPropertyChanged(nameof(FilmSeconds));
        RevalidateStoryboard();
        BranchTableStale();
    }

    private void RevalidateStoryboard()
    {
        StoryboardBlockers.Clear();
        if (Shots.Count > 0)
        {
            var film = CurrentStoryboard;
            foreach (var error in StoryboardValidator.Validate(film with { Output = film.Output with { OutputDirectory = "out" } }, FindPreset))
                StoryboardBlockers.Add($"{error.Path}: {error.Message}");
            if (RelativeFolderProblem(RenderDirectory) is { } relative) StoryboardBlockers.Add(relative);
        }
        FilmEstimate = null;
        DraftAllCommand.NotifyCanExecuteChanged();
        DraftShotCommand.NotifyCanExecuteChanged();
        RenderFilmCommand.NotifyCanExecuteChanged();
        EstimateFilmCommand.NotifyCanExecuteChanged();
        SaveCompositeCommand.NotifyCanExecuteChanged();
        ListBranchesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPresetChanged(PresetItem? oldValue, PresetItem? newValue)
    {
        AddShotCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    // ---- draft, estimate, render ----

    private bool CanStartFilm => !IsBusy && HasSource && Shots.Count > 0 && StoryboardBlockers.Count == 0
                                 && _renderService is not null && _sources is not null;

    [RelayCommand(CanExecute = nameof(CanStartFilm))]
    private Task DraftAllAsync() => RunFilmAsync(draft: true, shot: null);

    [RelayCommand(CanExecute = nameof(CanDraftShot))]
    private Task DraftShotAsync() => RunFilmAsync(draft: true, shot: SelectedShot);

    private bool CanDraftShot => CanStartFilm && SelectedShot is not null;

    [RelayCommand(CanExecute = nameof(CanStartFilm))]
    private Task RenderFilmAsync() => RunFilmAsync(draft: false, shot: null);

    [RelayCommand(CanExecute = nameof(CanEstimate))]
    private async Task EstimateFilmAsync()
    {
        LastError = null;
        IsBusy = true;
        using var work = BeginWork();
        try
        {
            // Finding the model may export it with Therion first: Cancel stops that.
            var source = await _sources!.AcquireAsync(BuildRequest(), work.Token).ConfigureAwait(true);
            await EstimateAsync(Film(null), source, draft: false, work.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr.Get("Blender_Status_Cancelled");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException
                                       or ModelSourceNotFoundException)
        {
            LastError = ex.Message.Split(" (Parameter", 2)[0];
        }
        finally
        {
            EndWork(work);
            IsBusy = false;
            RevalidateCommands();
        }
    }

    private bool CanEstimate => CanStartFilm && _renderService is IRenderEstimator;

    /// <summary>The film to render: every shot, or one alone; the user's presets written into
    /// the shots, so that a restart that starts it again renders the same.</summary>
    private Storyboard Film(StoryboardShotViewModel? only)
    {
        // The texts a tour writes are in the film's language, or the interface's.
        var film = CurrentStoryboard with { Language = FilmLanguage ?? InterfaceFilmLanguage };
        if (only is not null)
        {
            var shot = only.Shot;
            film = film with
            {
                Shots = [shot with { In = null, Reveal = shot.Reveal is { Mode: ShotRevealMode.Hold } ? ShotReveal.None : shot.Reveal }],
                Output = film.Output with { BaseName = film.Output.BaseName + "." + StoryboardPlanner.SegmentId(Shots.IndexOf(only)) },
            };
        }
        return StoryboardExpander.InlinePresets(film, FindPreset);
    }

    private async Task EstimateAsync(Storyboard film, RenderSource source, bool draft, CancellationToken ct = default)
    {
        if (_renderService is not IRenderEstimator estimator) return;
        var estimate = await estimator.EstimateStoryboardAsync(film, source, new RenderOptions { Draft = draft, Owner = PanelHolder }, ct).ConfigureAwait(true);
        FilmEstimate = string.Format(CultureInfo.CurrentCulture, Tr.Get("Blender_Storyboard_Estimate"),
            estimate.Frames, TimeSpan.FromSeconds(Math.Round(estimate.Seconds)).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture),
            estimate.DiskBytes / 1e9, estimate.MemoryBytes / 1e9);
    }

    private async Task RunFilmAsync(bool draft, StoryboardShotViewModel? shot)
    {
        Outputs.Clear();
        Warnings.Clear();
        LastError = null;
        IsBusy = true;
        RevalidateCommands();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<RenderProgress>(OnProgress);
            StatusMessage = Tr.Get("Blender_Status_Acquiring");
            var source = await _sources!.AcquireAsync(BuildRequest(), _cts.Token).ConfigureAwait(true);
            var film = Film(shot);
            // What it will cost is shown before the long render starts.
            if (!draft)
            {
                try { await EstimateAsync(film, source, draft).ConfigureAwait(true); }
                catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException) { }
            }
            var options = new RenderOptions { Draft = draft, Owner = PanelHolder, JobName = film.Output.BaseName };
            var result = await _renderService!.RenderStoryboardAsync(film, source, options, progress, _cts.Token).ConfigureAwait(true);
            ApplyResult(result, draft);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr.Get("Blender_Status_Cancelled");
        }
        catch (Exception ex)
        {
            LastError = ex.Message.Split(" (Parameter", 2)[0];
            StatusMessage = Tr.Get("Blender_Status_Failed");
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            _cts?.Dispose();
            _cts = null;
            Revalidate();
            RevalidateCommands();
        }
    }

    private void RevalidateCommands()
    {
        DraftAllCommand.NotifyCanExecuteChanged();
        DraftShotCommand.NotifyCanExecuteChanged();
        RenderFilmCommand.NotifyCanExecuteChanged();
        EstimateFilmCommand.NotifyCanExecuteChanged();
        ListBranchesCommand.NotifyCanExecuteChanged();
    }

    // ---- the queue ----

    /// <summary>Lists the queue's jobs again: the queue changed.</summary>
    private void RefreshQueueRows()
    {
        QueueRows.Clear();
        if (_queue is null) return;
        int n = 0;
        foreach (var job in _queue.Jobs)
        {
            n++;
            QueueRows.Add(new QueueRowViewModel(job.Id, n, job.Request.Name, OwnerText(job.Request.Owner), StateText(job.State),
                job.State is RenderJobState.Queued or RenderJobState.Running));
        }
    }

    private static string OwnerText(string? owner) => owner switch
    {
        PanelHolder => Tr.Get("Blender_Queue_OwnerPanel"),
        RenderQueue.RestartOwner => Tr.Get("Blender_Queue_OwnerRestart"),
        _ => Tr.Get("Blender_Queue_OwnerAgent"),
    };

    private static string StateText(RenderJobState state) => state switch
    {
        RenderJobState.Queued => Tr.Get("Blender_Queue_Queued"),
        RenderJobState.Running => Tr.Get("Blender_Queue_Running"),
        RenderJobState.Succeeded => Tr.Get("Blender_Status_Done"),
        RenderJobState.Cancelled => Tr.Get("Blender_Status_Cancelled"),
        _ => Tr.Get("Blender_Status_Failed"),
    };

    [RelayCommand]
    private void CancelQueued(QueueRowViewModel? row)
    {
        if (row is not null) _queue?.Cancel(row.Id);
    }

    /// <summary>A render a restart started again (ThIDE was closed in the middle of it) is running.</summary>
    public bool ResumedRendering => _queue?.Running is { Request.Owner: RenderQueue.RestartOwner };
}

/// <summary>One shot of the film as a row of the Storyboard tab.</summary>
public sealed partial class StoryboardShotViewModel : ObservableObject
{
    private readonly Func<string, RenderPreset?> _presets;
    private readonly Action _changed;

    public StoryboardShotViewModel(ShotSpec shot, Func<string, RenderPreset?> presets, Action changed)
    {
        _shot = shot;
        _presets = presets;
        _changed = changed;
    }

    /// <summary>The shot as the storyboard has it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Kind), nameof(IsTour), nameof(Seconds), nameof(Transition), nameof(TransitionKind), nameof(TransitionSeconds))]
    private ShotSpec _shot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectId), nameof(NameId), nameof(UpId), nameof(DownId), nameof(RemoveId))]
    private int _number;

    [ObservableProperty] private double _stripStart;
    [ObservableProperty] private double _stripEnd;

    /// <summary>True for the shot the settings edit.</summary>
    [ObservableProperty] private bool _isSelected;

    public string SelectId => $"Blender.Shot.{Number}.Select";
    public string NameId => $"Blender.Shot.{Number}.Name";
    public string UpId => $"Blender.Shot.{Number}.Up";
    public string DownId => $"Blender.Shot.{Number}.Down";
    public string RemoveId => $"Blender.Shot.{Number}.Remove";

    /// <summary>The shot's name; editing it renames the shot.</summary>
    public string Name
    {
        get => Shot.Name ?? "";
        set
        {
            if (value == (Shot.Name ?? "")) return;
            Shot = Shot with { Name = string.IsNullOrWhiteSpace(value) ? null : value };
            _changed();
        }
    }

    /// <summary>The shot's camera, its own or its preset's.</summary>
    internal CameraSpec? Camera => Shot.Camera ?? (Shot.Preset is { } name ? _presets(name)?.Spec.Camera : null);

    /// <summary>True for a tour of the cave's branches: the tab lists its branches.</summary>
    public bool IsTour => Camera is { Template: CameraTemplate.Route, Route.Branches: not null };

    /// <summary>What the camera does: its template, or a tour of the branches.</summary>
    public string Kind => IsTour
        ? Tr.Get("Blender_Shot_Tour")
        : ThIDE.Converters.LocKeyConverter.Label(BlenderAnimationViewModel.CameraOptionPrefix, (Camera?.Template ?? CameraTemplate.Static).ToString());

    /// <summary>How long the shot is, as it says or as its preset does.</summary>
    public double Seconds => Shot.Seconds
        ?? (Shot.Preset is { } name ? _presets(name)?.Spec.Animation.DurationSeconds : null)
        ?? StoryboardPlanner.DefaultShotSeconds;

    public string Transition => Shot.In is not { Kind: not Therion.Blender.TransitionKind.Cut } t
        ? Tr.Get("Blender_Shot_Cut")
        : string.Format(CultureInfo.CurrentCulture, Tr.Get(t.Kind == Therion.Blender.TransitionKind.Dip ? "Blender_Shot_Dip" : "Blender_Shot_Dissolve"), t.Seconds);

    /// <summary>How the shot begins: editable for any shot but the first.</summary>
    public TransitionKind TransitionKind
    {
        get => Shot.In?.Kind ?? Therion.Blender.TransitionKind.Cut;
        set
        {
            if (value == TransitionKind || Number == 1) return;
            Shot = Shot with { In = value == Therion.Blender.TransitionKind.Cut ? null : (Shot.In ?? new TransitionSpec()) with { Kind = value } };
            _changed();
        }
    }

    public double TransitionSeconds
    {
        get => Shot.In?.Seconds ?? new TransitionSpec().Seconds;
        set
        {
            if (Shot.In is null || value == Shot.In.Seconds) return;
            Shot = Shot with { In = Shot.In with { Seconds = value } };
            _changed();
        }
    }

    internal void Place(int number, double start, double end)
    {
        Number = number;
        StripStart = Math.Clamp(start, 0, 1);
        StripEnd = Math.Clamp(end, 0, 1);
    }
}

/// <summary>A composite preset in the tab's list.</summary>
public sealed record CompositeItem(CompositePreset Composite)
{
    public string Name => Composite.Name;
    public string? Description => Composite.Description;
}

/// <summary>A job of the render queue as a row of the tab.</summary>
public sealed record QueueRowViewModel(string Id, int Number, string Name, string Owner, string State, bool CanCancel)
{
    public string CancelId => $"Blender.Queue.{Number}.Cancel";
}
