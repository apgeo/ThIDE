using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class DiagnosticsToolView : UserControl
{
    public DiagnosticsToolView()
    {
        InitializeComponent();
        // The full Path column is hidden by default (#3); apply once the columns exist.
        AttachedToVisualTree += (_, _) => SetColumnVisible("Path", false);
        // Enter opens the selected diagnostic. Tunnel, because the grid itself handles Enter on the
        // way up (it moves to the next row).
        var grid = this.FindControl<DataGrid>("Grid");
        grid?.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
        // handledEventsToo: the grid marks the release handled once it has selected and focused a cell.
        grid?.AddHandler(PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // A double-click jumps on the button release, after the grid has focused the clicked cell, so the
    // editor keeps the keyboard instead of handing it straight back to the grid.
    private bool _navigateOnRelease;

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs e) => _navigateOnRelease = true;

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_navigateOnRelease) return;
        _navigateOnRelease = false;
        NavigateToSelected();
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        if (NavigateToSelected()) e.Handled = true;
    }

    private bool NavigateToSelected()
    {
        if (DataContext is not DiagnosticsToolViewModel vm || vm.Diagnostics.Selected is not { } row) return false;
        vm.Diagnostics.NavigateCommand.Execute(row);
        return true;
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

    // Stable, language-independent column keys in XAML column order — headers are localized via
    // {l:Loc}, so we identify a column by its key (the CheckBox Tag), not the translated header.
    private static readonly string[] ColOrder = { "Code", "Severity", "Message", "File", "Path", "Line" };

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
