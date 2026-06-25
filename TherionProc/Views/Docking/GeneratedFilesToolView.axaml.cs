using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class GeneratedFilesToolView : UserControl
{
    public GeneratedFilesToolView()
    {
        InitializeComponent();
        // Path is hidden by default (#4); apply once the columns exist.
        AttachedToVisualTree += (_, _) => SetColumnVisible("Path", false);
    }

    private void OnArtifactDoubleTapped(object? sender, RoutedEventArgs e) => OpenSelected();
    private void OnOpenArtifact(object? sender, RoutedEventArgs e) => OpenSelected();

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

    private void SetColumnVisible(string header, bool visible)
    {
        var col = this.FindControl<DataGrid>("Grid")?.Columns
            .FirstOrDefault(c => string.Equals(c.Header?.ToString(), header, System.StringComparison.Ordinal));
        if (col is not null) col.IsVisible = visible;
    }

    private void OnFitColumns(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<DataGrid>("Grid") is not { } grid) return;
        foreach (var col in grid.Columns)
            col.Width = DataGridLength.Auto;
    }
}
