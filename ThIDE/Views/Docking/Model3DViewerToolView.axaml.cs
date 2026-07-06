// host view for the embedded 3D model viewer. Owns the NativeWebView control
// (created imperatively so the VM stays unit-testable and the control can be wired up
// lazily, only when the feature-flagged tool is actually shown). Bridges the control to
// Model3DViewerViewModel: WebMessageReceived → OnWebMessage (JS→C#), ScriptRequested →
// InvokeScript (C#→JS), and the asset-host URL → the control's Source.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class Model3DViewerToolView : UserControl
{
    private NativeWebView? _web;
    private Model3DViewerViewModel? _vm;
    private bool _eventsWired;
    private bool _reFitQueued;   // coalesces bursts of size changes into a single canvas re-fit
    private Window? _fsWindow;   // borderless full-screen host for the whole panel

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
        if (DataContext is not Model3DViewerToolViewModel tool || tool.Viewer is null) return;
        _vm = tool.Viewer;

        // Full-screen works even when the engine/assets are missing (the fallback panel goes
        // full-screen too), so wire it before the availability gate — but only once.
        if (!_eventsWired) { _vm.FullscreenRequested += OnFullscreenRequested; _eventsWired = true; }

        if (_web is not null) return;

        // Assets missing → leave the fallback panel visible; don't instantiate the engine.
        if (!_vm.IsAvailable) { _vm.EnsureStarted(); return; }

        try
        {
            _web = new NativeWebView();
            // JS console output + uncaught errors are forwarded to the in-app Log panel by viewer.html
            // (the {type:"console"} bridge message). The engine's own inspector is also available where
            // supported (WebView2: right-click ▸ Inspect / F12), so no extra wiring is needed here (#2).
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
            _vm.ScriptRequested += OnScriptRequested;
            _vm.DevToolsRequested += OnDevToolsRequested;

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
        // NativeWebView exposes no public DevTools API, but on Windows/WebView2 the CoreWebView2 is
        // reachable through the known internal chain (verified against Avalonia.Controls.WebView 12.0.1):
        //   NativeWebView._controlHostImplTcs.Task.Result  (INativeWebViewControlImpl)
        //     .TryGetAdapter()                             (WebView2BaseAdapter on Windows)
        //     .TryGetWebView2()                            (ICoreWebView2)
        //     .OpenDevToolsWindow()
        // Anything missing (not-yet-initialised, or a non-WebView2 engine) → the F12 / Inspect hint.
        if (TryOpenNativeDevTools()) return;
        if (_vm is not null)
            _vm.Status = "Developer tools: right-click the 3D view ▸ Inspect, or press F12. JS logs are also in the Log panel.";
    }

    private bool TryOpenNativeDevTools()
    {
        if (_web is null) return false;
        try
        {
            const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 1. NativeWebView._controlHostImplTcs → Task → Result (the platform control host).
            var tcs = _web.GetType().GetField("_controlHostImplTcs", Inst)?.GetValue(_web);
            if (tcs?.GetType().GetProperty("Task")?.GetValue(tcs) is not Task { IsCompletedSuccessfully: true } task)
                return false;
            var impl = task.GetType().GetProperty("Result")?.GetValue(task);
            if (impl is null) return false;

            // 2. The WebView2 adapter: prefer the ready-completion TCS (the adapter itself), and fall
            //    back to TryGetAdapter(). Either can be null until the page's WebView2 core is created.
            var adapter = FindMethod(impl.GetType(), "TryGetAdapter")?.Invoke(impl, null)
                          ?? TaskResult(GetField(impl, "_webViewReadyCompletion"));
            if (adapter is null) return false;

            // 3. adapter.TryGetWebView2() → ICoreWebView2. NOTE: TryGetWebView2 is a *non-public* method
            //    declared on the base WebView2BaseAdapter, so a public-only GetMethod misses it — walk
            //    the hierarchy with NonPublic (this was the bug: getCore/core came back null).
            var getCore = FindMethod(adapter.GetType(), "TryGetWebView2");
            var core = getCore?.Invoke(adapter, null);
            if (core is null || getCore is null) return false;

            // 4. Invoke OpenDevToolsWindow() via the declared ICoreWebView2 interface (COM dispatch).
            var open = getCore.ReturnType.GetMethod("OpenDevToolsWindow", Type.EmptyTypes)
                       ?? core.GetType().GetMethod("OpenDevToolsWindow", Type.EmptyTypes);
            if (open is null) return false;
            open.Invoke(core, null);
            return true;
        }
        catch { return false; }
    }

    // GetMethod misses inherited non-public methods, so walk the base chain matching declared-only.
    private static MethodInfo? FindMethod(Type? t, string name)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (; t is not null; t = t.BaseType)
        {
            var m = t.GetMethod(name, F, binder: null, Type.EmptyTypes, modifiers: null);
            if (m is not null) return m;
        }
        return null;
    }

    private static object? GetField(object obj, string name) =>
        obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);

    private static object? TaskResult(object? tcsOrTask)
    {
        var task = (tcsOrTask?.GetType().GetProperty("Task")?.GetValue(tcsOrTask) ?? tcsOrTask) as Task;
        if (task is null || !task.IsCompletedSuccessfully) return null;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
