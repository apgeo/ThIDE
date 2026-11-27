// Content view-model for the Blender Animation dock tool (BA-B12, doc 05). Edits a SceneSpec
// (the library's single source of truth): a preset strip replaces the whole presentation;
// per-knob bindings tweak it; SceneSpecValidator drives the blocker list + Render enablement.
// The Render/Export commands run off the UI thread via the render service; progress arrives
// through Progress<RenderProgress> (which marshals back to the UI thread) so no manual OnUi is
// needed. The VM holds ZERO pipeline logic — Therion.Blender computes, the VM orchestrates.
//
// Beside the knobs it holds the scene's annotations, which come from a JSON file (the one the
// command line and the agent tools take) and are shown as a count with what is wrong with them.
// Files are named in text fields; a "Browse" command asks the view for one through PickFile.
// "Export script…" and "Open in Blender…" go through the service's export, by the builder that
// "Render" uses.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using Therion.Build;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed partial class BlenderAnimationViewModel : ObservableObject
{
    private readonly IBlenderRenderService? _renderService;
    private readonly IBlenderSourceProvider? _sources;
    private readonly PresetStore? _presetStore;
    private readonly INotificationService? _notifications;
    private readonly IShellOpener? _shell;
    private readonly IBlenderGuiLauncher? _gui;
    private string? _lastJobLogPath;

    private static readonly HashSet<string> KnobNames = new(StringComparer.Ordinal)
    {
        nameof(CameraTemplate), nameof(EngineKind), nameof(Gpu), nameof(Samples), nameof(Fps),
        nameof(DurationSeconds), nameof(OutputKind), nameof(Container), nameof(Width), nameof(Height),
        nameof(BaseName), nameof(SelfContained), nameof(EmbedMesh), nameof(ShowStationLabels), nameof(ShowLeadMarkers),
        nameof(LeadMarkerSizing), nameof(LabelRoof), nameof(SurfaceMode), nameof(SurfaceOpacity),
        nameof(Title), nameof(Credits), nameof(EndCardText), nameof(EndCardSeconds),
        nameof(Subtitle), nameof(ShowScaleBar), nameof(ShowNorthArrow), nameof(ShowDepthLegend),
        nameof(FadeFromBlack), nameof(FadeToBlack),
        nameof(LightRig), nameof(LightStrength), nameof(HdriPath), nameof(HdriRotation), nameof(LampReach),
        nameof(RevealMode), nameof(RevealFrom), nameof(RevealDatesPath), nameof(RevealStart),
        nameof(RevealToClipEnd), nameof(RevealEnd), nameof(RevealEdge), nameof(RevealGlow),
    };

    private SceneSpec _spec;
    private bool _suspend;               // suppress knob → spec sync during a bulk apply
    private CancellationTokenSource? _cts;

    public ObservableCollection<PresetItem> Presets { get; } = [];
    public ObservableCollection<ArtifactItem> Artifacts { get; } = [];
    public ObservableCollection<string> Blockers { get; } = [];
    public ObservableCollection<string> Outputs { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>What is wrong with the annotations: a file that could not be read, what the
    /// validator says of them, a photo that is missing or cannot be used.</summary>
    public ObservableCollection<string> AnnotationProblems { get; } = [];

    /// <summary>Most-recent-first history of finished render jobs (BA-B13).</summary>
    public ObservableCollection<JobHistoryEntry> Jobs { get; } = [];

    /// <summary>Design-time constructor.</summary>
    public BlenderAnimationViewModel() : this(null, null, null) { }

    public BlenderAnimationViewModel(
        IBlenderRenderService? renderService,
        IBlenderSourceProvider? sources,
        PresetStore? presetStore,
        INotificationService? notifications = null,
        IShellOpener? shell = null,
        IBlenderGuiLauncher? gui = null,
        RenderQueue? queue = null)
    {
        _queue = queue;
        _ui = SynchronizationContext.Current;
        if (_queue is not null)
        {
            _queue.Changed += (_, _) => OnUi(OnQueueChanged);
            _queue.JobEnded += (_, job) => { if (job.Request.Owner != PanelHolder) OnUi(() => OnOtherRenderEnded(job)); };
        }
        _renderService = renderService;
        _sources = sources;
        _presetStore = presetStore;
        _notifications = notifications;
        _shell = shell;
        _gui = gui;

        _spec = BuiltInPresets.OrbitShowcase().Spec;
        LoadPresets();
        LoadComposites();
        ReloadArtifacts();
        // The knobs take the spec's values, one after another; the spec is not rebuilt from the
        // half of them that have theirs.
        _suspend = true;
        try { PushSpecToKnobs(_spec); }
        finally { _suspend = false; }
        Revalidate();
    }

    // ---- source ----
    [ObservableProperty] private bool _useExternalFile;
    [ObservableProperty] private string? _externalFilePath;
    [ObservableProperty] private ArtifactItem? _selectedArtifact;
    [ObservableProperty] private bool _allowReExport = true;

    // ---- presets ----
    [ObservableProperty] private PresetItem? _selectedPreset;
    [ObservableProperty] private string _newPresetName = "";

    // ---- spec knobs (project onto _spec) ----
    [ObservableProperty] private CameraTemplate _cameraTemplate;
    [ObservableProperty] private RenderEngineKind _engineKind;
    [ObservableProperty] private GpuMode _gpu;
    [ObservableProperty] private int _samples;
    [ObservableProperty] private int _fps;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private OutputKind _outputKind;
    [ObservableProperty] private VideoContainer _container;
    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private string _baseName = "cave-render";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmbedMesh))]
    private bool _selfContained;
    /// <summary>The self-contained script carries the meshes too, so it needs no file beside
    /// it. Only with <see cref="SelfContained"/>; turning that off turns this off.</summary>
    [ObservableProperty] private bool _embedMesh;
    [ObservableProperty] private bool _showStationLabels;
    [ObservableProperty] private bool _showLeadMarkers;
    [ObservableProperty] private LeadMarkerSizing _leadMarkerSizing;
    [ObservableProperty] private LabelRoofSource _labelRoof;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSurface))]
    private SurfaceMode _surfaceMode;

    /// <summary>How opaque the terrain is seen from above, 0–1.</summary>
    [ObservableProperty] private double _surfaceOpacity;

    /// <summary>True when a terrain is drawn: only then does its opacity matter.</summary>
    public bool HasSurface => SurfaceMode != SurfaceMode.Off;

    /// <summary>The title; empty is none. <see cref="TextPlaceholders.Cave"/> in it, as in the
    /// other texts, stands for the cave's name, which is filled in from the model.</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>The credits line; empty is none.</summary>
    [ObservableProperty] private string _credits = "";

    /// <summary>The text of the card the movie ends on; empty is no card.</summary>
    [ObservableProperty] private string _endCardText = "";

    /// <summary>How long the end card stays, in seconds.</summary>
    [ObservableProperty] private double _endCardSeconds = new EndCardSpec().Seconds;

    /// <summary>A smaller line under the title; empty is none. Not shown without a title.</summary>
    [ObservableProperty] private string _subtitle = "";

    [ObservableProperty] private bool _showScaleBar;
    [ObservableProperty] private bool _showNorthArrow;
    [ObservableProperty] private bool _showDepthLegend;

    /// <summary>How long the movie takes to come out of black at its start, and to go to black
    /// at its end, in seconds; 0 is no fade.</summary>
    [ObservableProperty] private double _fadeFromBlack;
    [ObservableProperty] private double _fadeToBlack;

    // ---- light ----

    /// <summary>The light rig. <see cref="LightingRig.HdriFile"/> lights the scene by the image
    /// of <see cref="HdriPath"/>, which is also what is seen behind the cave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHdri))]
    [NotifyPropertyChangedFor(nameof(IsHeadlamp))]
    private LightingRig _lightRig;

    /// <summary>How brightly the rig shines: 1 is the rig, or the image, as it is.</summary>
    [ObservableProperty] private double _lightStrength = 1.0;

    /// <summary>The image of an HDRI rig: a Radiance <c>.hdr</c> or an OpenEXR <c>.exr</c> file.</summary>
    [ObservableProperty] private string _hdriPath = "";

    /// <summary>How far the image is turned about the vertical, in degrees.</summary>
    [ObservableProperty] private double _hdriRotation;

    /// <summary>True when the scene is lit by an image: only then do its file and its turn matter.</summary>
    public bool IsHdri => LightRig == LightingRig.HdriFile;

    /// <summary>The head lamp is turned up with the distance the camera looks over, so that a
    /// cave seen from far away is lit as one seen from near.</summary>
    [ObservableProperty] private bool _lampReach;

    /// <summary>True when the lamp on the camera lights the scene: only then does its reach matter.</summary>
    public bool IsHeadlamp => LightRig == LightingRig.Headlamp;

    // ---- reveal ----

    /// <summary>What the cave is revealed by while the clip runs; <see cref="RevealChoice.Off"/>
    /// shows all of it throughout.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReveal), nameof(RevealsByDistance), nameof(RevealsByDate), nameof(RevealHasItsOwnEnd))]
    private RevealChoice _revealMode;

    /// <summary>By distance: the station the distances are measured from; empty measures from
    /// the entrances.</summary>
    [ObservableProperty] private string _revealFrom = "";

    /// <summary>By date: a <c>.3d</c> file of the same cave that has the survey dates; empty
    /// looks for one of the same name beside the model.</summary>
    [ObservableProperty] private string _revealDatesPath = "";

    /// <summary>When the reveal starts, in seconds.</summary>
    [ObservableProperty] private double _revealStart;

    /// <summary>The whole cave is there at the end of the clip; unticked, at <see cref="RevealEnd"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealHasItsOwnEnd))]
    private bool _revealToClipEnd = true;

    /// <summary>When the whole cave is there, in seconds, unless <see cref="RevealToClipEnd"/>.</summary>
    [ObservableProperty] private double _revealEnd;

    /// <summary>How soft the edge of the reveal is, as a share of the whole reveal; 0 is a cut.</summary>
    [ObservableProperty] private double _revealEdge = new RevealSpec().Edge;

    /// <summary>Whether what has just appeared glows.</summary>
    [ObservableProperty] private bool _revealGlow = new RevealSpec().Glow;

    public bool HasReveal => RevealMode != RevealChoice.Off;
    public bool RevealsByDistance => RevealMode == RevealChoice.Distance;
    public bool RevealsByDate => RevealMode == RevealChoice.Date;
    public bool RevealHasItsOwnEnd => HasReveal && !RevealToClipEnd;

    /// <summary>What a reveal has beside what the panel shows (the color of its glow, the
    /// counter of the years): kept while the reveal is off, for when it is turned on again.</summary>
    private RevealSpec _revealKept = new();

    // ---- annotations ----

    /// <summary>The scene's annotations: a preset's, or those of the file that was loaded.</summary>
    private IReadOnlyList<AnnotationSpec> _annotations = [];

    /// <summary>What is wrong with the photos' files, found when the annotations were taken.</summary>
    private IReadOnlyList<string> _photoProblems = [];

    /// <summary>Why the file that was last asked for was not loaded; null when it was.</summary>
    private string? _annotationsNotLoaded;

    /// <summary>The annotations file to load: a JSON list of labels, callouts, markers and photos.</summary>
    [ObservableProperty] private string _annotationsPath = "";

    /// <summary>How many annotations the scene has, and of which kinds.</summary>
    [ObservableProperty] private string _annotationsSummary = "";

    /// <summary>True when the scene has annotations: they can be cleared.</summary>
    public bool HasAnnotations => _annotations.Count > 0;

    /// <summary>Loads the annotations of <see cref="AnnotationsPath"/> in place of the scene's.
    /// A file that cannot be read leaves the scene's as they are, and says why.</summary>
    [RelayCommand]
    private void LoadAnnotations()
    {
        string path = (AnnotationsPath ?? "").Trim();
        if (path.Length == 0) return;
        string name = System.IO.Path.GetFileName(path);
        try
        {
            SetAnnotations(AnnotationsFile.ReadFile(path));
        }
        catch (SceneSpecFormatException ex)
        {
            AnnotationsNotLoaded(string.Format(Tr.Get("Blender_Annotations_NotAList"), name, ex.Message));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AnnotationsNotLoaded(string.Format(Tr.Get("Blender_Annotations_NotRead"), name, IoErrorText.Reason(ex)));
        }
    }

    /// <summary>Takes the scene's annotations away.</summary>
    [RelayCommand(CanExecute = nameof(HasAnnotations))]
    private void ClearAnnotations() => SetAnnotations([]);

    private void SetAnnotations(IReadOnlyList<AnnotationSpec> annotations)
    {
        _annotations = annotations;
        _annotationsNotLoaded = null;
        _photoProblems = PhotoProblems(annotations);
        ApplyKnobsToSpec();
    }

    private void AnnotationsNotLoaded(string why)
    {
        _annotationsNotLoaded = why;
        Revalidate();
    }

    /// <summary>What is wrong with the files of the photos: one that is not there, or is not an
    /// image a scene carries (a PNG or a JPEG, within the sizes <see cref="PlanImage"/> allows).</summary>
    private static List<string> PhotoProblems(IReadOnlyList<AnnotationSpec> annotations)
    {
        var problems = new List<string>();
        for (int i = 0; i < annotations.Count; i++)
        {
            if (annotations[i] is not { Kind: AnnotationKind.Photo, Image: { Length: > 0 } image } photo) continue;
            string id = photo.IdAt(i), name = System.IO.Path.GetFileName(image);
            try
            {
                if (!System.IO.File.Exists(image)) problems.Add(string.Format(Tr.Get("Blender_Annotations_PhotoMissing"), id, name));
                else PlanImage.Check(image);
            }
            catch (PlanImageException ex)
            {
                problems.Add(string.Format(Tr.Get("Blender_Annotations_PhotoRefused"), id, name, ex.Message));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                problems.Add(string.Format(Tr.Get("Blender_Annotations_PhotoRefused"), id, name, IoErrorText.Reason(ex)));
            }
        }
        return problems;
    }

    /// <summary>The count of the annotations with their kinds, and what is wrong with them:
    /// <paramref name="invalid"/> is what the validator says of them.</summary>
    private void RefreshAnnotations(IEnumerable<string> invalid)
    {
        int Of(AnnotationKind kind) => _annotations.Count(a => a.Kind == kind);
        AnnotationsSummary = _annotations.Count == 0
            ? Tr.Get("Blender_Annotations_None")
            : Tr.Plural("Blender_Annotations_Count", _annotations.Count) + ": " + string.Format(
                Tr.Get("Blender_Annotations_Kinds"),
                Of(AnnotationKind.Label), Of(AnnotationKind.Callout), Of(AnnotationKind.Marker), Of(AnnotationKind.Photo));
        AnnotationProblems.Clear();
        if (_annotationsNotLoaded is { } notLoaded) AnnotationProblems.Add(notLoaded);
        foreach (string problem in invalid.Concat(_photoProblems)) AnnotationProblems.Add(problem);
        OnPropertyChanged(nameof(HasAnnotations));
        ClearAnnotationsCommand.NotifyCanExecuteChanged();
    }

    // ---- files ----

    /// <summary>Asks the user for a file of a kind; null when they chose none. Set by the view,
    /// which owns the file dialog; without it the paths are typed.</summary>
    public Func<BlenderFileKind, Task<string?>>? PickFile { get; set; }

    /// <summary>Picks the image the scene is lit by, and lights the scene by it.</summary>
    [RelayCommand]
    private async Task BrowseHdriAsync()
    {
        if (await PickAsync(BlenderFileKind.HdriImage) is not { } path) return;
        HdriPath = path;
        LightRig = LightingRig.HdriFile;
    }

    /// <summary>Picks the file with the survey dates of a reveal by date.</summary>
    [RelayCommand]
    private async Task BrowseRevealDatesAsync()
    {
        if (await PickAsync(BlenderFileKind.RevealDates) is { } path) RevealDatesPath = path;
    }

    /// <summary>Picks an annotations file and loads it.</summary>
    [RelayCommand]
    private async Task BrowseAnnotationsAsync()
    {
        if (await PickAsync(BlenderFileKind.Annotations) is not { } path) return;
        AnnotationsPath = path;
        LoadAnnotations();
    }

    private async Task<string?> PickAsync(BlenderFileKind kind)
        => PickFile is { } pick && await pick(kind).ConfigureAwait(true) is { Length: > 0 } path ? path : null;

    // ---- captions ----

    /// <summary>How long a caption that is added lasts, in seconds.</summary>
    public const double NewCaptionSeconds = 3.0;

    /// <summary>The captions, as rows of the panel: each a text and the seconds it is shown
    /// from and to. A row without a text is not yet a caption and is left out of the spec.</summary>
    public ObservableCollection<CaptionRowViewModel> Captions { get; } = [];

    /// <summary>True when there are rows: the strip that shows them over the clip is drawn.</summary>
    public bool HasCaptions => Captions.Count > 0;

    /// <summary>The seconds the strip of the captions stands for: the clip, or as far as the
    /// last caption reaches when that is further (the validator does not cut a caption off at
    /// the end of the clip; the strip shows how far past it one runs).</summary>
    public double CaptionStripSeconds
    {
        get
        {
            double clip = double.IsFinite(DurationSeconds) && DurationSeconds > 0 ? DurationSeconds : 0;
            double last = Captions.Count == 0 ? 0 : Captions.Max(c => double.IsFinite(c.End) ? c.End : 0);
            return Math.Max(Math.Max(clip, last), 1e-3);
        }
    }

    /// <summary>Where the clip ends on the strip, as a share of it: 1, unless a caption runs
    /// past the end of the clip.</summary>
    public double ClipEndOnStrip => Math.Clamp((double.IsFinite(DurationSeconds) ? DurationSeconds : 0) / CaptionStripSeconds, 0, 1);

    /// <summary>Adds a caption after the last one, <see cref="NewCaptionSeconds"/> long and not
    /// past the end of the clip when it starts inside it. Its text is for the user to type.</summary>
    [RelayCommand]
    private void AddCaption()
    {
        double start = Captions.Count == 0 ? 0 : Math.Round(Captions.Max(c => double.IsFinite(c.End) ? c.End : 0), 2);
        double end = start + NewCaptionSeconds;
        if (start < DurationSeconds) end = Math.Min(end, DurationSeconds);
        Captions.Add(NewCaptionRow("", start, Math.Round(end, 2)));
        CaptionsChanged();
    }

    [RelayCommand]
    private void RemoveCaption(CaptionRowViewModel? row)
    {
        if (row is null || !Captions.Remove(row)) return;
        CaptionsChanged();
    }

    private CaptionRowViewModel NewCaptionRow(string text, double start, double end)
        => new(text, start, end, () => { if (!_suspend) CaptionsChanged(); });

    /// <summary>The rows changed (one was added, removed or edited): the spec takes them, and
    /// the rows take their numbers and their places on the strip.</summary>
    private void CaptionsChanged()
    {
        ApplyKnobsToSpec();
        RefreshCaptionStrip();
    }

    /// <summary>Numbers the rows and places each on the strip, by the seconds it is shown from
    /// and to. A row that is not shown (no text, or no time) is marked so.</summary>
    private void RefreshCaptionStrip()
    {
        double seconds = CaptionStripSeconds;
        for (int i = 0; i < Captions.Count; i++)
            Captions[i].Place(i + 1, seconds);
        OnPropertyChanged(nameof(HasCaptions));
        OnPropertyChanged(nameof(CaptionStripSeconds));
        OnPropertyChanged(nameof(ClipEndOnStrip));
    }

    // ---- how the render is made (not part of the scene, so not of a preset) ----

    /// <summary>What builds the scene in Blender, for a render, an export and a scene opened
    /// in Blender; a preview is always the player's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmbedMesh))]
    private SceneBuilder _builder = SceneBuilder.Auto;

    /// <summary>True when "Embed the meshes" can be chosen: for the script generator's
    /// self-contained script. The player's one file always carries them.</summary>
    public bool CanEmbedMesh => SelfContained && Builder == SceneBuilder.Generator;

    // ---- export target ----
    [ObservableProperty] private string? _exportDirectory;

    /// <summary>
    /// Where a render's video, frames or still go. Defaults to a folder of the user's own, not the
    /// temporary job folder: temp is cleaned by the OS, and the job folder is only the render's scratch.
    /// Empty puts the outputs in the job folder.
    /// </summary>
    [ObservableProperty] private string? _renderDirectory = DefaultRenderDirectory();

    /// <summary>Documents/ThIDE/Renders (the home folder when the platform has no Documents folder).</summary>
    public static string DefaultRenderDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(documents, "ThIDE", "Renders");
    }

    // ---- run state ----
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _device;
    [ObservableProperty] private string? _lastError;

    /// <summary>True after a render failed because Blender is missing/too old — the view shows a
    /// "set the path in Preferences" affordance instead of a raw error (NFR-05).</summary>
    [ObservableProperty] private bool _blenderMissing;

    /// <summary>Frames the render will produce (still set ⇒ view count; else fps × duration).</summary>
    public int EstimatedFrameCount => SceneSpecValidator.FrameCount(_spec);

    /// <summary>The spec currently being edited (read-only view for callers/tests).</summary>
    public SceneSpec CurrentSpec => _spec;

    /// <summary>Available enum choices for the view's dropdowns.</summary>
    public IReadOnlyList<CameraTemplate> CameraTemplates { get; } = Enum.GetValues<CameraTemplate>();
    public IReadOnlyList<RenderEngineKind> Engines { get; } = Enum.GetValues<RenderEngineKind>();
    public IReadOnlyList<GpuMode> GpuModes { get; } = Enum.GetValues<GpuMode>();
    public IReadOnlyList<OutputKind> OutputKinds { get; } = Enum.GetValues<OutputKind>();
    public IReadOnlyList<VideoContainer> Containers { get; } = Enum.GetValues<VideoContainer>();
    public IReadOnlyList<LeadMarkerSizing> LeadMarkerSizings { get; } = Enum.GetValues<LeadMarkerSizing>();
    public IReadOnlyList<LabelRoofSource> LabelRoofSources { get; } = Enum.GetValues<LabelRoofSource>();
    public IReadOnlyList<SurfaceMode> SurfaceModes { get; } = Enum.GetValues<SurfaceMode>();
    public IReadOnlyList<SceneBuilder> Builders { get; } = Enum.GetValues<SceneBuilder>();
    public IReadOnlyList<LightingRig> LightRigs { get; } = Enum.GetValues<LightingRig>();
    public IReadOnlyList<RevealChoice> RevealChoices { get; } = Enum.GetValues<RevealChoice>();

    // ---- change wiring ----

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suspend || e.PropertyName is null) return;
        if (KnobNames.Contains(e.PropertyName))
        {
            ApplyKnobsToSpec();
            // The strip of the captions is as long as the clip.
            if (e.PropertyName == nameof(DurationSeconds)) RefreshCaptionStrip();
        }
        else if (e.PropertyName is nameof(UseExternalFile) or nameof(ExternalFilePath) or nameof(SelectedArtifact) or nameof(IsBusy)
                 or nameof(Builder))
            Revalidate();
    }

    partial void OnSelectedPresetChanged(PresetItem? value)
    {
        if (value is null) return;
        _suspend = true;
        try
        {
            // Keep the current source/output location; take the preset's presentation.
            _spec = value.Preset.Spec with { Output = value.Preset.Spec.Output with { BaseName = BaseName } };
            // The annotations are the preset's now, not those of a file.
            AnnotationsPath = "";
            PushSpecToKnobs(_spec);
        }
        finally { _suspend = false; }
        Revalidate();
    }

    private void ApplyKnobsToSpec()
    {
        _spec = _spec with
        {
            Camera = _spec.Camera with { Template = CameraTemplate },
            Engine = _spec.Engine with { Kind = EngineKind, Gpu = Gpu, Samples = Samples },
            Animation = _spec.Animation with { Fps = Fps, DurationSeconds = DurationSeconds },
            Output = _spec.Output with { Kind = OutputKind, Container = Container, Width = Width, Height = Height, BaseName = BaseName },
            Source = _spec.Source with { SelfContained = SelfContained, EmbedMesh = SelfContained && EmbedMesh },
            Labels = _spec.Labels with
            {
                Stations = _spec.Labels.Stations with { Show = ShowStationLabels },
                Leads = _spec.Labels.Leads with { Show = ShowLeadMarkers, Sizing = LeadMarkerSizing },
                Roof = LabelRoof,
                Overlays = _spec.Labels.Overlays with
                {
                    Title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(Subtitle) ? null : Subtitle.Trim(),
                    ScaleBar = ShowScaleBar,
                    NorthArrow = ShowNorthArrow,
                    DepthLegend = ShowDepthLegend,
                    FadeFromBlack = FadeFromBlack,
                    FadeToBlack = FadeToBlack,
                    Credits = string.IsNullOrWhiteSpace(Credits) ? null : Credits.Trim(),
                    // A row without a text is not a caption yet.
                    Captions = Captions.Where(c => !string.IsNullOrWhiteSpace(c.Text))
                        .Select(c => new CaptionSpec { Text = c.Text.Trim(), Start = c.Start, End = c.End }).ToList(),
                    // What a preset's end card has beside its text and its time stays.
                    EndCard = string.IsNullOrWhiteSpace(EndCardText)
                        ? null
                        : (_spec.Labels.Overlays.EndCard ?? new EndCardSpec()) with { Text = EndCardText.Trim(), Seconds = EndCardSeconds },
                },
            },
            // A slider's steps are not exact in binary; the spec keeps whole percents.
            Surface = _spec.Surface with { Mode = SurfaceMode, Opacity = Math.Round(SurfaceOpacity, 2) },
            // The image stays with the scene while another rig lights it, for when it is chosen again.
            Lighting = _spec.Lighting with
            {
                Rig = LightRig,
                Strength = LightStrength,
                HdriPath = string.IsNullOrWhiteSpace(HdriPath) ? null : HdriPath.Trim(),
                HdriRotation = HdriRotation,
                LampReach = LampReach,
            },
            Reveal = RevealFromKnobs(),
            Annotations = _annotations,
        };
        if (_spec.Reveal is { } reveal) _revealKept = reveal;
        WriteSettingsToShot();
        Revalidate();
    }

    /// <summary>The reveal the knobs say, or none: what the panel does not show of it (the
    /// color of its glow, the counter of the years) is kept as it was.</summary>
    private RevealSpec? RevealFromKnobs()
    {
        if (RevealMode == RevealChoice.Off) return null;
        var by = RevealMode switch
        {
            RevealChoice.Date => RevealBy.Date,
            RevealChoice.Order => RevealBy.Order,
            _ => RevealBy.Distance,
        };
        return (_spec.Reveal ?? _revealKept) with
        {
            By = by,
            From = by == RevealBy.Distance && !string.IsNullOrWhiteSpace(RevealFrom) ? RevealFrom.Trim() : null,
            DatesPath = by == RevealBy.Date && !string.IsNullOrWhiteSpace(RevealDatesPath) ? RevealDatesPath.Trim() : null,
            Start = RevealStart,
            End = RevealToClipEnd ? null : RevealEnd,
            Edge = RevealEdge,
            Glow = RevealGlow,
        };
    }

    partial void OnRevealToClipEndChanged(bool value)
    {
        // An end of its own starts at the end of the clip, unless it lies inside the clip already.
        if (!value && !_suspend && !(RevealEnd > RevealStart && RevealEnd <= DurationSeconds)) RevealEnd = DurationSeconds;
    }

    private void PushSpecToKnobs(SceneSpec spec)
    {
        CameraTemplate = spec.Camera.Template;
        EngineKind = spec.Engine.Kind;
        Gpu = spec.Engine.Gpu;
        Samples = spec.Engine.Samples;
        Fps = spec.Animation.Fps;
        DurationSeconds = spec.Animation.DurationSeconds;
        OutputKind = spec.Output.Kind;
        Container = spec.Output.Container;
        Width = spec.Output.Width;
        Height = spec.Output.Height;
        BaseName = string.IsNullOrWhiteSpace(spec.Output.BaseName) ? "cave-render" : spec.Output.BaseName;
        SelfContained = spec.Source.SelfContained;
        EmbedMesh = spec.Source.SelfContained && spec.Source.EmbedMesh;
        ShowStationLabels = spec.Labels.Stations.Show;
        ShowLeadMarkers = spec.Labels.Leads.Show;
        LeadMarkerSizing = spec.Labels.Leads.Sizing;
        LabelRoof = spec.Labels.Roof;
        SurfaceMode = spec.Surface.Mode;
        SurfaceOpacity = spec.Surface.Opacity;
        Title = spec.Labels.Overlays.Title ?? "";
        Credits = spec.Labels.Overlays.Credits ?? "";
        Captions.Clear();
        foreach (var caption in spec.Labels.Overlays.Captions)
            Captions.Add(NewCaptionRow(caption.Text, caption.Start, caption.End));
        RefreshCaptionStrip();
        EndCardText = spec.Labels.Overlays.EndCard?.Text ?? "";
        EndCardSeconds = spec.Labels.Overlays.EndCard?.Seconds ?? new EndCardSpec().Seconds;
        Subtitle = spec.Labels.Overlays.Subtitle ?? "";
        ShowScaleBar = spec.Labels.Overlays.ScaleBar;
        ShowNorthArrow = spec.Labels.Overlays.NorthArrow;
        ShowDepthLegend = spec.Labels.Overlays.DepthLegend;
        FadeFromBlack = spec.Labels.Overlays.FadeFromBlack;
        FadeToBlack = spec.Labels.Overlays.FadeToBlack;

        LightRig = spec.Lighting.Rig;
        LightStrength = spec.Lighting.Strength;
        HdriPath = spec.Lighting.HdriPath ?? "";
        HdriRotation = spec.Lighting.HdriRotation;
        LampReach = spec.Lighting.LampReach;

        var reveal = spec.Reveal;
        _revealKept = reveal ?? new RevealSpec();
        RevealMode = reveal?.By switch
        {
            null => RevealChoice.Off,
            RevealBy.Date => RevealChoice.Date,
            RevealBy.Order => RevealChoice.Order,
            _ => RevealChoice.Distance,
        };
        RevealFrom = reveal?.From ?? "";
        RevealDatesPath = reveal?.DatesPath ?? "";
        RevealStart = reveal?.Start ?? 0;
        RevealToClipEnd = reveal?.End is null;
        RevealEnd = reveal?.End ?? spec.Animation.DurationSeconds;
        RevealEdge = _revealKept.Edge;
        RevealGlow = _revealKept.Glow;

        _annotations = spec.Annotations;
        _annotationsNotLoaded = null;
        _photoProblems = PhotoProblems(_annotations);
    }

    private void Revalidate()
    {
        // Validate a copy with placeholder source/output filled — those are supplied by the
        // service at render time, not user-fixable blockers.
        var probe = _spec with
        {
            Source = _spec.Source with { PlyPath = "model.ply" },
            Output = _spec.Output with { OutputDirectory = "out" },
        };
        Blockers.Clear();
        var errors = SceneSpecValidator.Validate(probe);
        foreach (var error in errors)
            Blockers.Add($"{error.Path}: {error.Message}");
        if (!HasSource)
            Blockers.Add(Tr.Get("Blender_Blocker_NoSource"));
        // The files the scene names: found before Blender is started, not by it.
        if (IsHdri && _spec.Lighting.HdriPath is { } image)
        {
            // The player lights a scene by a .hdr or an .exr; the script generator loads any picture.
            if (Builder != SceneBuilder.Generator && !PlanEnvironmentImage.HasExtension(image))
                Blockers.Add(Tr.Get("Blender_Blocker_HdriKind"));
            else if (!System.IO.File.Exists(image))
                Blockers.Add(string.Format(Tr.Get("Blender_Blocker_FileNotFound"), image));
        }
        if (_spec.Reveal is { By: RevealBy.Date, DatesPath: { } dates } && !System.IO.File.Exists(dates))
            Blockers.Add(string.Format(Tr.Get("Blender_Blocker_FileNotFound"), dates));
        foreach (string problem in _photoProblems) Blockers.Add(problem);
        RefreshAnnotations(errors.Where(e => e.Path.StartsWith("annotations", StringComparison.Ordinal)).Select(e => $"{e.Path}: {e.Message}"));

        OnPropertyChanged(nameof(EstimatedFrameCount));
        OnPropertyChanged(nameof(CurrentSpec));
        RenderCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        ExportScriptCommand.NotifyCanExecuteChanged();
        OpenInBlenderGuiCommand.NotifyCanExecuteChanged();
        RevalidateCommands();
    }

    private bool HasSource => UseExternalFile
        ? !string.IsNullOrWhiteSpace(ExternalFilePath)
        : SelectedArtifact is not null;

    private bool CanStart => !IsBusy && HasSource && Blockers.Count == 0 && _renderService is not null && _sources is not null;

    private readonly RenderQueue? _queue;
    private readonly SynchronizationContext? _ui;

    /// <summary>A render the panel did not start (an agent's) is running: one the panel starts
    /// meanwhile waits for it in the queue.</summary>
    public bool AgentRendering => _queue?.Running is { } running && running.Request.Owner is not (PanelHolder or RenderQueue.RestartOwner);

    private const string PanelHolder = "panel";

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }

    private void OnQueueChanged()
    {
        OnPropertyChanged(nameof(AgentRendering));
        OnPropertyChanged(nameof(ResumedRendering));
        RefreshQueueRows();
        // A render a restart started again is nobody's now: it is not an agent's.
        if (ResumedRendering && !IsBusy) StatusMessage = Tr.Get("Blender_Status_RestartRendering");
        else if (AgentRendering && !IsBusy) StatusMessage = Tr.Get("Blender_Status_AgentRendering");
    }

    /// <summary>A render the panel did not start ended (an agent's, or one a restart had cut
    /// short): it joins the history.</summary>
    private void OnOtherRenderEnded(RenderJobRecord job)
    {
        var result = job.Result;
        bool ok = job.State == RenderJobState.Succeeded;
        string status = ok ? Tr.Get("Blender_Status_Done")
            : job.State == RenderJobState.Cancelled ? Tr.Get("Blender_Status_Cancelled")
            : result?.Error ?? Tr.Get("Blender_Status_Failed");
        Jobs.Insert(0, new JobHistoryEntry(job.Request.Name, ok, status,
            ok ? [.. result?.Outputs ?? []] : [], result?.Log, TimeSpan.FromSeconds(result?.Seconds ?? 0)));
        if (!IsBusy) StatusMessage = status;
    }

    // ---- commands ----

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task RenderAsync() => RunRenderAsync(draft: false);

    /// <summary>A quick draft of the render, to see the camera move, the framing and the texts
    /// before the long render. Always built by the player.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task PreviewAsync() => RunRenderAsync(draft: true);

    private async Task RunRenderAsync(bool draft)
    {
        Outputs.Clear();
        Warnings.Clear();
        LastError = null;
        IsBusy = true;
        RenderCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<RenderProgress>(OnProgress);
            StatusMessage = Tr.Get("Blender_Status_Acquiring");
            var source = await _sources!.AcquireAsync(BuildRequest(), _cts.Token).ConfigureAwait(true);
            var spec = string.IsNullOrWhiteSpace(RenderDirectory)
                ? _spec
                : _spec with { Output = _spec.Output with { OutputDirectory = RenderDirectory.Trim() } };
            var options = new RenderOptions { Builder = Builder, Draft = draft, Owner = PanelHolder };
            var result = await _renderService!.RenderAsync(spec, source, options, progress, _cts.Token).ConfigureAwait(true);
            ApplyResult(result, draft);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr.Get("Blender_Status_Cancelled");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusMessage = Tr.Get("Blender_Status_Failed");
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            _cts?.Dispose();
            _cts = null;
            Revalidate();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportScriptAsync()
    {
        Outputs.Clear();
        Warnings.Clear();
        LastError = null;
        IsBusy = true;
        ExportScriptCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<RenderProgress>(OnProgress);
            StatusMessage = Tr.Get("Blender_Status_Exporting");
            var source = await _sources!.AcquireAsync(BuildRequest(), _cts.Token).ConfigureAwait(true);
            // The scene "Render" shows, by the builder it uses: the player's folder, or one
            // script with all of it inside; the script generator's script when it is chosen.
            var options = new ExportOptions
            {
                Builder = Builder,
                SingleFile = Builder != SceneBuilder.Generator && SelfContained,
                Language = Tr.Culture.TwoLetterISOLanguageName,
            };
            var export = await _renderService!.ExportAsync(_spec, source, ExportDirectory!.Trim(), options, progress, _cts.Token).ConfigureAwait(true);
            Outputs.Add(export.ScriptPath);
            AddExportWarnings(export);
            StatusMessage = Tr.Get("Blender_Status_Exported");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr.Get("Blender_Status_Cancelled");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusMessage = Tr.Get("Blender_Status_Failed");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            Revalidate();
        }
    }

    private bool CanExport => CanStart && !string.IsNullOrWhiteSpace(ExportDirectory);

    /// <summary>What an export says of its scene: what it shows less of than was asked for,
    /// and what the script generator does not draw.</summary>
    private void AddExportWarnings(RenderExport export)
    {
        foreach (string notice in export.Notices) Warnings.Add(notice);
        if (export.LeftOut.Count > 0)
            Warnings.Add(string.Format(Tr.Get("Blender_Builder_LeftOutGenerator"), string.Join(", ", export.LeftOut)));
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>True while a render or script export runs; closing ThIDE would stop it.</summary>
    public bool IsJobRunning => _cts is not null;

    /// <summary>Stops the running job at once, for closing ThIDE: cancelling kills Blender's
    /// process tree before this returns.</summary>
    public void StopJob() => _cts?.Cancel();

    [RelayCommand]
    private void ReloadArtifacts()
    {
        Artifacts.Clear();
        if (_sources is null) return;
        // First and preferred: the project's own model, re-exported when the build is missing
        // or older than the sources.
        if (_sources.HasProject)
            Artifacts.Add(new ArtifactItem(Tr.Get("Blender_UI_ProjectModel"), null));
        foreach (var artifact in _sources.DiscoverArtifacts())
            Artifacts.Add(new ArtifactItem(
                System.IO.Path.GetFileName(artifact.Path) + $" ({artifact.Format})", artifact));
        SelectedArtifact ??= Artifacts.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        if (_presetStore is null) return;
        _presetStore.Save(new RenderPreset { Name = NewPresetName.Trim(), Spec = _spec, BuiltIn = false });
        LoadPresets();
        NewPresetName = "";
    }

    private bool CanSavePreset => _presetStore is not null && !string.IsNullOrWhiteSpace(NewPresetName);

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        if (_presetStore is null || SelectedPreset is not { BuiltIn: false } item) return;
        _presetStore.Delete(item.Name);
        LoadPresets();
    }

    private bool CanDeletePreset => _presetStore is not null && SelectedPreset is { BuiltIn: false };

    partial void OnNewPresetNameChanged(string value) => SavePresetCommand.NotifyCanExecuteChanged();
    partial void OnSelectedPresetChanging(PresetItem? value) => DeletePresetCommand.NotifyCanExecuteChanged();
    partial void OnExportDirectoryChanged(string? value) => ExportScriptCommand.NotifyCanExecuteChanged();

    partial void OnSelfContainedChanged(bool value)
    {
        if (!value) EmbedMesh = false;
    }

    // ---- helpers ----

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var preset in BuiltInPresets.All)
            Presets.Add(new PresetItem(preset.Name, preset.Description, true, preset));
        if (_presetStore is not null)
            foreach (var preset in _presetStore.Load())
                Presets.Add(new PresetItem(preset.Name, preset.Description, false, preset));
    }

    private ModelSourceRequest BuildRequest()
    {
        if (UseExternalFile && !string.IsNullOrWhiteSpace(ExternalFilePath))
            return ModelSourceRequest.ForExternalFile(ExternalFilePath);
        if (SelectedArtifact is { Artifact: { } artifact })
            return ModelSourceRequest.ForExternalFile(artifact.Path);
        return ModelSourceRequest.ForWorkspace(allowReExport: AllowReExport);
    }

    private void OnProgress(RenderProgress p)
    {
        IsIndeterminate = p.Fraction is null;
        ProgressFraction = p.Fraction ?? 0.0;
        if (p.Device is not null) Device = p.Device;
        StatusMessage = p is { Frame: int f, FrameCount: int n }
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} {1}/{2}",
                Tr.Get(p.Phase == RenderPhase.Composing ? "Blender_Phase_Composing" : "Blender_Phase_Rendering"), f, n)
              + (p.SecondsLeft is { } left
                  ? " · " + string.Format(System.Globalization.CultureInfo.CurrentCulture, Tr.Get("Blender_Status_TimeLeft"),
                      TimeSpan.FromSeconds(Math.Round(left)).ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture))
                  : "")
            : Tr.Get(PhaseKey(p.Phase));
    }

    private static string PhaseKey(RenderPhase phase) => phase switch
    {
        RenderPhase.AcquiringSource => "Blender_Phase_Acquiring",
        RenderPhase.ParsingModel or RenderPhase.ConvertingGeometry => "Blender_Phase_Converting",
        RenderPhase.GeneratingScript => "Blender_Phase_Generating",
        RenderPhase.Rendering => "Blender_Phase_Rendering",
        RenderPhase.Composing => "Blender_Phase_Composing",
        RenderPhase.CollectingOutputs => "Blender_Phase_Collecting",
        RenderPhase.Done => "Blender_Phase_Done",
        RenderPhase.Queued => "Blender_Phase_Queued",
        _ => "Blender_Phase_Working",
    };

    private void ApplyResult(RenderResult result, bool draft = false)
    {
        Device = result.Device;
        _lastJobLogPath = result.JobLogPath;
        BlenderMissing = result.FailureKind is RenderFailureKind.BlenderNotFound or RenderFailureKind.BlenderTooOld;
        foreach (var warning in result.Warnings) Warnings.Add(warning);
        // Which builder built the scene when it was not the one to expect, and what is not shown.
        if (result.Builder == SceneBuilder.Generator && result.NotCarried.Count > 0)
            Warnings.Add(string.Format(Tr.Get("Blender_Builder_Fallback"), string.Join(", ", result.NotCarried)));
        if (result.LeftOut.Count > 0)
            Warnings.Add(string.Format(
                Tr.Get(result.Builder == SceneBuilder.Generator ? "Blender_Builder_LeftOutGenerator" : "Blender_Builder_LeftOutPreview"),
                string.Join(", ", result.LeftOut)));

        string jobName = string.IsNullOrWhiteSpace(BaseName) ? Tr.Get("Blender_Tool_Title") : BaseName;
        if (draft) jobName = string.Format(Tr.Get("Blender_Job_Preview"), jobName);
        if (result.Succeeded)
        {
            foreach (var path in result.OutputPaths) Outputs.Add(path);
            StatusMessage = Tr.Get(draft ? "Blender_Status_PreviewDone" : "Blender_Status_Done");
            Jobs.Insert(0, new JobHistoryEntry(jobName, true, StatusMessage, [.. result.OutputPaths], result.JobLogPath, result.Duration));
            _notifications?.Success(Tr.Get("Blender_Tool_Title"), StatusMessage,
                _shell is not null && result.OutputPaths.Length > 0 ? Tr.Get("Blender_Notify_OpenFolder") : null,
                _shell is not null && result.OutputPaths.Length > 0 ? () => RevealOutput(result.OutputPaths[0]) : null);
        }
        else
        {
            LastError = FailureMessage(result);
            StatusMessage = Tr.Get("Blender_Status_Failed");
            Jobs.Insert(0, new JobHistoryEntry(jobName, false, LastError ?? StatusMessage, [], result.JobLogPath, result.Duration));
            _notifications?.Error(Tr.Get("Blender_Tool_Title"), LastError ?? StatusMessage,
                result.JobLogPath is not null && _shell is not null ? Tr.Get("Blender_Notify_OpenLog") : null,
                result.JobLogPath is not null && _shell is not null ? () => _shell.Open(result.JobLogPath) : null);
        }
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
        OpenJobLogCommand.NotifyCanExecuteChanged();
    }

    private static string FailureMessage(RenderResult result) => result.StartFailedPath is { } path
        ? string.Format(Tr.Get("Blender_Fail_StartFailed"), path)
            + (Therion.Blender.Execution.BlenderRunner.IsWindowsStorePath(path) ? " " + Tr.Get("Blender_Fail_StoreHint") : "")
        : result.FailureKind switch
    {
        RenderFailureKind.BlenderNotFound => Tr.Get("Blender_Fail_NotFound"),
        RenderFailureKind.BlenderTooOld => Tr.Get("Blender_Fail_TooOld"),
        RenderFailureKind.DiskSpace => Tr.Get("Blender_Fail_DiskSpace"),
        RenderFailureKind.Cancelled => Tr.Get("Blender_Status_Cancelled"),
        RenderFailureKind.BuilderRefused when result.NotCarried.Count > 0
            => string.Format(Tr.Get("Blender_Fail_BuilderRefused"), string.Join(", ", result.NotCarried)),
        _ => result.ErrorMessage ?? Tr.Get("Blender_Status_Failed"),
    };

    private void RevealOutput(string path)
    {
        if (_shell is null) return;
        if (!_shell.RevealInFileManager(path)) _shell.Open(System.IO.Path.GetDirectoryName(path) ?? path);
    }

    // ---- BA-B13 open/inspect commands ----

    [RelayCommand]
    private void OpenOutput(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) _shell?.Open(path);
    }

    /// <summary>Copies a finished render's output path(s) to the clipboard (or its status when the
    /// job produced no outputs), so the user can paste the result location elsewhere.</summary>
    [RelayCommand]
    private void CopyJob(JobHistoryEntry? entry)
    {
        if (entry is null) return;
        string text = entry.Outputs.Count > 0
            ? string.Join(Environment.NewLine, entry.Outputs)
            : entry.Status;
        ClipboardHelper.SetText(text);
    }

    [RelayCommand(CanExecute = nameof(HasOutputs))]
    private void OpenOutputFolder()
    {
        if (Outputs.Count > 0) RevealOutput(Outputs[0]);
    }

    private bool HasOutputs => Outputs.Count > 0;

    [RelayCommand(CanExecute = nameof(HasJobLog))]
    private void OpenJobLog()
    {
        if (_lastJobLogPath is { } log) _shell?.Open(log);
    }

    private bool HasJobLog => _lastJobLogPath is not null && _shell is not null;

    /// <summary>Writes the scene to a temp folder and opens it in the interactive Blender GUI so
    /// the user can inspect/tweak it (BA-B13): the scene "Render" shows, built by the builder it
    /// uses. The player builds it in Blender's window and stops; its texts over the picture are
    /// written in its second stage, after the frames, so the window does not show them.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenInBlender))]
    private async Task OpenInBlenderGuiAsync()
    {
        if (_gui is null || _renderService is null || _sources is null) return;
        Warnings.Clear();
        LastError = null;
        BlenderMissing = false;
        IsBusy = true;
        try
        {
            // The script Blender opens with lies in a folder of the user's alone, not in the temp
            // folder every user shares.
            var dir = System.IO.Path.Combine(
                Therion.Blender.Execution.PrivateFolders.Ensure(GuiFolder, tighten: true), System.Guid.NewGuid().ToString("N")[..8]);
            var progress = new Progress<RenderProgress>(OnProgress);
            var source = await _sources.AcquireAsync(BuildRequest()).ConfigureAwait(true);
            // A render started from Blender's window writes where "Render" would.
            var spec = string.IsNullOrWhiteSpace(RenderDirectory)
                ? _spec
                : _spec with { Output = _spec.Output with { OutputDirectory = RenderDirectory.Trim() } };
            var export = await _renderService.ExportAsync(
                spec, source, dir, new ExportOptions { Builder = Builder, Interactive = true }, progress).ConfigureAwait(true);
            if (!_gui.Launch(export.ScriptPath, export.ScriptArguments))
            {
                BlenderMissing = true;
                LastError = Tr.Get("Blender_Fail_NotFound");
            }
            else
            {
                StatusMessage = Tr.Get("Blender_Status_OpenedInBlender");
                AddExportWarnings(export);
                if (export.TextsInSecondStage) Warnings.Add(Tr.Get("Blender_OpenInBlender_TextsNote"));
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Revalidate();
        }
    }

    private bool CanOpenInBlender => CanStart && _gui is not null;

    /// <summary>Where the scenes opened in Blender's window are written, one folder each: a
    /// folder of the user's alone (<see cref="Therion.Blender.Execution.PrivateFolders"/>).</summary>
    internal string GuiFolder { get; set; } = Therion.Blender.Execution.PrivateFolders.Of("blender-gui");
}

/// <summary>
/// One caption as a row of the panel: its text, the seconds it is shown from and to, and where
/// it lies on the strip that shows the captions over the clip.
/// </summary>
public sealed partial class CaptionRowViewModel : ObservableObject
{
    private readonly Action _changed;

    public CaptionRowViewModel(string text, double start, double end, Action changed)
    {
        _text = text;
        _start = start;
        _end = end;
        _changed = changed;
    }

    /// <summary>What the caption says; empty, the row is not a caption yet.</summary>
    [ObservableProperty] private string _text;

    /// <summary>When it appears, in seconds from the start of the movie.</summary>
    [ObservableProperty] private double _start;

    /// <summary>When it goes, in seconds from the start of the movie.</summary>
    [ObservableProperty] private double _end;

    /// <summary>Its place among the rows, from 1: what the strip and the automation ids call it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextId), nameof(StartId), nameof(EndId), nameof(RemoveId))]
    private int _number;

    /// <summary>Where it starts and ends on the strip, as shares of the strip's length.</summary>
    [ObservableProperty] private double _stripStart;
    [ObservableProperty] private double _stripEnd;

    /// <summary>True when the movie shows it: it has a text, and ends after it starts.</summary>
    [ObservableProperty] private bool _isShown;

    public string TextId => $"Blender.Caption.{Number}.Text";
    public string StartId => $"Blender.Caption.{Number}.Start";
    public string EndId => $"Blender.Caption.{Number}.End";
    public string RemoveId => $"Blender.Caption.{Number}.Remove";

    partial void OnTextChanged(string value) => _changed();
    partial void OnStartChanged(double value) => _changed();
    partial void OnEndChanged(double value) => _changed();

    /// <summary>Gives the row its number and its place on a strip of
    /// <paramref name="stripSeconds"/> seconds.</summary>
    internal void Place(int number, double stripSeconds)
    {
        Number = number;
        bool timed = double.IsFinite(Start) && double.IsFinite(End) && Start >= 0 && End > Start;
        IsShown = timed && !string.IsNullOrWhiteSpace(Text);
        StripStart = timed ? Math.Clamp(Start / stripSeconds, 0, 1) : 0;
        StripEnd = timed ? Math.Clamp(End / stripSeconds, 0, 1) : 0;
    }
}

/// <summary>What the panel reveals a cave by; <see cref="Off"/> is no reveal.</summary>
public enum RevealChoice
{
    Off,
    Distance,
    Order,
    Date,
}

/// <summary>A file the panel asks the user for.</summary>
public enum BlenderFileKind
{
    /// <summary>The image a scene is lit by: a Radiance <c>.hdr</c> or an OpenEXR <c>.exr</c>.</summary>
    HdriImage,
    /// <summary>A model file with the survey dates of a reveal by date: a <c>.3d</c>.</summary>
    RevealDates,
    /// <summary>A JSON list of annotations.</summary>
    Annotations,
}

/// <summary>A preset in the gallery strip (built-in or user).</summary>
public sealed record PresetItem(string Name, string? Description, bool BuiltIn, RenderPreset Preset);

/// <summary>A discovered workspace model artifact in the source dropdown.</summary>
/// <summary>A source in the panel's dropdown: a model file, or (null) the project's model.</summary>
public sealed record ArtifactItem(string DisplayName, ModelArtifact? Artifact);

/// <summary>A finished render job in the panel's history (BA-B13).</summary>
public sealed record JobHistoryEntry(
    string Name, bool Succeeded, string Status, IReadOnlyList<string> Outputs, string? JobLogPath, System.TimeSpan Duration);
