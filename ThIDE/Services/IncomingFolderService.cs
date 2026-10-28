// Watches the project's incoming folder when the project has turned it on (project.json in its
// config folder): a survey export that lands there is offered for import once it has finished
// arriving. Off by default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Therion.Workspace;
using Therion.Workspace.Incoming;
using ThIDE.Resources;

namespace ThIDE.Services;

public interface IIncomingFolderService
{
    /// <summary>The folder being watched, or null.</summary>
    string? Folder { get; }

    /// <summary>Turns watching on or off for the current project (writes its project.json).</summary>
    void SetEnabled(bool enabled);
    bool IsEnabled { get; }

    /// <summary>Raised when the user chooses Import… on an arrived file (any thread).</summary>
    event EventHandler<string>? ImportRequested;
}

public sealed class IncomingFolderService : IIncomingFolderService, IDisposable
{
    private readonly IWorkspaceSession _session;
    private readonly IAppSettingsService _settings;
    private readonly INotificationService? _notifications;
    private readonly ILogService? _log;
    private readonly DebouncedFileWatcher _watcher = new(TimeSpan.FromMilliseconds(500));
    private readonly StableFileTracker _stable;
    private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);
    private readonly Timer _poll;

    public IncomingFolderService(IWorkspaceSession session, IAppSettingsService settings, INotificationService? notifications = null, ILogService? log = null)
    {
        _session = session;
        _settings = settings;
        _notifications = notifications;
        _log = log;
        _stable = new StableFileTracker(Probe, () => DateTime.UtcNow);
        _watcher.Filter = p => IncomingFiles.Route(p) != IncomingRoute.Ignore;
        _watcher.FileChanged += p => { lock (_waiting) if (!_offered.Contains(p)) _waiting.Add(p); };
        _watcher.Error += (dir, ex) => _log?.Warning($"Incoming folder {dir}: {ex.Message}");
        _poll = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _session.RootChanged += (_, _) => Restart();
        Restart();
    }

    public string? Folder { get; private set; }
    public bool IsEnabled => Read()?.Incoming.Enabled == true;
    public event EventHandler<string>? ImportRequested;

    private string? SettingsPath() => ProjectLocalConfig.PathFor(_session.RootPath, _settings.Current.ProjectConfigLocation, "project.json");

    private ProjectSettingsFile? Read() => SettingsPath() is { } p ? ProjectLocalConfig.TryReadJson<ProjectSettingsFile>(p).Value : null;

    public void SetEnabled(bool enabled)
    {
        if (SettingsPath() is not { } path) return;
        var file = Read() ?? new ProjectSettingsFile();
        file.Incoming.Enabled = enabled;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(file, ProjectLocalConfig.JsonOptions));
            if (enabled && IncomingFiles.FolderFor(_session.RootPath, file) is { } folder) Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Warning(ex.Message); }
        Restart();
    }

    private void Restart()
    {
        foreach (var dir in _watcher.Watched) _watcher.Unwatch(dir);
        lock (_waiting) { _waiting.Clear(); _offered.Clear(); }
        Folder = IncomingFiles.FolderFor(_session.RootPath, Read());
        if (Folder is not null && Directory.Exists(Folder))
        {
            _watcher.Watch(Folder);
            _log?.Info($"Watching the incoming folder {Folder}");
        }
    }

    private static (long, DateTime)? Probe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // Each second: offer the files that have stopped changing.
    private void Poll()
    {
        List<string> ready = [];
        lock (_waiting)
        {
            foreach (var path in _waiting)
                if (!File.Exists(path)) _offered.Add(path);
                else if (_stable.IsStable(path)) ready.Add(path);
            foreach (var path in ready) { _waiting.Remove(path); _offered.Add(path); }
            _waiting.RemoveWhere(p => _offered.Contains(p));
        }
        foreach (var path in ready) Offer(path);
    }

    private void Offer(string path)
    {
        var name = Path.GetFileName(path);
        if (IncomingFiles.Route(path) == IncomingRoute.SketchOnly)
        {
            _notifications?.Info(Tr.Get("Incoming_Title"), string.Format(Tr.Get("Incoming_Sketch"), name));
            return;
        }
        _notifications?.Info(Tr.Get("Incoming_Title"), string.Format(Tr.Get("Incoming_Arrived"), name),
            Tr.Get("Incoming_Import"), () => ImportRequested?.Invoke(this, path));
    }

    public void Dispose()
    {
        _poll.Dispose();
        _watcher.Dispose();
    }
}
