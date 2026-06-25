using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class DiagnosticsToolView : UserControl
{
    public DiagnosticsToolView()
    {
        InitializeComponent();
        // The full Path column is hidden by default (#3); apply once the columns exist.
        AttachedToVisualTree += (_, _) => SetColumnVisible("Path", false);
    }

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsToolViewModel vm && vm.Diagnostics.Selected is { } row)
            vm.Diagnostics.NavigateCommand.Execute(row);
    }

    // ---- copy (#3) ----

    private void OnCopyRow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsToolViewModel vm && vm.Diagnostics.Selected is { } row)
            SetClipboard(row.ToClipboardText());
    }

    private void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsToolViewModel vm)
            SetClipboard(vm.Diagnostics.AllRowsAsText());
    }

    private void SetClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) _ = clipboard.SetTextAsync(text);
    }

    // ---- column visibility + fit (#3) ----

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
