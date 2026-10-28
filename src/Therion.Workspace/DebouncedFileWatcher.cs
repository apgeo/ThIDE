// Implementation Plan �4.5 / �6 � debounced FileSystemWatcher.

using System.Collections.Concurrent;

namespace Therion.Workspace;

/// <summary>
/// Watches a set of directories and raises a single event per file path within
/// a debounce window (default 250 ms, matching Decision #24).
/// </summary>
public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly TimeSpan _debounce;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _pending
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _timer;
    private bool _disposed;

    public event Action<string>? FileChanged;

    /// <summary>Raised when a watcher fails (its buffer overflowed, the folder went away).</summary>
    public event Action<string, Exception>? Error;

    /// <summary>Only paths this accepts are reported (null: all).</summary>
    public Func<string, bool>? Filter { get; set; }

    public DebouncedFileWatcher(TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        _timer = new System.Threading.Timer(Flush, null,
            TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    }

    public void Watch(string directory, bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directory)) return;
        var full = Path.GetFullPath(directory);
        if (_watchers.ContainsKey(full)) return;

        var w = new FileSystemWatcher(full)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        w.Changed += OnEvent;
        w.Created += OnEvent;
        w.Deleted += OnEvent;
        w.Renamed += (_, e) => Enqueue(e.FullPath);
        w.Error += (_, e) => Error?.Invoke(full, e.GetException());
        _watchers[full] = w;
    }

    /// <summary>Stops watching a directory given to <see cref="Watch"/>.</summary>
    public void Unwatch(string directory)
    {
        if (_watchers.TryRemove(Path.GetFullPath(directory), out var w)) w.Dispose();
    }

    /// <summary>The directories being watched.</summary>
    public IReadOnlyCollection<string> Watched => _watchers.Keys.ToList();

    private void OnEvent(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

    private void Enqueue(string path)
    {
        if (Filter is { } accept && !accept(path)) return;
        _pending[path] = DateTime.UtcNow;
    }

    private void Flush(object? _)
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;
        foreach (var kv in _pending)
        {
            if (now - kv.Value < _debounce) continue;
            if (_pending.TryRemove(kv))
            {
                try { FileChanged?.Invoke(kv.Key); }
                catch { /* swallow � never let observer kill the timer */ }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
    }
}
