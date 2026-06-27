// VIS-01 — content view-model for the embedded 3D model viewer (CaveView.js in a NativeWebView).
//
// Owns the model/shading state and the C#↔JS protocol; the View owns the NativeWebView control
// and bridges it: it subscribes to NavigateRequested/ScriptRequested (C#→JS) and forwards the
// control's WebMessageReceived to OnWebMessage (JS→C#). Keeping the control out of the VM keeps
// this logic unit-testable and the View thin. Station picks resolve to a `.th` span via
// IStationSourceResolver and navigate the editor (Phase 3).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed partial class Model3DViewerViewModel : ObservableObject
{
    private readonly ICaveview3DAssetHost? _host;
    private readonly IStationSourceResolver? _resolver;
    private readonly IDocumentService? _documents;
    private readonly IShellOpener? _shell;
    private readonly ILogService? _log;
    private readonly IAppSettingsService? _settings;

    [ObservableProperty] private string _status = "Build a 3D model (.lox / .3d) to view it here.";
    [ObservableProperty] private string? _currentModelPath;
    [ObservableProperty] private string _shadingMode = "height";
    [ObservableProperty] private bool _isStale;
    /// <summary>False when the OS web engine (WebView2 / WebKit) is missing — drives the fallback panel.</summary>
    [ObservableProperty] private bool _isEngineAvailable = true;

    private bool _viewerReady;
    private bool _started;
    private string? _pendingModelName;   // staged name awaiting the page's 'ready' message
    private string? _pendingSelect;      // selection requested before the page was ready

    /// <summary>True when the vendored CaveView.js assets are installed.</summary>
    public bool IsAvailable => _host?.IsAvailable ?? false;
    public bool HasModel => !string.IsNullOrEmpty(CurrentModelPath);

    /// <summary>Show the "can't render in-app" panel instead of the web view (assets or engine missing).</summary>
    public bool ShowFallback => !IsAvailable || !IsEngineAvailable;

    // ---- View bridge (the View wires this to the NativeWebView control) ----
    /// <summary>Raised with a JS snippet the View should run via InvokeScript.</summary>
    public event EventHandler<string>? ScriptRequested;

    public Model3DViewerViewModel() { } // design-time

    public Model3DViewerViewModel(
        ICaveview3DAssetHost host,
        IStationSourceResolver resolver,
        IDocumentService documents,
        IShellOpener shell,
        ILogService? log = null,
        IAppSettingsService? settings = null)
    {
        _host = host;
        _resolver = resolver;
        _documents = documents;
        _shell = shell;
        _log = log;
        _settings = settings;
        if (_settings?.Current.Model3DShadingMode is { Length: > 0 } m) _shadingMode = m;
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
        if (_viewerReady) SendLoad(name);
        else _pendingModelName = name;
    }

    /// <summary>VIS-03 parallel: load the newest 3D model from a build (.lox preferred, then .3d).</summary>
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
                    if (_pendingModelName is { } pending) { SendLoad(pending); _pendingModelName = null; }
                    if (_pendingSelect is { } sel) { ScriptRequested?.Invoke(this, $"cvSelect({JsStr(sel)})"); _pendingSelect = null; }
                    break;
                case "loaded":
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
            }
        }
        catch { /* malformed bridge message — ignore */ }
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
        ApplyShading();
        if (_settings is not null)
            _settings.Save(_settings.Current with { Model3DShadingMode = mode });
    }

    [RelayCommand] private void ResetView() => ScriptRequested?.Invoke(this, "cvResetView()");

    [RelayCommand] private void Refresh() { if (CurrentModelPath is { } p) LoadModel(p); }

    /// <summary>Fallback: open the model in the OS-default 3D viewer (loch/aven via file association).</summary>
    [RelayCommand] private void OpenExternally() { if (HasModel) _shell?.Open(CurrentModelPath!); }

    private static bool HasExt(string path, string ext) =>
        path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>JSON-encode a string into a safe JS string literal for InvokeScript.</summary>
    private static string JsStr(string s) => JsonSerializer.Serialize(s);
}
