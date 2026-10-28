using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Semantics;
using ThIDE.Resources;
using ThIDE.ViewModels.Pickers;

namespace ThIDE.ViewModels;

/// <summary>What a <c>PreviewWindow</c> shows: a preview, and for a link the two pickers that drive it.</summary>
public sealed class PreviewWindowViewModel
{
    private Action? _detach;

    private PreviewWindowViewModel(LivePreviewViewModel preview, string title)
    {
        Preview = preview;
        Title = title;
    }

    public LivePreviewViewModel Preview { get; }
    public string Title { get; }
    public ItemPickerViewModel? LeftPicker { get; private init; }
    public ItemPickerViewModel? RightPicker { get; private init; }
    public bool HasPickers => LeftPicker is not null;

    /// <summary>Shows an existing preview (the embedded one, maximised); it keeps its own data.</summary>
    public static PreviewWindowViewModel ForPreview(LivePreviewViewModel preview, string title) => new(preview, title);

    /// <summary>
    /// The workspace plus the source survey, attached at the two chosen stations, with the source's
    /// legs emphasised and both stations ringed. Follows the dialog's pickers while open.
    /// </summary>
    public static PreviewWindowViewModel ForLink(LinkSurveyViewModel link)
    {
        var vm = new PreviewWindowViewModel(LivePreviewViewModel.CreateStandalone(), Tr.Get("LinkSurvey_FullPreviewTitle"))
        {
            LeftPicker = link.SourceSelector.Picker,
            RightPicker = link.Stations,
        };

        void Refresh(object? sender, PickerItem? item)
        {
            var (models, pair, sourceFile) = link.FullPreviewScene();
            vm.Preview.SetModels(models, pair is { } p ? [p] : null, sourceFile);
            var chosen = new List<QualifiedName>();
            if (link.SourceSelector.Picker.SelectedStation is { } a) chosen.Add(a.Local);
            if (link.Stations.SelectedStation is { } b) chosen.Add(b.Local);
            vm.Preview.SetHighlightedStations(chosen);
        }

        link.SourceSelector.Picker.SelectionChanged += Refresh;
        link.Stations.SelectionChanged += Refresh;
        vm._detach = () =>
        {
            link.SourceSelector.Picker.SelectionChanged -= Refresh;
            link.Stations.SelectionChanged -= Refresh;
        };
        Refresh(null, null);
        return vm;
    }

    /// <summary>Stops following the dialog (the window closed).</summary>
    public void Detach() => _detach?.Invoke();
}
