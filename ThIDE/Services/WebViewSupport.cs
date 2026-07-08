// cross-platform glue for the app's embedded NativeWebViews (3D viewer + structural plots).
//
// Linux is the tricky platform: the control embeds a WebKitGTK view by reparenting a
// foreign X11 window into Avalonia's window. Two failure modes surface there:
//  1. WebKitGTK's DMA-BUF renderer (default since 2.42) blanks out in exactly this
//     embedded/reparented setup — most often on NVIDIA drivers and XWayland sessions. The
//     page actually runs; the panel just stays an empty white/black box. The ecosystem-wide
//     workaround is WEBKIT_DISABLE_DMABUF_RENDERER=1, which must be in the environment
//     before any WebKit process spawns (the web process inherits our environment).
//  2. A missing engine (no webkit2gtk installed) never throws from the control's ctor —
//     adapter initialisation is asynchronous — so the panel silently stays a dead box.
//     DescribeMissingEngine() probes availability up front so views can show their
//     friendly fallback instead.
//
// DevTools: NativeWebView has no public DevTools API. On Windows the WebView2 core is
// reached through a known-internal reflection chain (verified against
// Avalonia.Controls.WebView 12.0.1); on Linux the WebKit inspector is driven through the
// public IGtkWebViewPlatformHandle + P/Invoke on the GLib thread. Everything here is
// best-effort and must never throw into the UI.

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ThIDE.Services;

public static class WebViewSupport
{
    // Startup snapshot of the Preferences ▸ Debug switches. The environment-variable knobs must
    // precede the first WebKit process, so a settings change only applies after a restart —
    // reading once here keeps every consumer consistent with what was actually applied.
    private static AppSettings s_settings = AppSettings.Default;

    /// <summary>
    /// Applies the Linux WebKitGTK environment workarounds per the Preferences ▸ Debug switches.
    /// Call first thing in Main, before Avalonia (and therefore any WebKit web process) starts.
    /// An externally-set environment variable always wins over the switch (so e.g.
    /// WEBKIT_DISABLE_DMABUF_RENDERER=0 in the shell re-enables the DMA-BUF renderer).
    /// </summary>
    public static void ConfigureEnvironment(AppSettings settings)
    {
        s_settings = settings;
        if (!OperatingSystem.IsLinux()) return;
        if (settings.WebViewDisableDmabufRenderer &&
            Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER") is null)
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
        if (settings.WebViewDisableCompositing &&
            Environment.GetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE") is null)
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
    }

    /// <summary>
    /// Returns a user-facing reason when no native web engine is installed (so views can show
    /// their fallback instead of a dead box), or null when an engine is available. Only Linux
    /// needs the probe: there the control initialises asynchronously and swallows the failure.
    /// </summary>
    public static string? DescribeMissingEngine()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            // Same order the control itself probes: WPE first, WebKitGTK otherwise.
            if (WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WpeWebKit) is { IsInstalled: true }) return null;
            if (WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk) is { IsInstalled: true }) return null;
            return ThIDE.Resources.Tr.Get("WebView_LinuxEngineMissing");
        }
        catch
        {
            // Probing must never take the panel down; let the control try its luck.
            return null;
        }
    }

    /// <summary>
    /// Applies the per-control Debug switches when the engine's environment is created. Must be
    /// called after construction but BEFORE the control is added to the visual tree
    /// (EnvironmentRequested fires on first attach). DevTools: on WebKit this turns on
    /// enable-developer-extras (right-click ▸ Inspect Element); on WebView2 it enables F12.
    /// </summary>
    public static void ConfigureWebView(NativeWebView web)
        => web.EnvironmentRequested += static (_, args) =>
        {
            args.EnableDevTools = s_settings.WebViewEnableDevTools;
            if (args is GtkWebViewEnvironmentRequestedEventArgs gtk)
                gtk.ExperimentalOffscreen = s_settings.WebViewExperimentalOffscreen;
        };

    /// <summary>Opens the engine's developer tools for <paramref name="web"/>, best-effort.</summary>
    public static bool TryOpenDevTools(NativeWebView web)
    {
        if (!s_settings.WebViewEnableDevTools) return false; // switched off in Preferences ▸ Debug
        if (OperatingSystem.IsWindows()) return TryOpenWebView2DevTools(web);
        if (OperatingSystem.IsLinux()) return TryShowWebKitGtkInspector(web);
        return false; // macOS/WKWebView: no programmatic entry point; right-click ▸ Inspect Element
    }

    // ---- Linux: WebKitGTK inspector over the public platform handle ----------------------

    // Same candidates (and order) as the control's own DllImport resolver, so we bind to the
    // library instance already loaded in this process.
    private static readonly string[] WebKitGtkLibraryNames =
    {
        "libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.1.so", "libwebkit2gtk-4.0.so.37", "libwebkit2gtk-4.0.so",
    };

    private delegate nint GetPtrFn(nint arg);
    private delegate void ShowFn(nint inspector);
    private delegate void SetExtrasFn(nint settings, int enabled); // gboolean = int

    private static bool TryShowWebKitGtkInspector(NativeWebView web)
    {
        try
        {
            if (web.TryGetPlatformHandle() is not IGtkWebViewPlatformHandle gtk || gtk.WebKitWebView == 0)
                return false;

            nint lib = 0;
            foreach (var name in WebKitGtkLibraryNames)
                if (NativeLibrary.TryLoad(name, out lib)) break;
            if (lib == 0 ||
                !NativeLibrary.TryGetExport(lib, "webkit_web_view_get_settings", out var getSettings) ||
                !NativeLibrary.TryGetExport(lib, "webkit_settings_set_enable_developer_extras", out var setExtras) ||
                !NativeLibrary.TryGetExport(lib, "webkit_web_view_get_inspector", out var getInspector) ||
                !NativeLibrary.TryGetExport(lib, "webkit_web_inspector_show", out var show))
                return false;

            var view = gtk.WebKitWebView;
            // WebKitGTK calls are only valid on the GLib thread; fire-and-forget (best-effort).
            _ = Avalonia.X11.Interop.GtkInteropHelper.RunOnGlibThread(() =>
            {
                // Extras can be toggled at runtime — set unconditionally so the inspector opens
                // even for a control created before the EnableDevTools hook existed.
                var settings = Marshal.GetDelegateForFunctionPointer<GetPtrFn>(getSettings)(view);
                if (settings != 0) Marshal.GetDelegateForFunctionPointer<SetExtrasFn>(setExtras)(settings, 1);
                var inspector = Marshal.GetDelegateForFunctionPointer<GetPtrFn>(getInspector)(view);
                if (inspector != 0) Marshal.GetDelegateForFunctionPointer<ShowFn>(show)(inspector);
                return true;
            });
            return true;
        }
        catch { return false; }
    }

    // ---- Windows: WebView2 DevTools over the known-internal chain ------------------------

    // NativeWebView exposes no public DevTools API, but on Windows/WebView2 the CoreWebView2 is
    // reachable through the known internal chain (verified against Avalonia.Controls.WebView 12.0.1):
    //   NativeWebView._controlHostImplTcs.Task.Result  (INativeWebViewControlImpl)
    //     .TryGetAdapter()                             (WebView2BaseAdapter on Windows)
    //     .TryGetWebView2()                            (ICoreWebView2)
    //     .OpenDevToolsWindow()
    // Anything missing (not-yet-initialised, or a non-WebView2 engine) → false.
    private static bool TryOpenWebView2DevTools(NativeWebView web)
    {
        try
        {
            const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 1. NativeWebView._controlHostImplTcs → Task → Result (the platform control host).
            var tcs = web.GetType().GetField("_controlHostImplTcs", Inst)?.GetValue(web);
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
