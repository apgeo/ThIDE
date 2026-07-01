using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class GeneratedFilesToolView : UserControl
{
    private GeneratedFilesToolViewModel? _vm;

    public GeneratedFilesToolView()
    {
        InitializeComponent();
        // Path is hidden by default (#4); apply once the columns exist.
        AttachedToVisualTree += (_, _) => SetColumnVisible("Path", false);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.FlashRequested -= OnFlashRequested;
        _vm = DataContext as GeneratedFilesToolViewModel;
        if (_vm is not null) _vm.FlashRequested += OnFlashRequested;
    }

    // #3: a quick amber flash over the panel when it's surfaced from the status artifact link.
    private void OnFlashRequested(object? sender, EventArgs e)
    {
        if (this.FindControl<Border>("FlashOverlay") is not { } overlay) return;
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(900),
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d),   Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(0.3d), Setters = { new Setter(OpacityProperty, 0.65d) } },
                new KeyFrame { Cue = new Cue(1d),   Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        _ = anim.RunAsync(overlay);
    }

    private void OnArtifactDoubleTapped(object? sender, RoutedEventArgs e) => OpenSelected();
    private void OnOpenArtifact(object? sender, RoutedEventArgs e) => OpenSelected();

    // VIS-01: open the selected .lox/.3d artifact in the embedded 3D viewer.
    private void OnViewArtifactIn3D(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && vm.Build.SelectedArtifact is { } row)
            vm.Build.ViewIn3DCommand.Execute(row);
    }

    // #3: only offer "View in internal 3D viewer" for .lox / .3d artifacts.
    private void OnArtifactContextOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (this.FindControl<MenuItem>("View3DMenuItem") is { } item)
            item.IsVisible = DataContext is GeneratedFilesToolViewModel vm
                             && vm.Build.SelectedArtifact?.CanView3D == true;
    }

    // ---- #1/#8: per-row action buttons (operate on the row, not the grid selection) ----

    private static ArtifactRow? RowOf(object? sender) => (sender as Control)?.DataContext as ArtifactRow;

    private void OnRowOpenExternal3D(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && RowOf(sender) is { } row)
            vm.Build.OpenInExternalViewerCommand.Execute(row);
    }

    private void OnRowOpenInternal3D(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && RowOf(sender) is { } row)
            vm.Build.ViewIn3DCommand.Execute(row);
    }

    private void OnRowReveal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && RowOf(sender) is { } row)
            vm.Build.RevealArtifactCommand.Execute(row);
    }

    private void OnRowGoToDef(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && RowOf(sender) is { } row)
            vm.Build.GoToArtifactDefinitionCommand.Execute(row);
    }

    // #7: persist the per-file auto-open override when the 3-state checkbox is toggled.
    private void OnAutoOpenToggled(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && sender is CheckBox { DataContext: ArtifactRow row } cb)
            vm.Build.SetAutoOpenOverride(row.Path, cb.IsChecked);
    }

    private void OnRevealArtifact(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && vm.Build.SelectedArtifact is { } row)
            vm.Build.RevealArtifactCommand.Execute(row);
    }

    // Jump to where this output is defined in the active thconfig (#7).
    private void OnGoToArtifactDefinition(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && vm.Build.SelectedArtifact is { } row)
            vm.Build.GoToArtifactDefinitionCommand.Execute(row);
    }

    private void OpenSelected()
    {
        if (DataContext is GeneratedFilesToolViewModel vm && vm.Build.SelectedArtifact is { } row)
            vm.Build.OpenArtifactCommand.Execute(row);
    }

    // ---- column visibility + fit (#4) ----

    private void OnToggleColumn(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string header } cb)
            SetColumnVisible(header, cb.IsChecked == true);
    }

    // Stable, language-independent column keys in XAML column order — headers are localized via
    // {l:Loc}, so we identify a column by its key (the CheckBox Tag), not the translated header.
    private static readonly string[] ColOrder =
        { "Kind", "File Name", "Auto-open", "Actions", "State", "Path", "Size", "Modified" };

    private void SetColumnVisible(string key, bool visible)
    {
        if (this.FindControl<DataGrid>("Grid") is not { } grid) return;
        int idx = System.Array.IndexOf(ColOrder, key);
        if (idx >= 0 && idx < grid.Columns.Count) grid.Columns[idx].IsVisible = visible;
    }

    private void OnFitColumns(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<DataGrid>("Grid") is not { } grid) return;
        foreach (var col in grid.Columns)
            col.Width = DataGridLength.Auto;
    }
}
