// Content view-model for the Blender Animation dock tool (BA-B12, doc 05). Edits a SceneSpec
// (the library's single source of truth): a preset strip replaces the whole presentation;
// per-knob bindings tweak it; SceneSpecValidator drives the blocker list + Render enablement.
// The Render/Export commands run off the UI thread via the render service; progress arrives
// through Progress<RenderProgress> (which marshals back to the UI thread) so no manual OnUi is
// needed. The VM holds ZERO pipeline logic — Therion.Blender computes, the VM orchestrates.

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
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed partial class BlenderAnimationViewModel : ObservableObject
{
    private readonly IBlenderRenderService? _renderService;
    private readonly IBlenderSourceProvider? _sources;
    private readonly PresetStore? _presetStore;

    private static readonly HashSet<string> KnobNames = new(StringComparer.Ordinal)
    {
        nameof(CameraTemplate), nameof(EngineKind), nameof(Gpu), nameof(Samples), nameof(Fps),
        nameof(DurationSeconds), nameof(OutputKind), nameof(Container), nameof(Width), nameof(Height),
        nameof(BaseName), nameof(SelfContained), nameof(ShowStationLabels), nameof(ShowLeadMarkers),
    };

    private SceneSpec _spec;
    private bool _suspend;               // suppress knob → spec sync during a bulk apply
    private CancellationTokenSource? _cts;

    public ObservableCollection<PresetItem> Presets { get; } = [];
    public ObservableCollection<ArtifactItem> Artifacts { get; } = [];
    public ObservableCollection<string> Blockers { get; } = [];
    public ObservableCollection<string> Outputs { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>Design-time constructor.</summary>
    public BlenderAnimationViewModel() : this(null, null, null) { }

    public BlenderAnimationViewModel(
        IBlenderRenderService? renderService,
        IBlenderSourceProvider? sources,
        PresetStore? presetStore)
    {
        _renderService = renderService;
        _sources = sources;
        _presetStore = presetStore;

        _spec = BuiltInPresets.OrbitShowcase().Spec;
        LoadPresets();
        ReloadArtifacts();
        PushSpecToKnobs(_spec);
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
    [ObservableProperty] private bool _selfContained;
    [ObservableProperty] private bool _showStationLabels;
    [ObservableProperty] private bool _showLeadMarkers;

    // ---- export target ----
    [ObservableProperty] private string? _exportDirectory;

    // ---- run state ----
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string? _device;
    [ObservableProperty] private string? _lastError;

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

    // ---- change wiring ----

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suspend || e.PropertyName is null) return;
        if (KnobNames.Contains(e.PropertyName)) ApplyKnobsToSpec();
        else if (e.PropertyName is nameof(UseExternalFile) or nameof(ExternalFilePath) or nameof(SelectedArtifact) or nameof(IsBusy))
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
            Source = _spec.Source with { SelfContained = SelfContained },
            Labels = _spec.Labels with
            {
                Stations = _spec.Labels.Stations with { Show = ShowStationLabels },
                Leads = _spec.Labels.Leads with { Show = ShowLeadMarkers },
            },
        };
        Revalidate();
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
        ShowStationLabels = spec.Labels.Stations.Show;
        ShowLeadMarkers = spec.Labels.Leads.Show;
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
        foreach (var error in SceneSpecValidator.Validate(probe))
            Blockers.Add($"{error.Path}: {error.Message}");
        if (!HasSource)
            Blockers.Add(Tr.Get("Blender_Blocker_NoSource"));

        OnPropertyChanged(nameof(EstimatedFrameCount));
        OnPropertyChanged(nameof(CurrentSpec));
        RenderCommand.NotifyCanExecuteChanged();
        ExportScriptCommand.NotifyCanExecuteChanged();
    }

    private bool HasSource => UseExternalFile
        ? !string.IsNullOrWhiteSpace(ExternalFilePath)
        : SelectedArtifact is not null;

    private bool CanStart => !IsBusy && HasSource && Blockers.Count == 0 && _renderService is not null && _sources is not null;

    // ---- commands ----

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task RenderAsync()
    {
        Outputs.Clear();
        Warnings.Clear();
        LastError = null;
        IsBusy = true;
        RenderCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<RenderProgress>(OnProgress);
            StatusMessage = Tr.Get("Blender_Status_Acquiring");
            var source = await _sources!.AcquireAsync(BuildRequest(), _cts.Token).ConfigureAwait(true);
            var result = await _renderService!.RenderAsync(_spec, source, progress, _cts.Token).ConfigureAwait(true);
            ApplyResult(result);
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
            var scriptPath = await _renderService!.ExportScriptAsync(_spec, source, ExportDirectory!, progress, _cts.Token).ConfigureAwait(true);
            Outputs.Add(scriptPath);
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

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ReloadArtifacts()
    {
        Artifacts.Clear();
        if (_sources is null) return;
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
        if (SelectedArtifact is { } artifact)
            return ModelSourceRequest.ForExternalFile(artifact.Artifact.Path);
        return ModelSourceRequest.ForWorkspace(allowReExport: AllowReExport);
    }

    private void OnProgress(RenderProgress p)
    {
        IsIndeterminate = p.Fraction is null;
        ProgressFraction = p.Fraction ?? 0.0;
        if (p.Device is not null) Device = p.Device;
        StatusMessage = p is { Frame: int f, FrameCount: int n }
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} {1}/{2}", Tr.Get("Blender_Phase_Rendering"), f, n)
            : Tr.Get(PhaseKey(p.Phase));
    }

    private static string PhaseKey(RenderPhase phase) => phase switch
    {
        RenderPhase.AcquiringSource => "Blender_Phase_Acquiring",
        RenderPhase.ParsingModel or RenderPhase.ConvertingGeometry => "Blender_Phase_Converting",
        RenderPhase.GeneratingScript => "Blender_Phase_Generating",
        RenderPhase.Rendering => "Blender_Phase_Rendering",
        RenderPhase.CollectingOutputs => "Blender_Phase_Collecting",
        RenderPhase.Done => "Blender_Phase_Done",
        _ => "Blender_Phase_Working",
    };

    private void ApplyResult(RenderResult result)
    {
        Device = result.Device;
        foreach (var warning in result.Warnings) Warnings.Add(warning);
        if (result.Succeeded)
        {
            foreach (var path in result.OutputPaths) Outputs.Add(path);
            StatusMessage = Tr.Get("Blender_Status_Done");
        }
        else
        {
            LastError = result.ErrorMessage;
            StatusMessage = Tr.Get("Blender_Status_Failed");
        }
    }
}

/// <summary>A preset in the gallery strip (built-in or user).</summary>
public sealed record PresetItem(string Name, string? Description, bool BuiltIn, RenderPreset Preset);

/// <summary>A discovered workspace model artifact in the source dropdown.</summary>
public sealed record ArtifactItem(string DisplayName, ModelArtifact Artifact);
