using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class CompilerOutputToolView : UserControl
{
    private BuildViewModel? _build;

    // Autoscroll-to-tail state (#3): each view follows new output until the user scrolls up,
    // and resumes once they scroll back to the bottom.
    private ScrollViewer? _gridScroll;
    private bool _gridFollow = true;
    private bool _rawFollow = true;

    public CompilerOutputToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => HookScrollViewers();
    }

    private void HookScrollViewers()
    {
        // The DataGrid's scroll viewer only exists once its template is applied.
        if (_gridScroll is null && this.FindControl<DataGrid>("Grid") is { } grid)
        {
            _gridScroll = grid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_gridScroll is not null) _gridScroll.ScrollChanged += OnGridScrollChanged;
        }
        if (this.FindControl<ScrollViewer>("RawScroll") is { } raw)
        {
            raw.ScrollChanged -= OnRawScrollChanged;
            raw.ScrollChanged += OnRawScrollChanged;
        }
    }

    private void OnGridScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Ignore changes caused by content growth (offset unchanged); only a real scroll —
        // user wheel/drag or our own ScrollIntoView — updates the follow flag.
        if (sender is ScrollViewer sv && e.OffsetDelta.Y != 0)
            _gridFollow = AtBottom(sv);
    }

    private void OnRawScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && e.OffsetDelta.Y != 0)
            _rawFollow = AtBottom(sv);
    }

    private static bool AtBottom(ScrollViewer sv) =>
        sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 2;

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
        Dispatcher.UIThread.Post(() =>
        {
            this.FindControl<SelectableTextBlock>("RawText")?.Inlines?.Clear();
            _gridFollow = true;   // a new build resumes following the tail (#3)
            _rawFollow = true;
        });

    private void OnOutputRowAdded(object? sender, CompilerOutputRow row) =>
        Dispatcher.UIThread.Post(() =>
        {
            AppendRaw(row);
            AutoScroll();
        });

    // Scroll both views to the latest line while following (#3).
    private void AutoScroll()
    {
        if (_gridFollow && _build is { Output.Count: > 0 } && this.FindControl<DataGrid>("Grid") is { } grid)
        {
            var last = _build.Output[^1];
            try { grid.ScrollIntoView(last, null); } catch { }
        }
        if (_rawFollow && this.FindControl<ScrollViewer>("RawScroll") is { } raw)
            raw.ScrollToEnd();
    }

    private void AppendRaw(CompilerOutputRow row)
    {
        if (this.FindControl<SelectableTextBlock>("RawText") is not { } rt || rt.Inlines is null) return;

        // Raw view is plain, fully-selectable text: each line is a single colored Run. We deliberately
        // do NOT embed clickable path links here — an InlineUIContainer link is skipped when the user
        // selects across it and copies, so it broke copying of raw output (#3). The parsed view keeps
        // its clickable links (its cells copy per-row, so the same problem doesn't apply there).
        rt.Inlines.Add(new Run(row.Text) { Foreground = row.TextBrush });
        rt.Inlines.Add(new LineBreak());
    }

    // Open the file detected in a compiler-output line and jump to its error line (#1).
    private void OnOutputPathPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: CompilerOutputRow row }) { NavigateOutput(row); e.Handled = true; }
    }

    private void NavigateOutput(CompilerOutputRow row) =>
        (DataContext as CompilerOutputToolViewModel)?.Build.NavigateOutputCommand.Execute(row);

    private void RebuildRaw()
    {
        if (_build is null || this.FindControl<SelectableTextBlock>("RawText") is not { } rt || rt.Inlines is null) return;
        rt.Inlines.Clear();
        foreach (var row in _build.Output) AppendRaw(row);
    }

    // ---- parsed-grid context menu ------------------------------------------

    // Open the detected Therion log in the editor (#3).
    private void OnOpenLog(object? sender, RoutedEventArgs e) =>
        (DataContext as CompilerOutputToolViewModel)?.Build.OpenLogCommand.Execute(null);

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
