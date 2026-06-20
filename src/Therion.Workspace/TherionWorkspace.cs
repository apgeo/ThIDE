// Implementation Plan §6 — in-memory project / session.
// Loads a .thconfig (or any entry-point), BFS-walks `source` directives,
// parses each file (cache-backed), and re-parses on file changes.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Workspace;

public sealed class TherionWorkspace : IWorkspace
{
    private readonly IParseCache _cache;
    private readonly WorkspaceOptions _options;
    private readonly DebouncedFileWatcher _watcher;
    private readonly object _gate = new();
    private readonly Dictionary<string, ParseResult<TherionFile>> _files
        = new(StringComparer.OrdinalIgnoreCase);

    public string? EntryPointPath { get; private set; }
    public ImmutableArray<string> LoadedFiles
    {
        get { lock (_gate) return _files.Keys.ToImmutableArray(); }
    }
    public event EventHandler? WorkspaceChanged;

    public TherionWorkspace(IParseCache? cache = null, WorkspaceOptions? options = null, IDiskParseCache? diskCache = null)
    {
        _options = options ?? new WorkspaceOptions();
        if (cache is not null)
        {
            _cache = cache;
        }
        else if (_options.DisableDiskCache)
        {
            _cache = new InMemoryParseCache();
        }
        else
        {
            var disk = diskCache ?? new JsonDiskParseCache();
            _cache = new TieredParseCache(new InMemoryParseCache(), disk);
        }
        _watcher = new DebouncedFileWatcher();
        _watcher.FileChanged += OnFileChanged;
    }

    public async ValueTask LoadAsync(string entryPointPath, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(entryPointPath);
        EntryPointPath = full;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(full);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = queue.Dequeue();
            if (!visited.Add(path)) continue;
            if (!File.Exists(path)) continue;

            var result = await Task.Run(() => ParseFile(path), cancellationToken).ConfigureAwait(false);
            lock (_gate) _files[path] = result;
            _watcher.Watch(Path.GetDirectoryName(path)!);

            foreach (var dep in ExtractDependencies(path, result.Value))
                queue.Enqueue(dep);
        }

        RaiseChanged();
    }

    public void InvalidateAll()
    {
        _cache.InvalidateAll();
        lock (_gate) _files.Clear();
        RaiseChanged();
    }

    /// <summary>Returns the latest parsed snapshot for <paramref name="path"/>, or <c>null</c>.</summary>
    public ParseResult<TherionFile>? TryGetFile(string path)
    {
        lock (_gate)
            return _files.TryGetValue(Path.GetFullPath(path), out var r) ? r : null;
    }

    /// <summary>
    /// Build a workspace-wide semantic snapshot (Plan §5, §5.1) — aggregates
    /// per-file <see cref="SemanticModel"/> + cross-file <see cref="XviIndex"/>
    /// and FileGraph edges. Parsed `.xvi` files in the workspace are picked
    /// up automatically by extension; pass <paramref name="extraXvi"/> to
    /// inject additional parsed XVI files known outside the loaded set.
    /// </summary>
    public WorkspaceSemanticModel BuildSemanticModel(IEnumerable<XviFile>? extraXvi = null)
    {
        Dictionary<string, ParseResult<TherionFile>> snapshot;
        lock (_gate) snapshot = new(_files, StringComparer.OrdinalIgnoreCase);

        var xviList = new List<XviFile>();
        if (extraXvi is not null) xviList.AddRange(extraXvi);
        // Auto-discover .xvi siblings of any loaded .th2 file (best-effort).
        foreach (var path in snapshot.Keys)
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            if (!path.EndsWith(".th2", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var xviPath in Directory.EnumerateFiles(dir, "*.xvi"))
            {
                try
                {
                    var text = File.ReadAllText(xviPath);
                    var parsed = new XviParser().Parse(xviPath, text);
                    if (parsed.Value is not null) xviList.Add(parsed.Value);
                }
                catch { /* skip unreadable */ }
            }
        }

        return WorkspaceSemanticModel.Build(snapshot, xviList);
    }

    private ParseResult<TherionFile> ParseFile(string path)
    {
        var info = new FileInfo(path);
        var version = TherionSyntaxVersion.Default;
        var key = new ParseCacheKey(path, info.Length, info.LastWriteTimeUtc, version);

        if (!_options.DisableDiskCache && _cache.TryGet(key, out var cached))
            return cached;

        var text = File.ReadAllText(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        ParseResult<TherionFile> result = ext switch
        {
            ".th2"      => new Th2Parser().Parse(path, text),
            ".thconfig" => new ThconfigParser().Parse(path, text),
            ".thc"      => new ThconfigParser().Parse(path, text),
            ".th"       => new ThParser().Parse(path, text),
            _           => ProbeAndParse(path, text),
        };

        _cache.Set(key, result);
        return result;
    }

    private static ParseResult<TherionFile> ProbeAndParse(string path, string text)
    {
        // No extension (e.g., `thconfig`): assume thconfig.
        return new ThconfigParser().Parse(path, text);
    }

    private static IEnumerable<string> ExtractDependencies(string parentPath, TherionFile? file)
    {
        if (file is null) yield break;
        var dir = Path.GetDirectoryName(parentPath)!;
        foreach (var child in file.Children)
        {
            if (child is not UnknownCommand cmd) continue;
            if (!IsSourceLike(cmd.Keyword)) continue;
            foreach (var token in SplitArgs(cmd.RawArguments))
            {
                var dep = token;
                if (!Path.IsPathRooted(dep)) dep = Path.Combine(dir, dep);
                yield return Path.GetFullPath(dep);
            }
        }
    }

    private static bool IsSourceLike(string keyword)
        => string.Equals(keyword, "source", StringComparison.OrdinalIgnoreCase)
        || string.Equals(keyword, "input", StringComparison.OrdinalIgnoreCase)
        || string.Equals(keyword, "load", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length) yield break;
            if (raw[i] == '"')
            {
                int end = raw.IndexOf('"', ++i);
                if (end < 0) { yield return raw[i..]; yield break; }
                yield return raw[i..end];
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                yield return raw[start..i];
            }
        }
    }

    private void OnFileChanged(string path)
    {
        var full = Path.GetFullPath(path);
        bool tracked;
        lock (_gate) tracked = _files.ContainsKey(full);
        if (!tracked) return;

        _cache.Invalidate(full);
        if (!File.Exists(full))
        {
            lock (_gate) _files.Remove(full);
        }
        else
        {
            var result = ParseFile(full);
            lock (_gate) _files[full] = result;
        }
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try { WorkspaceChanged?.Invoke(this, EventArgs.Empty); }
        catch { /* swallow */ }
    }

    public ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        return ValueTask.CompletedTask;
    }
}
