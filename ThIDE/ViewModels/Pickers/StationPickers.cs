using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Semantics;
using Therion.Workspace.Linking;

namespace ThIDE.ViewModels.Pickers;

/// <summary>A picker over stations; payloads are <see cref="StationEntry"/>.</summary>
public sealed class StationPickerViewModel(string name) : ItemPickerViewModel(name)
{
    public StationEntry? SelectedStation => Selected?.Payload as StationEntry;

    /// <param name="showFile">Show the declaring file beside the survey (workspace lists span many files).</param>
    public void SetStations(IEnumerable<StationEntry> stations, bool showFile)
    {
        SetItems(stations.Select(s =>
        {
            var survey = string.Join('.', s.Effective.Parts.Take(s.Effective.Parts.Length - 1));
            var secondary = showFile ? $"{survey} · {Path.GetFileName(s.FilePath)}" : survey;
            return new PickerItem(s.Effective.Last, secondary, s.Effective + " " + Path.GetFileName(s.FilePath), 0, s);
        }).ToList());
    }

    /// <summary>Selects the station with this file-local name in this file.</summary>
    public bool Select(string filePath, QualifiedName local) =>
        SelectByPayload(p => p is StationEntry s && s.Local.Equals(local) && Therion.Workspace.Editing.FilePaths.Same(s.FilePath, filePath));
}

/// <summary>A picker over surveys shown as an indented tree; payloads are <see cref="SurveyEntry"/>.</summary>
public sealed class SurveyPickerViewModel(string name) : ItemPickerViewModel(name)
{
    public SurveyEntry? SelectedSurvey => Selected?.Payload as SurveyEntry;

    public void SetSurveys(IEnumerable<SurveyEntry> surveys) =>
        SetItems(surveys.Select(s => new PickerItem(s.Name, Path.GetFileName(s.FilePath),
            string.Join('.', s.Effective) + " " + Path.GetFileName(s.FilePath), s.Depth, s)).ToList());
}

/// <summary>
/// A station picker with a small preview under it: clicking a station in the sketch selects it, the
/// selected station is ringed, and the preview can be folded away or opened in its own window.
/// </summary>
public sealed partial class StationSelectorViewModel : ObservableObject
{
    public StationSelectorViewModel(string name)
    {
        Picker = new StationPickerViewModel(name);
        Picker.SelectionChanged += (_, _) => Highlight();
        Preview.StationActivated += (_, point) => OnPreviewStation(point.Name);
    }

    public StationPickerViewModel Picker { get; }
    public LivePreviewViewModel Preview { get; } = LivePreviewViewModel.CreateStandalone();

    [ObservableProperty] private bool _isPreviewVisible = true;

    /// <summary>Raised by the maximise button; the host opens the preview in a window.</summary>
    public event EventHandler? MaximizeRequested;

    [RelayCommand] private void TogglePreview() => IsPreviewVisible = !IsPreviewVisible;
    [RelayCommand] private void Maximize() => MaximizeRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Shows these stations and draws these models.</summary>
    public void Load(IEnumerable<StationEntry> stations, IReadOnlyList<SemanticModel> models, bool showFile)
    {
        Picker.SetStations(stations, showFile);
        Preview.SetModels(models);
        Highlight();
    }

    private void Highlight() =>
        Preview.SetHighlightedStations(Picker.SelectedStation is { } s ? [s.Local] : Array.Empty<QualifiedName>());

    // Preview points carry the model's own (file-local) station name.
    private void OnPreviewStation(string localName) =>
        Picker.SelectByPayload(p => p is StationEntry s && s.Local.ToString() == localName);
}
