// code-behind for the Structural Geology dock tool. View glue:
//   • double-click a measurement / plane row → navigate to its source span;
//   • measurements grid grouped by station (DataGridCollectionView) with a per-station header checkbox;
//   • column show/hide + fit, persisted via the VM;
//   • the 3D-plot NativeWebView is created lazily on first show and bridged to the VM (incl. image export).

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class StructuralGeologyToolView : UserControl
{
    private const int PlotTabIndex = 3;
    private NativeWebView? _plot;
    private bool _plotWired;

    private StructuralGeologyViewModel? _vm;
    private bool _inited;
    private DataGridCollectionView? _measView;

    // Stable, language-independent column keys in the exact XAML column order. Headers are localized
    // via {l:Loc}, so we can no longer identify a column by its (translated) header text — these
    // keys drive column show/hide, persistence and Copy-Value instead, and stay the persisted keys.
    private static readonly string[] MeasColOrder =
        { "Use", "Plane", "Kind", "From", "To", "Length", "Azimuth", "Clino", "Comment", "File", "Line" };
    private static readonly string[] PlaneColOrder =
        { "Visible", "Plane", "Type", "Dip °", "Strike °", "Dip dir °", "North ref", "Points", "RMS", "File", "Line" };
    private readonly System.Collections.Generic.Dictionary<DataGridColumn, string> _measColKey = new();
    private readonly System.Collections.Generic.Dictionary<DataGridColumn, string> _planeColKey = new();

    public StructuralGeologyToolView() => InitializeComponent();

    private static void BuildKeyMap(DataGrid? grid, string[] order,
        System.Collections.Generic.Dictionary<DataGridColumn, string> map)
    {
        map.Clear();
        if (grid is null) return;
        for (int i = 0; i < grid.Columns.Count && i < order.Length; i++)
            map[grid.Columns[i]] = order[i];
    }

    private StructuralGeologyViewModel? Vm => (DataContext as StructuralGeologyToolViewModel)?.Structural;

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryInit();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TryInit();
    }

    // Wire grouping + column visibility + the export callback exactly once, when the VM is available.
    private void TryInit()
    {
        if (_inited || Vm is null) return;
        _vm = Vm;
        _inited = true;

        SetupGrouping();
        BuildKeyMap(MeasurementsGrid, MeasColOrder, _measColKey);
        BuildKeyMap(PlanesGrid, PlaneColOrder, _planeColKey);
        ApplyColumns(MeasurementsGrid, _vm.MeasurementColumns, MeasColumnsPanel, _measColKey);
        ApplyColumns(PlanesGrid, _vm.PlaneColumns, PlaneColumnsPanel, _planeColKey);

        _vm.GroupingChanged += (_, _) => SetupGrouping();
        _vm.PlotImageReady += OnPlotImageReady;
        // Once the plot pops out into its own panel, this tab's WebView must go away — otherwise
        // two NativeWebViews would both be wired to the same PlotScriptRequested bridge.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StructuralGeologyViewModel.PlotPoppedOut) && _vm.PlotPoppedOut)
                TeardownPlot();
        };
    }

    // ---- measurements grouping (#10) -----------------------------------------------------------

    private void SetupGrouping()
    {
        if (_vm is null || MeasurementsGrid is null) return;
        _measView ??= new DataGridCollectionView(_vm.Measurements);
        if (!ReferenceEquals(MeasurementsGrid.ItemsSource, _measView))
            MeasurementsGrid.ItemsSource = _measView;

        _measView.GroupDescriptions.Clear();
        if (_vm.GroupByStation)
            _measView.GroupDescriptions.Add(new DataGridPathGroupDescription("PlaneRow"));
    }

    // ---- navigation ----------------------------------------------------------------------------

    private void OnMeasurementDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: StructuralMeasurementRow row })
            row.NavigateToSource();
    }

    private void OnPlaneDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: StructuralPlaneRow plane })
            Vm?.Navigate(plane.Span);
    }

    // ---- resulted-planes context menu: highlight in preview / go to source ---------------------

    private void OnHighlightPlaneInPreview(object? sender, RoutedEventArgs e)
    {
        if (PlanesGrid?.SelectedItem is StructuralPlaneRow plane) Vm?.HighlightInPreview(plane);
    }

    private void OnGoToPlaneSource(object? sender, RoutedEventArgs e)
    {
        if (PlanesGrid?.SelectedItem is StructuralPlaneRow plane) Vm?.Navigate(plane.Span);
    }

    // ---- column visibility + fit (#2) ----------------------------------------------------------

    private static void ApplyColumns(DataGrid? grid, System.Collections.Generic.Dictionary<string, bool> vis, Panel? panel,
        System.Collections.Generic.Dictionary<DataGridColumn, string> keyMap)
    {
        if (grid is null) return;
        foreach (var c in grid.Columns)
            if (keyMap.TryGetValue(c, out var h))
                c.IsVisible = !vis.TryGetValue(h, out var v) || v;     // default: visible
        if (panel is null) return;
        foreach (var cb in panel.Children.OfType<CheckBox>())
            if (cb.Tag is string h)
                cb.IsChecked = !vis.TryGetValue(h, out var v) || v;
    }

    private void OnToggleMeasColumn(object? sender, RoutedEventArgs e) => ToggleColumn(sender, MeasurementsGrid, _vm?.MeasurementColumns, _measColKey);
    private void OnTogglePlaneColumn(object? sender, RoutedEventArgs e) => ToggleColumn(sender, PlanesGrid, _vm?.PlaneColumns, _planeColKey);

    private void ToggleColumn(object? sender, DataGrid? grid, System.Collections.Generic.Dictionary<string, bool>? dict,
        System.Collections.Generic.Dictionary<DataGridColumn, string> keyMap)
    {
        if (sender is not CheckBox { Tag: string header } cb || grid is null) return;
        bool visible = cb.IsChecked == true;
        var col = keyMap.FirstOrDefault(kv => string.Equals(kv.Value, header, StringComparison.Ordinal)).Key;
        if (col is not null) col.IsVisible = visible;
        if (dict is not null) { dict[header] = visible; _vm?.Persist(); }
    }

    private void OnFitMeasColumns(object? sender, RoutedEventArgs e) => FitColumns(MeasurementsGrid);
    private void OnFitPlaneColumns(object? sender, RoutedEventArgs e) => FitColumns(PlanesGrid);

    private static void FitColumns(DataGrid? grid)
    {
        if (grid is null) return;
        foreach (var col in grid.Columns) col.Width = DataGridLength.Auto;
    }

    // ---- clipboard: copy value / row / formatted data (both grids) ------------------------------

    // Fixed column order for row / formatted / CSV output (kept in sync with the VM's export tables).
    private static readonly string[] MeasHeaders =
        { "Plane", "Kind", "From", "To", "Length", "Azimuth", "Clino", "Include", "Comment", "File", "Line" };
    private static string[] MeasValues(StructuralMeasurementRow r) => new[]
        { r.Plane, r.Kind, r.From, r.To, r.Length, r.Compass, r.Clino, r.Include ? "yes" : "no", r.Comment, r.File, r.Line.ToString() };

    private static readonly string[] PlaneHeaders =
        { "Plane", "Type", "Dip °", "Strike °", "Dip dir °", "North ref", "Points", "RMS", "Visible", "File", "Line" };
    private static string[] PlaneValues(StructuralPlaneRow p) => new[]
        { p.Name, p.Type, p.Dip, p.Strike, p.DipDirection, p.Declination, p.Points, p.Quality, p.Visible ? "yes" : "no", p.File, p.Line.ToString() };

    private void OnCopyMeasValue(object? sender, RoutedEventArgs e)
    {
        if (MeasurementsGrid?.SelectedItem is StructuralMeasurementRow r && MeasCell(r, MeasurementsGrid.CurrentColumn) is { } v)
            CopyToClipboard(v);
    }

    private void OnCopyMeasRow(object? sender, RoutedEventArgs e)
    {
        var rows = SelectedRows<StructuralMeasurementRow>(MeasurementsGrid);
        if (rows.Count > 0) CopyToClipboard(string.Join("\n", rows.Select(r => string.Join("\t", MeasValues(r)))));
    }

    private void OnCopyMeasFormatted(object? sender, RoutedEventArgs e)
    {
        var rows = SelectedRows<StructuralMeasurementRow>(MeasurementsGrid);
        if (rows.Count > 0) CopyToClipboard(FormatRows(MeasHeaders, rows.Select(MeasValues)));
    }

    private void OnCopyPlaneValue(object? sender, RoutedEventArgs e)
    {
        if (PlanesGrid?.SelectedItem is StructuralPlaneRow p && PlaneCell(p, PlanesGrid.CurrentColumn) is { } v)
            CopyToClipboard(v);
    }

    private void OnCopyPlaneRow(object? sender, RoutedEventArgs e)
    {
        var rows = SelectedRows<StructuralPlaneRow>(PlanesGrid);
        if (rows.Count > 0) CopyToClipboard(string.Join("\n", rows.Select(p => string.Join("\t", PlaneValues(p)))));
    }

    private void OnCopyPlaneFormatted(object? sender, RoutedEventArgs e)
    {
        var rows = SelectedRows<StructuralPlaneRow>(PlanesGrid);
        if (rows.Count > 0) CopyToClipboard(FormatRows(PlaneHeaders, rows.Select(PlaneValues)));
    }

    // Selected rows (multi-select aware), falling back to the single SelectedItem.
    private static System.Collections.Generic.List<T> SelectedRows<T>(DataGrid? grid) where T : class
    {
        var list = new System.Collections.Generic.List<T>();
        if (grid?.SelectedItems is { } sel)
            foreach (var o in sel) if (o is T t) list.Add(t);
        if (list.Count == 0 && grid?.SelectedItem is T single) list.Add(single);
        return list;
    }

    // "Field: value" blocks, one row per block, blank line between rows.
    private static string FormatRows(string[] headers, System.Collections.Generic.IEnumerable<string[]> rows)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var values in rows)
        {
            if (sb.Length > 0) sb.AppendLine();
            for (int i = 0; i < headers.Length && i < values.Length; i++)
                sb.Append(headers[i]).Append(": ").AppendLine(values[i]);
        }
        return sb.ToString().TrimEnd();
    }

    private string? MeasCell(StructuralMeasurementRow r, DataGridColumn? col) =>
        (col is not null && _measColKey.TryGetValue(col, out var key) ? key : null) switch
    {
        "Use"     => r.Include ? "yes" : "no",
        "Plane"   => r.Plane,
        "Kind"    => r.Kind,
        "From"    => r.From,
        "To"      => r.To,
        "Length"  => r.Length,
        "Azimuth" => r.Compass,
        "Clino"   => r.Clino,
        "Comment" => r.Comment,
        "File"    => r.File,
        "Line"    => r.Line.ToString(),
        _         => null,
    };

    private string? PlaneCell(StructuralPlaneRow p, DataGridColumn? col) =>
        (col is not null && _planeColKey.TryGetValue(col, out var key) ? key : null) switch
    {
        "Visible"   => p.Visible ? "yes" : "no",
        "Plane"     => p.Name,
        "Type"      => p.Type,
        "Dip °"     => p.Dip,
        "Strike °"  => p.Strike,
        "Dip dir °" => p.DipDirection,
        "North ref" => p.Declination,
        "Points"    => p.Points,
        "RMS"       => p.Quality,
        "File"      => p.File,
        "Line"      => p.Line.ToString(),
        _           => null,
    };

    private void CopyToClipboard(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }

    // ---- CSV export (both grids) ---------------------------------------------------------------

    private void OnExportMeasCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var (headers, rows) = vm.MeasurementsTable();
        _ = ExportCsvAsync("structural-measurements.csv", DataExport.ToCsv(headers, rows));
    }

    private void OnExportPlanesCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var (headers, rows) = vm.PlanesTable();
        _ = ExportCsvAsync("structural-planes.csv", DataExport.ToCsv(headers, rows));
    }

    private async Task ExportCsvAsync(string suggestedName, string csv)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = ThIDE.Resources.Tr.Get("Pick_ExportCsv"),
                SuggestedFileName = suggestedName,
                DefaultExtension = "csv",
                FileTypeChoices = new[] { new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_CsvFile")) { Patterns = new[] { "*.csv" } } },
            });
            if (file is null) return;
            if (file.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, csv);
            else { await using var s = await file.OpenWriteAsync(); await using var w = new StreamWriter(s); await w.WriteAsync(csv); }
        }
        catch { /* best-effort export */ }
    }

    // ---- 3D plot (#4 background / #5 export) ---------------------------------------------------

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Wizard?.SelectedIndex == PlotTabIndex)
        {
            EnsurePlot();
            KickResize();
        }
    }

    private void OnFitView(object? sender, RoutedEventArgs e)
    {
        try { _ = _plot?.InvokeScript("stFit()"); } catch { /* best-effort */ }
    }

    private void OnExportImage(object? sender, RoutedEventArgs e) => Vm?.ExportPlotImageCommand.Execute(null);

    private async void OnPlotImageReady(object? sender, string dataUrl)
    {
        try
        {
            int comma = dataUrl.IndexOf(',');
            if (comma < 0) return;
            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = ThIDE.Resources.Tr.Get("Pick_ExportImage"),
                SuggestedFileName = "structural-plot.png",
                DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_PngImage")) { Patterns = new[] { "*.png" } } },
            });
            if (file is null) return;
            if (file.TryGetLocalPath() is { } path) await File.WriteAllBytesAsync(path, bytes);
            else { await using var s = await file.OpenWriteAsync(); await s.WriteAsync(bytes); }
        }
        catch { /* best-effort export */ }
    }

    // Create the plot web control + wire the bridge once, the first time the plot tab is shown.
    private void EnsurePlot()
    {
        if (_plot is not null || Vm is null || PlotHost is null) return;
        if (!Vm.IsPlotAvailable) { ShowFallback(); return; }
        // Missing native engine (e.g. no webkit2gtk on Linux) fails asynchronously, not in the
        // ctor — probe first so the fallback shows instead of a dead empty box.
        if (WebViewSupport.DescribeMissingEngine() is not null) { ShowFallback(); return; }

        try
        {
            _plot = new NativeWebView();
            WebViewSupport.ConfigureWebView(_plot); // before attach — EnvironmentRequested fires then
            PlotHost.Children.Add(_plot);
            _plot.WebMessageReceived += OnPlotMessage;
            if (!_plotWired) { Vm.PlotScriptRequested += OnPlotScript; _plotWired = true; }

            var url = Vm.EnsurePlotStarted();
            if (url is not null) _plot.Source = new Uri(url);
            else ShowFallback();
        }
        catch
        {
            ShowFallback();
        }
    }

    private void ShowFallback()
    {
        if (PlotFallback is not null) PlotFallback.IsVisible = true;
    }

    // Tears down this tab's WebView + bridge subscriptions once the plot has popped out into its
    // own panel (StructuralPlotToolView), so exactly one WebView is ever wired to the VM at a time.
    private void TeardownPlot()
    {
        if (_plot is null) return;
        if (_vm is not null) _vm.PlotScriptRequested -= OnPlotScript;
        _plotWired = false;
        _plot.WebMessageReceived -= OnPlotMessage;
        PlotHost?.Children.Clear();
        _plot = null;
    }

    private void OnPlotMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        var body = e.Body;
        Dispatcher.UIThread.Post(() => Vm?.OnPlotMessage(body));
    }

    private async void OnPlotScript(object? sender, string js)
    {
        try { if (_plot is not null) await _plot.InvokeScript(js); }
        catch { /* best-effort C#→JS */ }
    }

    private void KickResize()
    {
        if (_plot is null) return;
        Dispatcher.UIThread.Post(async () =>
        {
            foreach (var ms in new[] { 80, 250, 600 })
            {
                await Task.Delay(ms);
                try { if (_plot is not null) await _plot.InvokeScript("stResize()"); } catch { /* best-effort */ }
            }
        });
    }
}
