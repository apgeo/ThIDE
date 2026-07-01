// Single-root workspace session (re-org #1–#9). Owns the one workspace root
// directory, the one active thconfig, the single shared object graph
// (WorkspaceSemanticModel) built from that thconfig, and a recursive filesystem
// watcher that keeps the file-explorer + object graph in sync with disk.
//
// This pulls workspace/session concerns out of DocumentService (SRP): DocumentService
// now delegates all "what project are we in?" questions here. The service is UI-agnostic
// — it exposes immutable snapshots + events; view-models marshal to the UI thread.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Workspace;

namespace TherionProc.Services;

/// <summary>One detected/known thconfig file, with a display string for the dropdown (#3).</summary>
public sealed record ThconfigCandidate(string FullPath, string DisplayPath, bool IsExternal);

/// <summary>An on-disk change to a tracked file (drives editor + workspace banners, #5b/#6).</summary>
public sealed record ExternalFileChange(string Path, DateTime TimeUtc, bool Deleted);

public interface IWorkspaceSession : IAsyncDisposable
{
    string? RootPath { get; }
    IReadOnlyList<ThconfigCandidate> Candidates { get; }
    ThconfigCandidate? ActiveThconfig { get; }
    WorkspaceSemanticModel? Model { get; }

    /// <summary>PERF-02: true while the object graph is being (re)built on a background thread.</summary>
    bool IsIndexing { get; }
    /// <summary>PERF-02: raised when <see cref="IsIndexing"/> changes (drives the status-bar indicator).</summary>
    event EventHandler? IndexingChanged;

    /// <summary>PERF-03: the current persistent symbol-index snapshot — warm-loaded on root change
    /// before the graph is (re)built, then refreshed after each build. Null when none is available.</summary>
    WorkspaceSymbolIndex? SymbolIndex { get; }

    /// <summary>Raised after the object graph (<see cref="Model"/>) is rebuilt.</summary>
    event EventHandler? Changed;
    /// <summary>Raised after <see cref="RevalidateBuffersAsync"/> rebuilds the graph from unsaved buffers
    /// (a lighter signal than <see cref="Changed"/>: it drives diagnostics/squiggles without the heavier
    /// full-UI refresh such as rebuilding the workspace tree).</summary>
    event EventHandler? BuffersRevalidated;
    /// <summary>Raised when <see cref="RootPath"/> changes.</summary>
    event EventHandler? RootChanged;
    /// <summary>Raised when the thconfig candidate list changes.</summary>
    event EventHandler? CandidatesChanged;
    /// <summary>Raised when a tracked file changes on disk (excluding our own saves).</summary>
    event EventHandler<ExternalFileChange>? ExternalFileChanged;
    /// <summary>Raised on any filesystem change under the root (drives file-explorer refresh).</summary>
    event EventHandler? FileSystemChanged;

    /// <summary>Sets the workspace root, scans for thconfigs, and activates the first one.</summary>
    Task SetRootAsync(string rootDir, CancellationToken ct = default);
    /// <summary>
    /// Makes <paramref name="thconfigPath"/> the active reference and rebuilds the graph.
    /// Returns false when the file is missing or fails to load (so callers can warn the user
    /// instead of failing silently, #8).
    /// </summary>
    Task<bool> SetActiveThconfigAsync(string thconfigPath, CancellationToken ct = default);
    /// <summary>Establishes a workspace for a directly-opened file when none exists yet.</summary>
    Task EnsureCoversAsync(string filePath, CancellationToken ct = default);
    /// <summary>
    /// Live "validate on type": rebuilds the object graph with the given unsaved editor buffers overlaid
    /// on the on-disk files (only files already in the graph are overlaid). Files previously overlaid but
    /// no longer supplied here (saved / closed) are restored from disk. Raises
    /// <see cref="BuffersRevalidated"/> rather than <see cref="Changed"/>.
    /// </summary>
    Task RevalidateBuffersAsync(IReadOnlyList<(string Path, string Text)> buffers, CancellationToken ct = default);
    /// <summary>Adds an open thconfig located outside the root to the candidate list (#3).</summary>
    void RegisterExternalThconfig(string thconfigPath);
    /// <summary>True when <paramref name="filePath"/> participates in the active object graph (#4).</summary>
    bool Covers(string filePath);
    /// <summary>Marks a path we are about to write ourselves, so it isn't flagged as external (#6).</summary>
    void SuppressSelfWrite(string filePath);
}

public sealed class WorkspaceSessionService : IWorkspaceSession
{
    private readonly IThconfigSniffer _sniffer;
    private readonly IAppSettingsService? _settings;
    private readonly IWorkspaceSymbolIndexStore? _symbolIndexStore;   // PERF-03
    private readonly object _gate = new();

    private TherionWorkspace? _workspace;
    private DebouncedFileWatcher? _rootWatcher;
    private readonly Dictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _externalConfigs = new(StringComparer.OrdinalIgnoreCase);

    public string? RootPath { get; private set; }
    public IReadOnlyList<ThconfigCandidate> Candidates { get; private set; } = Array.Empty<ThconfigCandidate>();
    public ThconfigCandidate? ActiveThconfig { get; private set; }
    public WorkspaceSemanticModel? Model { get; private set; }

    public bool IsIndexing { get; private set; }
    public event EventHandler? IndexingChanged;

    public WorkspaceSymbolIndex? SymbolIndex { get; private set; }   // PERF-03

    private void SetIndexing(bool value)
    {
        if (IsIndexing == value) return;
        IsIndexing = value;
        IndexingChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
    public event EventHandler? BuffersRevalidated;
    public event EventHandler? RootChanged;
    public event EventHandler? CandidatesChanged;
    public event EventHandler<ExternalFileChange>? ExternalFileChanged;
    public event EventHandler? FileSystemChanged;

    private readonly ILogService? _log;

    public WorkspaceSessionService(IThconfigSniffer sniffer, IAppSettingsService? settings = null,
        ILogService? log = null, IWorkspaceSymbolIndexStore? symbolIndexStore = null)
    {
        _sniffer = sniffer;
        _settings = settings;
        _log = log;
        _symbolIndexStore = symbolIndexStore;
    }

    public async Task SetRootAsync(string rootDir, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) return;
        var full = Path.GetFullPath(rootDir);

        // Switching to a *different* root must not carry the previous directory's state: drop
        // the externally-registered thconfigs and unload the old active config + graph so the
        // dropdown and object tree reflect only the new directory (#2). Without this, old
        // thconfigs linger in the selector and the previous project stays loaded.
        if (!string.Equals(full, RootPath, StringComparison.OrdinalIgnoreCase))
            await ResetForNewRootAsync().ConfigureAwait(false);

        RootPath = full;
        RootChanged?.Invoke(this, EventArgs.Empty);
        RecordRecentDirectory(full);
        _log?.Info($"Workspace root set: {full}");

        // PERF-03: warm symbol search from the persisted index immediately, before the (background)
        // graph build replaces it — so "Go to Symbol in Workspace" works the instant a project opens.
        if (_symbolIndexStore is not null)
            try { SymbolIndex = _symbolIndexStore.Load(full); } catch { /* best-effort */ }

        WatchRoot(full);
        RescanCandidates();

        // Pick the thconfig to activate as the reference graph (task 1).
        var chosen = ChooseInitialThconfig(full);
        if (chosen is not null)
            await SetActiveThconfigAsync(chosen, ct).ConfigureAwait(false);
        else
            Raise(); // empty root with no thconfig: notify so the UI clears the old graph
    }

    /// <summary>
    /// Clears all carried-over state before adopting a new root: forgets externally-opened
    /// thconfigs, unloads the active workspace/graph, and resets the active selection so a
    /// fresh <see cref="RescanCandidates"/> reflects only the incoming directory (#2).
    /// </summary>
    private async Task ResetForNewRootAsync()
    {
        TherionWorkspace? old;
        lock (_gate)
        {
            _externalConfigs.Clear();
            old = _workspace;
            if (old is not null) old.WorkspaceChanged -= OnWorkspaceChanged;
            _workspace = null;
            Model = null;
        }
        ActiveThconfig = null;
        if (old is not null) { try { await old.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
    }

    /// <summary>
    /// Chooses which detected thconfig to open for a freshly-set root (task 1):
    /// the one remembered for this directory if still present, otherwise the one highest
    /// in the directory tree (fewest path segments below the root) and, among ties, the
    /// most recently modified.
    /// </summary>
    private string? ChooseInitialThconfig(string root)
    {
        var internalCandidates = Candidates.Where(c => !c.IsExternal).Select(c => c.FullPath).ToList();
        if (internalCandidates.Count == 0) return null;

        // 1. Remembered choice for this root, if it still exists and is a candidate.
        if (_settings?.Current.LastThconfigByRoot is { } map &&
            map.TryGetValue(NormalizeDir(root), out var remembered) &&
            File.Exists(remembered) &&
            internalCandidates.Any(p => string.Equals(p, remembered, StringComparison.OrdinalIgnoreCase)))
            return remembered;

        // 2. Highest in the tree (shallowest), then newest modified.
        return internalCandidates
            .OrderBy(p => DepthBelowRoot(root, p))
            .ThenByDescending(LastWriteUtc)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int DepthBelowRoot(string root, string path)
    {
        try
        {
            var rel = Path.GetRelativePath(root, path);
            return rel.Count(c => c is '/' or '\\');
        }
        catch { return int.MaxValue; }
    }

    private static DateTime LastWriteUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string NormalizeDir(string dir)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)); }
        catch { return dir; }
    }

    private const int MaxRecentDirectories = 16;

    // Promotes a just-opened working directory to the front of the persisted recent list, so it
    // appears in File ▸ Recent Directories. De-duplicated (path-normalized) and capped.
    private void RecordRecentDirectory(string fullDir)
    {
        if (_settings is null) return;
        var key = NormalizeDir(fullDir);
        var current = _settings.Current;
        var list = new List<string>(current.RecentDirectories.Count + 1) { fullDir };
        foreach (var p in current.RecentDirectories)
            if (!string.Equals(NormalizeDir(p), key, StringComparison.OrdinalIgnoreCase)) list.Add(p);
        if (list.Count > MaxRecentDirectories) list.RemoveRange(MaxRecentDirectories, list.Count - MaxRecentDirectories);
        try { _settings.Save(current with { RecentDirectories = list }); } catch { /* best-effort */ }
    }

    // Persists the active thconfig for the current root so reopening the directory
    // restores the same choice (task 1).
    private void RememberActiveForRoot()
    {
        if (_settings is null || RootPath is not { } root || ActiveThconfig is not { } active) return;
        if (!IsUnderRoot(active.FullPath)) return; // only remember configs that live under the root

        var key = NormalizeDir(root);
        var cur = _settings.Current;
        if (cur.LastThconfigByRoot.TryGetValue(key, out var prev) &&
            string.Equals(prev, active.FullPath, StringComparison.OrdinalIgnoreCase))
            return; // already recorded

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in cur.LastThconfigByRoot) map[kv.Key] = kv.Value;
        map[key] = active.FullPath;
        _settings.Save(cur with { LastThconfigByRoot = map });
    }

    public async Task<bool> SetActiveThconfigAsync(string thconfigPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(thconfigPath)) return false;
        var full = Path.GetFullPath(thconfigPath);
        if (!File.Exists(full)) return false; // missing file → let the caller warn (#8)

        var ws = new TherionWorkspace();
        try { await ws.LoadAsync(full, ct).ConfigureAwait(false); }
        catch (Exception ex) { _log?.Error($"Failed to load thconfig '{full}': {ex.Message}"); await ws.DisposeAsync().ConfigureAwait(false); return false; }

        // PERF-02: build the (potentially heavy) cross-file semantic model on a background thread
        // with a "ready" indicator, so a large project never blocks the UI while it indexes.
        WorkspaceSemanticModel model;
        SetIndexing(true);
        try { model = await Task.Run(() => ws.BuildSemanticModel(), ct).ConfigureAwait(false); }
        finally { SetIndexing(false); }
        var old = SwapWorkspace(ws, model, full);
        UpdateSymbolIndex(model);   // PERF-03
        if (old is not null) await old.DisposeAsync().ConfigureAwait(false);

        // The active config (and its directory) may not be in the candidate list yet.
        if (RootPath is null) { RootPath = Path.GetDirectoryName(full); RootChanged?.Invoke(this, EventArgs.Empty); WatchRoot(RootPath); }
        EnsureCandidate(full);
        RememberActiveForRoot();
        _log?.Info($"Active thconfig: {full} ({model.PerFile.Count} .th file(s) loaded)");
        Raise();
        return true;
    }

    // Files currently overlaid with an unsaved buffer (so they can be restored from disk later).
    private readonly HashSet<string> _bufferOverlaid = new(StringComparer.OrdinalIgnoreCase);

    public async Task RevalidateBuffersAsync(IReadOnlyList<(string Path, string Text)> buffers, CancellationToken ct = default)
    {
        TherionWorkspace? ws;
        lock (_gate) ws = _workspace;
        if (ws is null) return;
        if (buffers.Count == 0 && _bufferOverlaid.Count == 0) return;   // nothing dirty and no overlay to undo

        SetIndexing(true);
        try
        {
            WorkspaceSemanticModel model;
            try
            {
                model = await Task.Run(() =>
                {
                    var loaded = new HashSet<string>(ws.LoadedFiles, StringComparer.OrdinalIgnoreCase);
                    var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (path, _) in buffers) current.Add(TryFull(path));

                    // Drop overlays no longer supplied (the buffer was saved or its tab closed).
                    foreach (var prev in _bufferOverlaid.ToList())
                        if (!current.Contains(prev)) { ws.ReloadFileFromDisk(prev); _bufferOverlaid.Remove(prev); }

                    foreach (var (path, text) in buffers)
                    {
                        var full = TryFull(path);
                        if (loaded.Contains(full)) { ws.UpdateFileFromText(full, text); _bufferOverlaid.Add(full); }
                    }
                    return ws.BuildSemanticModel();
                }, ct).ConfigureAwait(false);
            }
            catch { return; }

            lock (_gate) { if (!ReferenceEquals(_workspace, ws)) return; Model = model; }
            UpdateSymbolIndex(model);
            BuffersRevalidated?.Invoke(this, EventArgs.Empty);
        }
        finally { SetIndexing(false); }
    }

    public async Task EnsureCoversAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        var full = Path.GetFullPath(filePath);
        if (Covers(full)) return;

        // A thconfig opened from outside the root just joins the dropdown (#3).
        if (IsThconfig(full) && RootPath is not null && !IsUnderRoot(full))
        {
            RegisterExternalThconfig(full);
            return;
        }

        // No workspace yet → derive the project entry from the file and root there.
        if (_workspace is null || RootPath is null)
        {
            string entry;
            try { entry = await Task.Run(() => ProjectEntryDiscovery.FindEntryPoint(full), ct).ConfigureAwait(false); }
            catch { entry = full; }
            await SetActiveThconfigAsync(entry, ct).ConfigureAwait(false);
        }
        // Otherwise the file is simply an orphan inside the existing root (gets a banner).
    }

    public void RegisterExternalThconfig(string thconfigPath)
    {
        if (string.IsNullOrEmpty(thconfigPath)) return;
        var full = Path.GetFullPath(thconfigPath);
        lock (_gate) { if (!_externalConfigs.Add(full)) return; }
        RescanCandidates();
    }

    public bool Covers(string filePath)
    {
        var model = Model;
        if (model is null || string.IsNullOrEmpty(filePath)) return false;
        var full = TryFull(filePath);
        if (model.PerFile.ContainsKey(full)) return true;
        foreach (var (from, to) in model.FileGraphEdges)
            if (string.Equals(from, full, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(to, full, StringComparison.OrdinalIgnoreCase)) return true;
        // The active thconfig itself is part of the graph even when it sources nothing.
        return ActiveThconfig is { } a && string.Equals(a.FullPath, full, StringComparison.OrdinalIgnoreCase);
    }

    public void SuppressSelfWrite(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        lock (_gate) _selfWrites[TryFull(filePath)] = DateTime.UtcNow;
    }

    // ---- internals ---------------------------------------------------------

    private TherionWorkspace? SwapWorkspace(TherionWorkspace ws, WorkspaceSemanticModel model, string activePath)
    {
        TherionWorkspace? old;
        lock (_gate)
        {
            old = _workspace;
            if (old is not null) old.WorkspaceChanged -= OnWorkspaceChanged;
            _workspace = ws;
            ws.WorkspaceChanged += OnWorkspaceChanged;
            Model = model;
        }
        ActiveThconfig = new ThconfigCandidate(activePath, DisplayFor(activePath), !IsUnderRoot(activePath));
        return ReferenceEquals(old, ws) ? null : old;
    }

    // The TherionWorkspace re-parses a changed tracked file and raises this; rebuild the
    // shared object graph from the new snapshot (gated by the user setting, #5b).
    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_settings is { Current.AutoReloadGraphOnExternalChange: false }) return;
        if (sender is not TherionWorkspace ws) return;
        _ = RebuildGraphAsync(ws);   // PERF-02: rebuild off the watcher thread, with the indicator
    }

    private async Task RebuildGraphAsync(TherionWorkspace ws)
    {
        SetIndexing(true);
        try
        {
            WorkspaceSemanticModel model;
            try { model = await Task.Run(() => ws.BuildSemanticModel()).ConfigureAwait(false); }
            catch { return; }
            lock (_gate) { if (!ReferenceEquals(_workspace, ws)) return; Model = model; }
            UpdateSymbolIndex(model);   // PERF-03
            Raise();
        }
        finally { SetIndexing(false); }
    }

    /// <summary>PERF-03: rebuilds the persistent symbol index from the new model and persists it.</summary>
    private void UpdateSymbolIndex(WorkspaceSemanticModel model)
    {
        if (_symbolIndexStore is null) return;
        try
        {
            var index = _symbolIndexStore.Build(RootPath, model);
            SymbolIndex = index;
            _symbolIndexStore.Save(index);
        }
        catch { /* best-effort — symbol search still works from the live model */ }
    }

    private void WatchRoot(string? root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        _rootWatcher ??= CreateRootWatcher();
        _rootWatcher.Watch(root, includeSubdirectories: true);
    }

    private DebouncedFileWatcher CreateRootWatcher()
    {
        var w = new DebouncedFileWatcher();
        w.FileChanged += OnRootFileChanged;
        return w;
    }

    private void OnRootFileChanged(string path)
    {
        var full = TryFull(path);

        // File-explorer + candidate list reflect every add/delete/rename/move (#5b).
        FileSystemChanged?.Invoke(this, EventArgs.Empty);
        if (IsThconfig(full)) RescanCandidates();

        if (IsSelfWrite(full)) return; // our own Save — not an external edit

        bool tracked = Covers(full);
        if (!tracked) return;
        ExternalFileChanged?.Invoke(this, new ExternalFileChange(full, DateTime.UtcNow, !File.Exists(full)));
    }

    private bool IsSelfWrite(string full)
    {
        lock (_gate)
        {
            if (!_selfWrites.TryGetValue(full, out var when)) return false;
            // Self-write window: the watcher fires shortly after our File.WriteAllText.
            if (DateTime.UtcNow - when > TimeSpan.FromSeconds(3)) { _selfWrites.Remove(full); return false; }
            return true;
        }
    }

    private void RescanCandidates()
    {
        var list = new List<ThconfigCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (RootPath is { } root)
            foreach (var p in ThconfigDiscovery.Scan(root, _sniffer))
                if (seen.Add(p)) list.Add(new ThconfigCandidate(p, DisplayFor(p), false));

        // Configs open from outside the root (and the active config, wherever it lives).
        lock (_gate)
        {
            foreach (var p in _externalConfigs)
                if (seen.Add(p)) list.Add(new ThconfigCandidate(p, DisplayFor(p), !IsUnderRoot(p)));
        }
        if (ActiveThconfig is { } a && seen.Add(a.FullPath))
            list.Add(a with { DisplayPath = DisplayFor(a.FullPath) });

        Candidates = list;
        CandidatesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureCandidate(string full)
    {
        if (Candidates.Any(c => string.Equals(c.FullPath, full, StringComparison.OrdinalIgnoreCase))) return;
        if (!IsUnderRoot(full)) lock (_gate) _externalConfigs.Add(full);
        RescanCandidates();
    }

    private string DisplayFor(string path) => WorkspacePathFormatter.Display(RootPath, path);

    private bool IsUnderRoot(string path)
    {
        if (RootPath is null) return false;
        try
        {
            var rel = Path.GetRelativePath(RootPath, Path.GetFullPath(path));
            return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
        }
        catch { return false; }
    }

    private static bool IsThconfig(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Length == 0
            ? string.Equals(Path.GetFileName(path), "thconfig", StringComparison.OrdinalIgnoreCase)
            : ext.Equals(".thconfig", StringComparison.OrdinalIgnoreCase)
              || ext.Equals(".thc", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryFull(string p) { try { return Path.GetFullPath(p); } catch { return p; } }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        _rootWatcher?.Dispose();
        TherionWorkspace? ws;
        lock (_gate) { ws = _workspace; _workspace = null; }
        if (ws is not null) { try { await ws.DisposeAsync().ConfigureAwait(false); } catch { } }
    }
}
