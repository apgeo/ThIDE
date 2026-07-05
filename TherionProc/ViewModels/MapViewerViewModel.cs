// in-app map viewer (PNG/SVG/PDF). Renders via IMapRenderService into an Avalonia bitmap
// shown in a zoomable/scrollable view; PDF gets page navigation. reuses it: ShowLatest()
// loads the newest renderable build artifact after a compile.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>One compatible map output from the active thconfig (combobox item, #2). File name shown.</summary>
public sealed record MapOutputItem(string FileName, string FullPath);

public sealed partial class MapViewerViewModel : ObservableObject
{
    // Render at a slightly higher base resolution than 1:1 so PDF/SVG stay crisp; the view's zoom
    // transform scales the bitmap on top of this.
    private const double RenderScale = 1.6;

    private readonly IMapRenderService? _render;
    private readonly IShellOpener? _shell;
    private readonly IWorkspaceSession? _session;
    private readonly IAppSettingsService? _settings;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _status = "Open a PNG, SVG or PDF — or build a map.";
    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private double _zoom = 1.0;
    /// <summary>True while a page is being rasterized off the UI thread (drives the panel spinner, #6).</summary>
    [ObservableProperty] private bool _isLoading;
    /// <summary>File size of the current file, human-readable (footer, #8). Empty when no file.</summary>
    [ObservableProperty] private string _fileSizeDisplay = string.Empty;
    /// <summary>Last-modified time of the current file (footer, #8). Empty when no file.</summary>
    [ObservableProperty] private string _modifiedDisplay = string.Empty;

    /// <summary>Compatible map outputs from the active thconfig's <c>export map</c> commands (#2).</summary>
    public ObservableCollection<MapOutputItem> AvailableOutputs { get; } = new();
    /// <summary>The output currently selected in the combobox; picking one loads it (#2).</summary>
    [ObservableProperty] private MapOutputItem? _selectedOutput;

    public bool HasImage => Image is not null;
    public bool HasMultiplePages => PageCount > 1;
    public bool HasFile => !string.IsNullOrEmpty(CurrentPath);
    public bool HasAvailableOutputs => AvailableOutputs.Count > 0;
    public string PageLabel => $"{PageIndex + 1} / {PageCount}";
    /// <summary>Current zoom as a whole-number percentage, e.g. "125%" (footer, #8).</summary>
    public string ZoomPercent => Zoom.ToString("0%", CultureInfo.InvariantCulture);

    // Set while syncing the combobox selection to the loaded file, to suppress the load-on-select.
    private bool _syncingSelection;
    // The active thconfig we've already auto-shown a map for, so auto-show fires once per load (#3).
    private string? _lastAutoShownThconfig;

    /// <summary>Raised on the UI thread after a new image is rendered, so the view can fit-to-window (#4).</summary>
    public event EventHandler? ImageLoaded;

    public MapViewerViewModel() { } // design-time
    public MapViewerViewModel(IMapRenderService render, IShellOpener shell,
        IWorkspaceSession? session = null, IAppSettingsService? settings = null)
    {
        _render = render;
        _shell = shell;
        _session = session;
        _settings = settings;
        if (_session is not null)
            _session.Changed += (_, _) => Dispatcher.UIThread.Post(OnSessionChanged);
    }

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(HasMultiplePages));
    partial void OnCurrentPathChanged(string? value) => OnPropertyChanged(nameof(HasFile));
    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomPercent));

    // Picking an output in the combobox loads it (unless we're just syncing the selection to the
    // file that is already showing).
    partial void OnSelectedOutputChanged(MapOutputItem? value)
    {
        if (_syncingSelection || value is null) return;
        if (!PathEquals(value.FullPath, CurrentPath)) Load(value.FullPath);
    }

    // The active thconfig (or its graph) changed: refresh the compatible-output list and, when
    // enabled, auto-show the first map once for this thconfig (#2/#3).
    private void OnSessionChanged()
    {
        RefreshOutputs();
        MaybeAutoShow();
    }

    /// <summary>Loads and renders the first page of <paramref name="path"/> (off the UI thread, #6).</summary>
    public void Load(string path) => _ = LoadAsync(path);

    /// <summary>Loads and renders the first page of <paramref name="path"/>; awaitable variant.</summary>
    public async Task LoadAsync(string path)
    {
        CurrentPath = path;
        PageIndex = 0;
        Zoom = 1.0;
        CaptureFileMeta(path);
        await RenderCurrentAsync().ConfigureAwait(true);
    }

    // Rasterizes the current page on a background thread, then publishes the bitmap on the UI thread
    // (the await resumes on Avalonia's UI SynchronizationContext). Keeps a big PDF/SVG from freezing
    // the UI while it decodes (#6).
    private async Task RenderCurrentAsync()
    {
        if (_render is null || string.IsNullOrEmpty(CurrentPath)) return;
        var path = CurrentPath;
        if (!_render.CanRender(path))
        {
            Image = null;
            Status = $"Can't preview {Path.GetExtension(path)} in-app — use “Open externally”.";
            return;
        }

        var page = PageIndex;
        IsLoading = true;
        try
        {
            var result = await Task.Run(() => _render.Render(path, page, RenderScale)).ConfigureAwait(true);
            Image = result.Image;
            PageCount = Math.Max(1, result.PageCount);
            Status = result.Ok ? $"{Path.GetFileName(path)}  ·  {PageLabel}" : (result.Error ?? "Render failed.");
            OnPropertyChanged(nameof(PageLabel));
            SyncSelectionToCurrent();
            if (result.Ok) ImageLoaded?.Invoke(this, EventArgs.Empty);
        }
        finally { IsLoading = false; }
    }

    // ---- compatible outputs from the active thconfig (#2) --------------------

    /// <summary>
    /// Rebuilds <see cref="AvailableOutputs"/> from the active thconfig's <c>export map</c> commands:
    /// keeps only outputs the in-app viewer can render and that exist on disk. UI-thread only.
    /// </summary>
    public void RefreshOutputs()
    {
        var desired = ComputeOutputs();
        AvailableOutputs.Clear();
        foreach (var it in desired) AvailableOutputs.Add(it);
        OnPropertyChanged(nameof(HasAvailableOutputs));
        SyncSelectionToCurrent();
    }

    private IReadOnlyList<MapOutputItem> ComputeOutputs()
    {
        var cfg = _session?.ActiveThconfig?.FullPath;
        if (_render is null || cfg is null || !File.Exists(cfg)) return Array.Empty<MapOutputItem>();
        string text;
        try { text = File.ReadAllText(cfg); } catch { return Array.Empty<MapOutputItem>(); }
        var dir = Path.GetDirectoryName(Path.GetFullPath(cfg)) ?? string.Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<MapOutputItem>();
        foreach (var e in ThconfigExportEditor.ParseExports(text))
        {
            if (!string.Equals(e.ExportType, "map", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(e.Output)) continue;
            string full;
            try
            {
                var rel = e.Output.Trim().Replace('/', Path.DirectorySeparatorChar);
                full = Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(dir, rel));
            }
            catch { continue; }
            if (!_render.CanRender(full) || !File.Exists(full)) continue; // viewable + present
            if (seen.Add(full)) list.Add(new MapOutputItem(Path.GetFileName(full), full));
        }
        return list;
    }

    /// <summary>Sets the combobox selection to the file currently showing (or null), without reloading.</summary>
    private void SyncSelectionToCurrent()
    {
        var match = AvailableOutputs.FirstOrDefault(o => PathEquals(o.FullPath, CurrentPath));
        _syncingSelection = true;
        try { SelectedOutput = match; }
        finally { _syncingSelection = false; }
    }

    // ---- auto-show first map on load (#3) ------------------------------------

    /// <summary>
    /// When "Auto-show first map on load" is on and the in-app viewer is enabled, shows the first
    /// compatible map for a newly-active thconfig (once). A per-file "auto-open" override wins;
    /// otherwise the first PDF, else the first other viewable output, is chosen. Never opens the
    /// external app on load.
    /// </summary>
    private void MaybeAutoShow()
    {
        if (_settings?.Current is not { AutoShowFirstMapOnLoad: true, EnableInAppViewer: true }) return;
        var cfg = _session?.ActiveThconfig?.FullPath;
        if (cfg is null) return;
        if (PathEquals(cfg, _lastAutoShownThconfig)) return; // only once per active-thconfig change
        _lastAutoShownThconfig = cfg;
        if (PickAutoShow() is { } path) Load(path);
    }

    private string? PickAutoShow()
    {
        if (AvailableOutputs.Count == 0) return null;
        var overrides = _settings?.Current.AutoOpenOverrides;
        // Per-file "always open" overrides take precedence over the first-PDF heuristic.
        var forced = AvailableOutputs.Where(o => IsForcedOpen(overrides, o.FullPath)).ToList();
        var pool = forced.Count > 0 ? forced : AvailableOutputs.ToList();
        var pdf = pool.FirstOrDefault(o => o.FullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        return (pdf ?? pool[0]).FullPath;
    }

    private static bool IsForcedOpen(IReadOnlyDictionary<string, bool>? overrides, string path)
    {
        if (overrides is null || overrides.Count == 0) return false;
        var key = SafeFull(path);
        foreach (var kv in overrides)
            if (kv.Value && string.Equals(SafeFull(kv.Key), key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool PathEquals(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(SafeFull(a), SafeFull(b), StringComparison.OrdinalIgnoreCase);

    private static string SafeFull(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }

    /// <summary>Captures the current file's size + modification time for the footer (#8).</summary>
    private void CaptureFileMeta(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                FileSizeDisplay = FormatSize(fi.Length);
                ModifiedDisplay = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
                return;
            }
        }
        catch { /* best-effort metadata */ }
        FileSizeDisplay = string.Empty;
        ModifiedDisplay = string.Empty;
    }

    /// <summary>Human-readable byte size (B / KB / MB / GB), invariant formatting.</summary>
    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "KB", "MB", "GB", "TB" };
        double v = bytes / 1024.0;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return string.Create(CultureInfo.InvariantCulture, $"{v:0.#} {units[u]}");
    }

    /// <summary>load the most recently written renderable map artifact from a build.</summary>
    public void ShowLatest(IEnumerable<string> artifactPaths)
    {
        if (_render is null) return;
        var best = artifactPaths
            .Where(p => _render.CanRender(p))
            .OrderByDescending(SafeWrite)
            .FirstOrDefault();
        if (best is not null) Load(best);
    }

    private static DateTime SafeWrite(string p)
    {
        try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; }
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(8.0, Math.Round(Zoom * 1.25, 3));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.1, Math.Round(Zoom / 1.25, 3));
    [RelayCommand] private void ZoomReset() => Zoom = 1.0;
    [RelayCommand] private void Refresh() => _ = RenderCurrentAsync();
    [RelayCommand] private void NextPage() { if (PageIndex < PageCount - 1) { PageIndex++; _ = RenderCurrentAsync(); } }
    [RelayCommand] private void PrevPage() { if (PageIndex > 0) { PageIndex--; _ = RenderCurrentAsync(); } }
    [RelayCommand] private void OpenExternally() { if (HasFile) _shell?.Open(CurrentPath!); }
}
