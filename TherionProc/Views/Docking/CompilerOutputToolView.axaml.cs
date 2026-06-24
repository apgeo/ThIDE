using System;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class CompilerOutputToolView : UserControl
{
    private BuildViewModel? _build;

    public CompilerOutputToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_build is not null)
        {
            _build.OutputRowAdded -= OnOutputRowAdded;
            _build.OutputCleared -= OnOutputCleared;
        }
        _build = (DataContext as CompilerOutputToolViewModel)?.Build;
        if (_build is not null)
        {
            _build.OutputRowAdded += OnOutputRowAdded;
            _build.OutputCleared += OnOutputCleared;
            RebuildRaw(); // reflect any output produced before this view was attached
        }
    }

    // ---- raw colored view --------------------------------------------------

    private void OnOutputCleared(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => this.FindControl<SelectableTextBlock>("RawText")?.Inlines?.Clear());

    private void OnOutputRowAdded(object? sender, CompilerOutputRow row) =>
        Dispatcher.UIThread.Post(() => AppendRaw(row));

    private void AppendRaw(CompilerOutputRow row)
    {
        if (this.FindControl<SelectableTextBlock>("RawText") is not { } rt || rt.Inlines is null) return;
        rt.Inlines.Add(new Run(row.Text) { Foreground = row.TextBrush });
        rt.Inlines.Add(new LineBreak());
    }

    private void RebuildRaw()
    {
        if (_build is null || this.FindControl<SelectableTextBlock>("RawText") is not { } rt || rt.Inlines is null) return;
        rt.Inlines.Clear();
        foreach (var row in _build.Output) AppendRaw(row);
    }

    // ---- parsed-grid context menu ------------------------------------------

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CompilerOutputToolViewModel vm && vm.Build.SelectedOutput is { } row)
            vm.Build.NavigateOutputCommand.Execute(row);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) =>
        this.FindControl<DataGrid>("Grid")?.SelectAll();

    // Copy just the current line (the selected row).
    private void OnCopyLine(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CompilerOutputToolViewModel vm && vm.Build.SelectedOutput is { } row)
            Copy(row.Text);
    }

    private void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CompilerOutputToolViewModel vm)
            Copy(vm.Build.OutputAsText());
    }

    // ---- raw view context menu ---------------------------------------------

    private void OnRawSelectAll(object? sender, RoutedEventArgs e) =>
        this.FindControl<SelectableTextBlock>("RawText")?.SelectAll();

    private void OnRawCopy(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<SelectableTextBlock>("RawText")?.SelectedText is { Length: > 0 } sel)
            Copy(sel);
    }

    private void OnRawCopyAll(object? sender, RoutedEventArgs e) => Copy(_build?.RawOutput ?? string.Empty);

    private void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var top = TopLevel.GetTopLevel(this);
        _ = top?.Clipboard?.SetTextAsync(text);
    }
}
