// host view for the embedded 3D model viewer. Owns the NativeWebView control
// (created imperatively so the VM stays unit-testable and the control can be wired up
// lazily, only when the feature-flagged tool is actually shown). Bridges the control to
// Model3DViewerViewModel: WebMessageReceived → OnWebMessage (JS→C#), ScriptRequested →
// InvokeScript (C#→JS), and the asset-host URL → the control's Source.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class Model3DViewerToolView : UserControl
{
    private NativeWebView? _web;
    private Model3DViewerViewModel? _vm;
    private bool _vmWired;       // guards against double-subscribing the singleton VM's events
    private bool _reFitQueued;   // coalesces bursts of size changes into a single canvas re-fit
    private Window? _fsWindow;   // borderless full-screen host for the whole panel

    public Model3DViewerToolView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryInitialize();
    }

    // Dock recreates/re-parents tool views while the ViewModel stays a singleton. If every view
    // instance left its handlers on the VM they'd accumulate — one click firing on all past views
    // (e.g. three DevTools windows) and those views never getting GC'd. Drop this view's
    // subscriptions when it leaves the tree; OnAttachedToVisualTree re-wires the current one.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnwireVm();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TryInitialize();
    }

    // Create the web control once per view instance and keep exactly one set of VM subscriptions
    // (the singleton VM persists across tab switches; see OnDetachedFromVisualTree).
    private void TryInitialize()
    {
        if (DataContext is not Model3DViewerToolViewModel tool || tool.Viewer is null) return;
        _vm = tool.Viewer;

        // Wire the VM events before the availability gate: full-screen (and the DevTools hint) work
        // even when the engine/assets are missing. WireVm is idempotent, so the two entry points
        // (attach + data-context) don't double-subscribe.
        WireVm();

        if (_web is not null) return;

        // Assets missing → leave the fallback panel visible; don't instantiate the engine.
        if (!_vm.IsAvailable) { _vm.EnsureStarted(); return; }

        // Missing native engine (e.g. no webkit2gtk on Linux) never throws from the ctor — the
        // adapter initialises asynchronously and just leaves a dead empty box — so probe first.
        if (WebViewSupport.DescribeMissingEngine() is { } missingEngine)
        {
            _vm.SetEngineUnavailable(missingEngine);
            return;
        }

        try
        {
            _web = new NativeWebView();
            // JS console output + uncaught errors are forwarded to the in-app Log panel by viewer.html
            // (the {type:"console"} bridge message). The engine's own inspector is opened through
            // WebViewSupport (WebView2 DevTools window / WebKit inspector); the hook below applies
            // the Debug switches (DevTools, offscreen) and must land before the control attaches.
            WebViewSupport.ConfigureWebView(_web);
            WebHost.Children.Add(_web);
            // Keep the native control (and thus CaveView's WebGL canvas) glued to the panel size. The
            // native child doesn't always follow an Avalonia-side layout change on its own; when it lags,
            // the page stays at its original size and the cave no longer fills the pane — the same failure
            // that made growing the panel and going full screen leave it small (#5). Pin the control to
            // the host bounds on every size change and re-fit the canvas once the new size settles.
            WebHost.SizeChanged += OnWebHostSizeChanged;
            if (WebHost.Bounds is { Width: > 0, Height: > 0 } b0)
            {
                _web.Width = b0.Width;
                _web.Height = b0.Height;
            }
            _web.WebMessageReceived += OnWebMessage;

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

    // Subscribe this view to the singleton VM's C#→JS / full-screen / DevTools events, exactly once.
    // Idempotent so the attach + data-context entry points can both call it.
    private void WireVm()
    {
        if (_vm is null || _vmWired) return;
        _vm.FullscreenRequested += OnFullscreenRequested;
        _vm.ScriptRequested += OnScriptRequested;
        _vm.DevToolsRequested += OnDevToolsRequested;
        _vmWired = true;
    }

    private void UnwireVm()
    {
        if (_vm is null || !_vmWired) return;
        _vm.FullscreenRequested -= OnFullscreenRequested;
        _vm.ScriptRequested -= OnScriptRequested;
        _vm.DevToolsRequested -= OnDevToolsRequested;
        _vmWired = false;
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
            Title = ThIDE.Resources.Tr.Get("Pick_Open3D"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_3DFilter"))
                {
                    Patterns = new[] { "*.lox", "*.3d" },
                },
            },
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) _vm.LoadModel(path);
    }

    // ---- URL test: navigate the embedded engine to an arbitrary page ----

    // Testing aid (e.g. for the Linux web-view workarounds): navigate the web control to any
    // http(s) page to check the engine renders at all. The dialog pre-fills the viewer's own
    // address, so pressing 🌐 again and OK returns to the 3D viewer (the VM re-sends the model
    // on the page's next 'ready' message).
    private async void OnOpenUrl(object? sender, RoutedEventArgs e)
    {
        if (_web is null || _vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var home = _vm.EnsureStarted();
        var input = await new InputDialog(
            ThIDE.Resources.Tr.Get("M3D_OpenUrlTitle"),
            ThIDE.Resources.Tr.Get("M3D_OpenUrlPrompt"),
            home ?? "https://").ShowAsync(owner);
        if (string.IsNullOrWhiteSpace(input) || _web is null || _vm is null) return;

        var url = input.Trim();
        if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _vm.Status = ThIDE.Resources.Tr.Get("M3D_OpenUrlInvalid");
            return;
        }

        _vm.PrepareViewerReload();
        _web.Source = uri;
        var goingHome = home is not null && string.Equals(url, home, StringComparison.OrdinalIgnoreCase);
        if (!goingHome) _vm.Status = ThIDE.Resources.Tr.Get("M3D_OpenUrlStatus");
    }

    // ---- full screen: reparent the whole panel into a borderless full-screen window ----

    private void OnFullscreenRequested(object? sender, bool on)
        => Dispatcher.UIThread.Post(() => SetFullscreen(on));

    private void SetFullscreen(bool on)
    {
        if (RootContent is null || OuterHost is null) return;
        if (on)
        {
            if (_fsWindow is not null) return;
            OuterHost.Children.Remove(RootContent);
            _fsWindow = new Window
            {
                WindowState = WindowState.FullScreen,   // FullScreen hides the window chrome
                Background = Brushes.Black,
                ShowInTaskbar = false,
                Content = RootContent,
            };
            _fsWindow.KeyDown += OnFullscreenKeyDown;
            _fsWindow.Closed += OnFullscreenWindowClosed;
            if (TopLevel.GetTopLevel(this) is Window owner) _fsWindow.Show(owner);
            else _fsWindow.Show();
        }
        else
        {
            RestoreFromFullscreen();
        }
        // The native control's new size isn't applied synchronously after a reparent, so CaveView's
        // canvas can keep the old size — nudge it a few times as the new layout settles (#2).
        ScheduleResizeKicks();
    }

    // The panel (or the full-screen window) changed size: pin the native control to the new bounds so
    // it actually resizes, then re-fit the CaveView canvas to it (#5).
    private void OnWebHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_web is null) return;
        _web.Width = e.NewSize.Width;
        _web.Height = e.NewSize.Height;
        ScheduleReFit();
    }

    // Re-fit the WebGL canvas once the current burst of layout changes has settled. Coalesced so a
    // drag of the dock splitter doesn't fire a script call on every intermediate size.
    private void ScheduleReFit()
    {
        if (_web is null || _reFitQueued) return;
        _reFitQueued = true;
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);
            _reFitQueued = false;
            try { if (_web is not null) await _web.InvokeScript("cvResize()"); } catch { /* best-effort */ }
        });
    }

    // Re-fit the WebGL canvas after the panel has been reparented (full screen in/out).
    private void ScheduleResizeKicks()
    {
        if (_web is null) return;
        Dispatcher.UIThread.Post(async () =>
        {
            foreach (var ms in new[] { 60, 200, 500, 900 })
            {
                await Task.Delay(ms);
                try { if (_web is not null) await _web.InvokeScript("cvResize()"); } catch { /* best-effort */ }
            }
        });
    }

    private void OnFullscreenKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _vm?.ToggleFullscreenCommand.Execute(null); e.Handled = true; }
    }

    // OS-initiated close (Alt+F4) while still full-screen: salvage the panel back into the dock.
    private void OnFullscreenWindowClosed(object? sender, EventArgs e)
    {
        if (_fsWindow is null) return;        // a normal restore already handled it
        _fsWindow = null;
        if (sender is Window w) w.Content = null;
        ReturnRootContent();
        if (_vm is { IsFullscreen: true }) _vm.ToggleFullscreenCommand.Execute(null);
    }

    private void RestoreFromFullscreen()
    {
        if (_fsWindow is null) return;
        var w = _fsWindow;
        _fsWindow = null;                     // clear first so Closed treats this as a normal restore
        w.KeyDown -= OnFullscreenKeyDown;
        w.Content = null;                     // detach before reparenting
        ReturnRootContent();
        try { w.Close(); } catch { /* best-effort */ }
    }

    private void ReturnRootContent()
    {
        if (RootContent is not null && OuterHost is not null && !OuterHost.Children.Contains(RootContent))
            OuterHost.Children.Add(RootContent);
    }

    // ---- #5: "show DevTools" for the underlying web engine ----

    private void OnDevToolsRequested(object? sender, EventArgs e)
    {
        // WebView2 DevTools window on Windows, WebKit inspector on Linux; not-yet-initialised
        // or an engine without a programmatic entry point → the Inspect / F12 hint.
        if (_web is not null && WebViewSupport.TryOpenDevTools(_web)) return;
        if (_vm is not null)
            _vm.Status = ThIDE.Resources.Tr.Get("Viewer3D_DevToolsHint");
    }
}
