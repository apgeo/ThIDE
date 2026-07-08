// code-behind for the popped-out 3D Plot panel. Mirrors the plot-hosting half of
// StructuralGeologyToolView.axaml.cs (EnsurePlot / bridge wiring / image export), but the
// NativeWebView is created once on attach instead of on a tab-changed event, since this panel has
// no tabs. Wraps the same StructuralGeologyViewModel, so plane data / disc size / selection are
// already live-synced with the Resulted Planes tab — nothing further to wire for that.

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class StructuralPlotToolView : UserControl
{
    private NativeWebView? _plot;
    private bool _plotWired;
    private bool _inited;

    public StructuralPlotToolView() => InitializeComponent();

    private StructuralGeologyViewModel? Vm => (DataContext as StructuralPlotToolViewModel)?.Structural;

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

    private void TryInit()
    {
        if (_inited || Vm is null) return;
        _inited = true;
        Vm.PlotImageReady += OnPlotImageReady;
        EnsurePlot();
        KickResize();
    }

    private void OnFitView(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { _ = _plot?.InvokeScript("stFit()"); } catch { /* best-effort */ }
    }

    private void OnExportImage(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) =>
        Vm?.ExportPlotImageCommand.Execute(null);

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
