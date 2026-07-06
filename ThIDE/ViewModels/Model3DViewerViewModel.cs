// content view-model for the embedded 3D model viewer (CaveView.js in a NativeWebView).
//
// Owns the model/shading state and the C#↔JS protocol; the View owns the NativeWebView control
// and bridges it: it subscribes to NavigateRequested/ScriptRequested (C#→JS) and forwards the
// control's WebMessageReceived to OnWebMessage (JS→C#). Keeping the control out of the VM keeps
// this logic unit-testable and the View thin. Station picks resolve to a `.th` span via
// IStationSourceResolver and navigate the editor (Phase 3).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed partial class Model3DViewerViewModel : ObservableObject
{
    private readonly ICaveview3DAssetHost? _host;
    private readonly IStationSourceResolver? _resolver;
    private readonly IDocumentService? _documents;
    private readonly IShellOpener? _shell;
    private readonly ILogService? _log;
    private readonly IAppSettingsService? _settings;
    private readonly IWorkspaceSession? _session;

    [ObservableProperty] private string _status = "Build a 3D model (.lox / .3d) to view it here.";
    [ObservableProperty] private string? _currentModelPath;
    [ObservableProperty] private string _shadingMode = "height";
    [ObservableProperty] private bool _isStale;
    /// <summary>Bottom-bar notice for an old model (e.g. "Generated …. Recompile for a fresh model.").</summary>
    [ObservableProperty] private string _generatedNotice = string.Empty;
    /// <summary>False when the OS web engine (WebView2 / WebKit) is missing — drives the fallback panel.</summary>
    [ObservableProperty] private bool _isEngineAvailable = true;

    // ---- external control bar state (mirrors the speosilex.ro reference viewer) ----
    [ObservableProperty] private bool _wallsOn = true;          // walls visible by default
    [ObservableProperty] private bool _splaysOn;
    [ObservableProperty] private bool _surfaceOn;
    [ObservableProperty] private bool _stationsOn;
    [ObservableProperty] private bool _labelsOn;
    [ObservableProperty] private bool _namesOn;
    [ObservableProperty] private bool _boxOn = true;
    [ObservableProperty] private bool _autoRotateOn;
    [ObservableProperty] private bool _directionalLightOn;
    [ObservableProperty] private bool _hudOn = true;
    [ObservableProperty] private bool _sidebarOn;
    /// <summary>Current orientation button selection (none/plan/north/south/east/west).</summary>
    [ObservableProperty] private string _currentView = "none";
    /// <summary>True when the camera is orthographic (else perspective).</summary>
    [ObservableProperty] private bool _isOrthographic;
    /// <summary>True while the panel is shown full-screen (drives the toolbar's Exit-fullscreen state).</summary>
    [ObservableProperty] private bool _isFullscreen;

    // ---- model dropdown (BUILD export-model targets + detected .lox/.3d) ----
    public ObservableCollection<Model3DOption> Models { get; } = new();
    [ObservableProperty] private Model3DOption? _selectedModel;
    private bool _suppressModelSelection;   // guards SelectedModel ↔ LoadModel re-entrancy

    private bool _viewerReady;
    private bool _started;
    private string? _pendingModelName;   // staged name awaiting the page's 'ready' message
    private string? _pendingSelect;      // selection requested before the page was ready

    /// <summary>True when the vendored CaveView.js assets are installed.</summary>
    public bool IsAvailable => _host?.IsAvailable ?? false;
    public bool HasModel => !string.IsNullOrEmpty(CurrentModelPath);
    /// <summary>A model is loaded, or at least one export-model output exists on disk.</summary>
    public bool HasAnyModel => HasModel || Models.Any(m => m.Exists);
    /// <summary>True when the editor's "Open in 3D" can do something useful (assets present + a model exists).</summary>
    public bool CanShowInViewer => IsAvailable && HasAnyModel;

    /// <summary>Show the "can't render in-app" panel instead of the web view (assets or engine missing).</summary>
    public bool ShowFallback => !IsAvailable || !IsEngineAvailable;

    // ---- View bridge (the View wires this to the NativeWebView control) ----
    /// <summary>Raised with a JS snippet the View should run via InvokeScript.</summary>
    public event EventHandler<string>? ScriptRequested;
    /// <summary>Raised when the user toggles full-screen; the View reparents the panel accordingly.</summary>
    public event EventHandler<bool>? FullscreenRequested;

    public Model3DViewerViewModel() { } // design-time

    public Model3DViewerViewModel(
        ICaveview3DAssetHost host,
        IStationSourceResolver resolver,
        IDocumentService documents,
        IShellOpener shell,
        ILogService? log = null,
        IAppSettingsService? settings = null,
        IWorkspaceSession? session = null)
    {
        _host = host;
        _resolver = resolver;
        _documents = documents;
        _shell = shell;
        _log = log;
        _settings = settings;
        _session = session;
        if (_settings?.Current.Model3DShadingMode is { Length: > 0 } m) _shadingMode = m;
        // Marshal to the UI thread: the session raises Changed on a background thread (its graph
        // rebuild runs under Task.Run/ConfigureAwait(false)), and OnSessionChanged mutates the
        // UI-bound Models collection — doing that off-thread throws "the calling thread cannot
        // access this object". Every other session subscriber marshals; keep this one consistent.
        if (_session is not null) _session.Changed += (_, _) => ProjectFormat.OnUi(OnSessionChanged);
        RefreshModels();
    }

    partial void OnCurrentModelPathChanged(string? value) => OnPropertyChanged(nameof(HasModel));

    /// <summary>
    /// Called by the View when it creates the web control: starts the loopback asset host
    /// (idempotent) and returns the URL the View should navigate to, or null if unavailable.
    /// </summary>
    public string? EnsureStarted()
    {
        if (_host is null) return null;
        _started = true;
        if (!_host.IsAvailable)
        {
            Status = "The 3D viewer components are not installed.";
            return null;
        }
        if (!_host.TryStart() || _host.ViewerUrl is not { } url)
        {
            Status = "Could not start the 3D viewer.";
            return null;
        }
        return url;
    }

    /// <summary>The View reports the OS web engine is unavailable (no WebView2 / WebKit).</summary>
    public void SetEngineUnavailable(string? reason)
    {
        IsEngineAvailable = false;
        Status = string.IsNullOrEmpty(reason)
            ? "The system web engine required for the 3D view isn’t available — use “Open externally”."
            : reason;
    }

    partial void OnIsEngineAvailableChanged(bool value) => OnPropertyChanged(nameof(ShowFallback));

    /// <summary>Loads (stages + displays) a specific .lox/.3d model file.</summary>
    public void LoadModel(string path)
    {
        if (_host is null || string.IsNullOrEmpty(path)) return;
        var name = _host.StageModel(path);
        if (name is null)
        {
            Status = $"Can’t load {Path.GetFileName(path)} — only .lox / .3d models are supported.";
            return;
        }
        CurrentModelPath = path;
        Status = $"Loading {Path.GetFileName(path)}…";
        GeneratedNotice = GeneratedNoticeFor(path);
        SyncSelectedModel(path);
        if (_viewerReady) SendLoad(name);
        else _pendingModelName = name;
    }

    /// <summary>"Generated …. Recompile for a fresh model." when the model file isn't from today; else empty.</summary>
    internal static string GeneratedNoticeFor(string path)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            var when = File.GetLastWriteTime(path);
            if (when.Date == DateTime.Today) return string.Empty;
            return $"Generated {when:yyyy-MM-dd HH:mm}. Recompile for a fresh model.";
        }
        catch { return string.Empty; }
    }

    /// <summary>Select the matching dropdown entry without re-triggering a load.</summary>
    private void SyncSelectedModel(string path)
    {
        var match = Models.FirstOrDefault(m => PathsEqual(m.Path, path));
        if (ReferenceEquals(match, SelectedModel)) return;
        _suppressModelSelection = true;
        try { SelectedModel = match; }
        finally { _suppressModelSelection = false; }
    }

    partial void OnSelectedModelChanged(Model3DOption? value)
    {
        if (_suppressModelSelection || value is null) return;
        if (!PathsEqual(value.Path, CurrentModelPath))
        {
            if (File.Exists(value.Path)) LoadModel(value.Path);
            else Status = $"{value.Title} hasn’t been built yet — run the export to generate it.";
        }
    }

    private static bool PathsEqual(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>parallel: load the newest 3D model from a build (.lox preferred, then .3d).</summary>
    public void ShowLatest(IEnumerable<ArtifactRow> artifacts)
    {
        if (PickBestModel(artifacts) is not { } best) return;
        IsStale = best.IsStale;
        LoadModel(best.Path);
    }

    /// <summary>
    /// Chooses which build artifact to show in 3D: the newest .lox (CaveView's richer native
    /// format) when present, else the newest .3d; null when neither exists. Pure (unit-tested).
    /// </summary>
    public static ArtifactRow? PickBestModel(IEnumerable<ArtifactRow> artifacts)
    {
        var rows = artifacts as IReadOnlyList<ArtifactRow> ?? artifacts.ToList();
        return rows.Where(a => HasExt(a.Path, ".lox")).OrderByDescending(a => a.LastWriteUtc).FirstOrDefault()
            ?? rows.Where(a => HasExt(a.Path, ".3d")).OrderByDescending(a => a.LastWriteUtc).FirstOrDefault();
    }

    /// <summary>Phase 3 "Show in 3D": select a station/survey by its full dotted path in the model.</summary>
    public void SelectInModel(string label)
    {
        if (_host is null || string.IsNullOrWhiteSpace(label)) return;
        if (!_started) EnsureStarted();
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvSelect({JsStr(label)})");
        else _pendingSelect = label;   // applied once the page reports 'ready'
        Status = $"Showing {label} in 3D.";
    }

    /// <summary>Handles a JSON message posted by the host page (JS→C#). Never throws.</summary>
    public void OnWebMessage(string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "ready":
                    _viewerReady = true;
                    ApplyShading();
                    ApplyAllControls();
                    if (_pendingModelName is { } pending) { SendLoad(pending); _pendingModelName = null; }
                    // A reparent (full-screen toggle) can reload the page after a model was shown;
                    // re-stage the current model so it doesn't come back blank.
                    else if (CurrentModelPath is { } cur) LoadModel(cur);
                    if (_pendingSelect is { } sel) { ScriptRequested?.Invoke(this, $"cvSelect({JsStr(sel)})"); _pendingSelect = null; }
                    break;
                case "loaded":
                    // Re-assert toggles/orientation: a freshly loaded cave can reset feature layers,
                    // and a full-screen reparent reloads the page, so this also restores state then.
                    ApplyAllControls();
                    Status = HasModel
                        ? $"{Path.GetFileName(CurrentModelPath)}{(IsStale ? "  ·  ⚠ stale (rebuild to refresh)" : string.Empty)}"
                        : "Model loaded.";
                    break;
                case "station":
                    HandlePick(root);
                    break;
                case "error":
                    _log?.Warning("3D viewer: " + (root.TryGetProperty("message", out var m) ? m.GetString() : "error"));
                    break;
                case "console":
                    LogConsole(root);
                    break;
                case "esc":
                    // Esc inside the web control (#3): exit full screen if we're in it.
                    if (IsFullscreen) ToggleFullscreen();
                    break;
            }
        }
        catch { /* malformed bridge message — ignore */ }
    }

    /// <summary>Routes a forwarded page console message (#2) into the in-app Log at the right level.</summary>
    private void LogConsole(JsonElement root)
    {
        if (_log is null) return;
        var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
        if (string.IsNullOrWhiteSpace(msg)) return;
        var level = root.TryGetProperty("level", out var l) ? l.GetString() : "log";
        var text = "3D viewer JS: " + msg;
        switch (level)
        {
            case "error": _log.Error(text); break;
            case "warn":  _log.Warning(text); break;
            default:      _log.Info(text); break;
        }
    }

    private void HandlePick(JsonElement root)
    {
        var label = root.TryGetProperty("station", out var s) ? s.GetString() : null;
        if (string.IsNullOrEmpty(label) || _resolver is null) return;

        var result = _resolver.Resolve(label, _documents?.Workspace, _documents?.CurrentSemantics);
        Status = result.Message;
        if (result.Found && _documents is not null)
            _ = _documents.NavigateToSpanAsync(result.Span!.Value);
        else
            _log?.Info("3D pick — " + result.Message);
    }

    private void ApplyShading()
    {
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvSetShading({JsStr(ShadingMode)})");
    }

    private void SendLoad(string stagedName) => ScriptRequested?.Invoke(this, $"cvLoadModel({JsStr(stagedName)})");

    // ---- toolbar commands (Phase 4) ----

    [RelayCommand]
    private void SetShading(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        ShadingMode = mode;
        // Re-assert so the Color-by toggle bindings refresh even when the active button is
        // re-clicked (no value change → no automatic notification → it would look un-pushed).
        OnPropertyChanged(nameof(ShadingMode));
        ApplyShading();
        if (_settings is not null)
            _settings.Save(_settings.Current with { Model3DShadingMode = mode });
    }

    [RelayCommand] private void ResetView() => ScriptRequested?.Invoke(this, "cvResetView()");

    [RelayCommand] private void Refresh() { if (CurrentModelPath is { } p) LoadModel(p); }

    /// <summary>Fallback: open the model in the OS-default 3D viewer (loch/aven via file association).</summary>
    [RelayCommand] private void OpenExternally() { if (HasModel) _shell?.Open(CurrentModelPath!); }

    // ---- external control bar (orientation / camera / feature toggles / fullscreen) ----

    /// <summary>Orientation: plan or one of the four elevation profiles (north/south/east/west).</summary>
    [RelayCommand]
    private void SetView(string? view)
    {
        if (string.IsNullOrEmpty(view)) return;
        CurrentView = view.ToLowerInvariant();
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvSetView({JsStr(CurrentView)})");
    }

    /// <summary>Camera projection: perspective or orthographic.</summary>
    [RelayCommand]
    private void SetCamera(string? camera)
    {
        IsOrthographic = string.Equals(camera, "orthographic", StringComparison.OrdinalIgnoreCase);
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvSetCamera({JsStr(IsOrthographic ? "orthographic" : "perspective")})");
    }

    /// <summary>Toggle full-screen for the whole 3D panel (handled by the View via reparenting).</summary>
    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
        // Let the page forward Esc back to us while full-screen, even when it has keyboard focus (#3).
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvSetFullscreen({(IsFullscreen ? "true" : "false")})");
        FullscreenRequested?.Invoke(this, IsFullscreen);
    }

    /// <summary>#6: return every control switch (toggles / orientation / camera / shading) to its default.</summary>
    [RelayCommand]
    private void ResetControls()
    {
        WallsOn = true;
        SplaysOn = SurfaceOn = StationsOn = LabelsOn = NamesOn = AutoRotateOn = DirectionalLightOn = SidebarOn = false;
        BoxOn = true;
        HudOn = true;
        CurrentView = "none";
        IsOrthographic = false;
        ShadingMode = "height";
        OnPropertyChanged(nameof(ShadingMode));   // refresh the Color-by toggle bindings
        ApplyShading();
        ApplyAllControls();
    }

    /// <summary>#5: raised when the user asks for the web engine's developer tools; the View handles it.</summary>
    public event EventHandler? DevToolsRequested;
    [RelayCommand] private void OpenDevTools() => DevToolsRequested?.Invoke(this, EventArgs.Empty);

    // Each feature toggle pushes a single cvToggle() when the page is ready; otherwise the full
    // state is asserted by ApplyAllControls() once 'ready' arrives.
    partial void OnWallsOnChanged(bool value) => Toggle("walls", value);
    partial void OnSplaysOnChanged(bool value) => Toggle("splays", value);
    partial void OnSurfaceOnChanged(bool value) => Toggle("surface", value);
    partial void OnStationsOnChanged(bool value) => Toggle("stations", value);
    partial void OnLabelsOnChanged(bool value) => Toggle("labels", value);
    partial void OnNamesOnChanged(bool value) => Toggle("names", value);
    partial void OnBoxOnChanged(bool value) => Toggle("box", value);
    partial void OnAutoRotateOnChanged(bool value) => Toggle("autorotate", value);
    partial void OnDirectionalLightOnChanged(bool value) => Toggle("light", value);
    partial void OnHudOnChanged(bool value) => Toggle("hud", value);
    partial void OnSidebarOnChanged(bool value)
    {
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvSetSidebar({(value ? "true" : "false")})");
    }

    private void Toggle(string feature, bool on)
    {
        if (_viewerReady) ScriptRequested?.Invoke(this, $"cvToggle({JsStr(feature)},{(on ? "true" : "false")})");
    }

    /// <summary>Re-asserts the full control state (called on 'ready', after each load, after a reload).</summary>
    private void ApplyAllControls()
    {
        if (!_viewerReady) return;
        Toggle("walls", WallsOn);
        Toggle("splays", SplaysOn);
        Toggle("surface", SurfaceOn);
        Toggle("stations", StationsOn);
        Toggle("labels", LabelsOn);
        Toggle("names", NamesOn);
        Toggle("box", BoxOn);
        Toggle("autorotate", AutoRotateOn);
        Toggle("light", DirectionalLightOn);
        Toggle("hud", HudOn);
        ScriptRequested?.Invoke(this, $"cvSetCamera({JsStr(IsOrthographic ? "orthographic" : "perspective")})");
        if (!string.Equals(CurrentView, "none", StringComparison.Ordinal))
            ScriptRequested?.Invoke(this, $"cvSetView({JsStr(CurrentView)})");
        ScriptRequested?.Invoke(this, $"cvSetSidebar({(SidebarOn ? "true" : "false")})");
        ScriptRequested?.Invoke(this, $"cvSetFullscreen({(IsFullscreen ? "true" : "false")})");
    }

    // ---- model dropdown + default-open ----

    /// <summary>
    /// Called by the shell when the 3D Viewer pane becomes active: ensures the engine is started,
    /// refreshes the model list, and auto-opens the default model (first existing export-model
    /// output) when nothing is loaded yet.
    /// </summary>
    public void OnPanelActivated()
    {
        if (!_started) EnsureStarted();
        RefreshModels();
        if (!HasModel && Models.FirstOrDefault(m => m.Exists) is { } first)
            LoadModel(first.Path);
    }

    /// <summary>
    /// #4: a workspace/thconfig switch unloads the current model first, then re-applies the normal
    /// default-load rules (so an open panel jumps to the new project's default model).
    /// </summary>
    private void OnSessionChanged()
    {
        UnloadCurrentModel();
        RefreshModels();
        if (_started && Models.FirstOrDefault(m => m.Exists) is { } first)
            LoadModel(first.Path);
    }

    /// <summary>Clears the displayed model + its state (without touching the control toggles).</summary>
    private void UnloadCurrentModel()
    {
        CurrentModelPath = null;
        IsStale = false;
        GeneratedNotice = string.Empty;
        _pendingModelName = null;
        _suppressModelSelection = true;
        try { SelectedModel = null; } finally { _suppressModelSelection = false; }
        if (_viewerReady) ScriptRequested?.Invoke(this, "cvClear()");
        Status = "Build a 3D model (.lox / .3d) to view it here.";
    }

    /// <summary>Rebuilds <see cref="Models"/> from the active thconfig's export-model targets + detected files.</summary>
    public void RefreshModels()
    {
        var cfg = _session?.ActiveThconfig?.FullPath;
        IReadOnlyList<Model3DOption> options = Array.Empty<Model3DOption>();
        if (cfg is not null && File.Exists(cfg))
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(cfg)) ?? string.Empty;
                options = BuildModelOptions(File.ReadAllText(cfg), dir, File.Exists, DetectModelFiles(dir));
            }
            catch { /* unreadable thconfig → empty list */ }
        }

        // Mutate the existing collection in place so the ComboBox keeps a valid selection.
        Models.Clear();
        foreach (var o in options) Models.Add(o);
        if (CurrentModelPath is { } p) SyncSelectedModel(p);
    }

    /// <summary>Top-level .lox/.3d files next to the thconfig ("detected as valid file").</summary>
    private static IEnumerable<string> DetectModelFiles(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Array.Empty<string>();
            return new DirectoryInfo(dir).EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsViewable(f.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Pure list builder: every <c>export model</c> target with a viewable (.lox/.3d) output, in source
    /// order, followed by any other detected model files. Existence is decided by <paramref name="fileExists"/>.
    /// </summary>
    public static IReadOnlyList<Model3DOption> BuildModelOptions(
        string thconfigText, string thconfigDir, Func<string, bool> fileExists, IEnumerable<string> detectedFiles)
    {
        var list = new List<Model3DOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ex in ThconfigExportEditor.ParseExports(thconfigText))
        {
            // ExportTargetInfo.Title is "export <type> …"; keep only the model exports.
            if (!ex.Title.StartsWith("export model", StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(ex.Output) || !IsViewable(ex.Output!)) continue;
            var full = ResolveOutput(thconfigDir, ex.Output!);
            if (!seen.Add(full)) continue;
            bool exists = SafeExists(fileExists, full);
            list.Add(new Model3DOption(Path.GetFileName(full) + (exists ? string.Empty : "  — not built"), full, exists));
        }

        foreach (var f in detectedFiles)
        {
            if (string.IsNullOrEmpty(f) || !IsViewable(f)) continue;
            var full = SafeFullPath(f);
            if (!seen.Add(full)) continue;
            list.Add(new Model3DOption(Path.GetFileName(full), full, true));
        }
        return list;
    }

    private static bool IsViewable(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".lox" or ".3d";
    }

    private static string ResolveOutput(string dir, string output)
    {
        var rel = output.Trim().Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        try { return Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(dir, rel)); }
        catch { return rel; }
    }

    private static string SafeFullPath(string p) { try { return Path.GetFullPath(p); } catch { return p; } }
    private static bool SafeExists(Func<string, bool> exists, string p) { try { return exists(p); } catch { return false; } }

    private static bool HasExt(string path, string ext) =>
        path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>JSON-encode a string into a safe JS string literal for InvokeScript.</summary>
    private static string JsStr(string s) => JsonSerializer.Serialize(s);
}

/// <summary>One entry in the 3D-viewer model dropdown: a label + the model file path.</summary>
public sealed record Model3DOption(string Title, string Path, bool Exists)
{
    public override string ToString() => Title;   // ComboBox display without a DataTemplate
}
