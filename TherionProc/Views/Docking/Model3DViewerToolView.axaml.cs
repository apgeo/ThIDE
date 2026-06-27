// VIS-01 — host view for the embedded 3D model viewer. Owns the NativeWebView control
// (created imperatively so the VM stays unit-testable and the control can be wired up
// lazily, only when the feature-flagged tool is actually shown). Bridges the control to
// Model3DViewerViewModel: WebMessageReceived → OnWebMessage (JS→C#), ScriptRequested →
// InvokeScript (C#→JS), and the asset-host URL → the control's Source.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class Model3DViewerToolView : UserControl
{
    private NativeWebView? _web;
    private Model3DViewerViewModel? _vm;

    public Model3DViewerToolView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryInitialize();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TryInitialize();
    }

    // Create + wire the web control exactly once (the singleton VM persists across tab switches).
    private void TryInitialize()
    {
        if (_web is not null) return;
        if (DataContext is not Model3DViewerToolViewModel tool || tool.Viewer is null) return;
        _vm = tool.Viewer;

        // Assets missing → leave the fallback panel visible; don't instantiate the engine.
        if (!_vm.IsAvailable) { _vm.EnsureStarted(); return; }

        try
        {
            _web = new NativeWebView();
            WebHost.Children.Add(_web);
            _web.WebMessageReceived += OnWebMessage;
            _vm.ScriptRequested += OnScriptRequested;

            var url = _vm.EnsureStarted();
            if (url is not null) _web.Source = new Uri(url);
            else _vm.SetEngineUnavailable(null);
        }
        catch (Exception ex)
        {
            // No native web engine (e.g. missing WebView2 runtime / WebKitGTK) → graceful fallback.
            _vm.SetEngineUnavailable("The system web engine (WebView2 / WebKit) could not be initialised: " + ex.Message);
        }
    }

    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        var body = e.Body;
        Dispatcher.UIThread.Post(() => _vm?.OnWebMessage(body));
    }

    private async void OnScriptRequested(object? sender, string js)
    {
        try { if (_web is not null) await _web.InvokeScript(js); }
        catch { /* best-effort C#→JS call */ }
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || _vm is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open 3D model",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("3D models (.lox / .3d)")
                {
                    Patterns = new[] { "*.lox", "*.3d" },
                },
            },
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) _vm.LoadModel(path);
    }
}
