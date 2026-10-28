// Watches the project's incoming folder when the project has turned it on (project.json in its
// config folder): a survey export that lands there is offered for import once it has finished
// arriving — again whenever a file of the same name is replaced. Off by default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Therion.Workspace;
using Therion.Workspace.Incoming;
using ThIDE.Resources;

namespace ThIDE.Services;

public interface IIncomingFolderService
{
    /// <summary>The folder being watched, or null.</summary>
    string? Folder { get; }

    /// <summary>Turns watching on or off for the current project (writes its project.json). Null on success, else why not.</summary>
    string? SetEnabled(bool enabled);
    bool IsEnabled { get; }

    /// <summary>Raised when watching starts or stops (project switch, toggle).</summary>
    event EventHandler? Changed;

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
    private readonly object _gate = new();
    // What was offered, by path, with the size and time it had: a replaced file is offered again.
    private readonly Dictionary<string, (long Size, DateTime Modified)> _offered = new(StringComparer.Ordinal);
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
        _watcher.FileChanged += OnChanged;
        _watcher.Error += (dir, ex) => _log?.Warning($"Incoming folder {dir}: {ex.Message}");
        _poll = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        _session.RootChanged += (_, _) => Restart();
        Restart();
    }

    public string? Folder { get; private set; }
    public bool IsEnabled { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler<string>? ImportRequested;

    private string? SettingsPath() => ProjectLocalConfig.PathFor(_session.RootPath, _settings.Current.ProjectConfigLocation, "project.json");

    public string? SetEnabled(bool enabled)
    {
        if (SettingsPath() is not { } path) return Tr.Get("Incoming_NoProject");
        // Change only incoming.enabled; keep whatever else the (hand-edited, shared) file holds.
        byte[] bytes;
        try
        {
            var existing = File.Exists(path) ? File.ReadAllBytes(path) : null;
            bytes = (existing is null ? null : ReplaceEnabled(existing, enabled))
                    ?? Rewritten(existing, enabled);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return string.Format(Tr.Get("Incoming_FileInvalid"), path, ex.Message);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Therion.Workspace.Editing.SourceFileIo.WriteOver(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        Restart();
        return null;
    }

    // The file with the literal after incoming.enabled swapped in place, so comments and layout stay;
    // null when there is no such value to swap. Names match in any case, as the reader matches them.
    internal static byte[]? ReplaceEnabled(byte[] utf8, bool enabled)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        bool inIncoming = false;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var name = reader.GetString();
            if (reader.CurrentDepth == 1)
            {
                if (!reader.Read()) return null;
                inIncoming = string.Equals(name, "incoming", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.StartObject;
                if (!inIncoming) reader.Skip();
            }
            else if (inIncoming && reader.CurrentDepth == 2 && string.Equals(name, "enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (!reader.Read() || reader.TokenType is not (JsonTokenType.True or JsonTokenType.False)) return null;
                int start = (int)reader.TokenStartIndex, length = reader.ValueSpan.Length;
                var literal = enabled ? "true"u8 : "false"u8;
                return [.. utf8.AsSpan(0, start), .. literal, .. utf8.AsSpan(start + length)];
            }
        }
        return null;
    }

    // No value to swap: add one (an existing file is re-serialised, which drops its comments).
    internal static byte[] Rewritten(byte[]? existing, bool enabled)
    {
        var root = existing is null ? new JsonObject { ["version"] = 1 }
            : JsonNode.Parse(existing, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
              ?? throw new JsonException("not an object");
        var key = root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, "incoming", StringComparison.OrdinalIgnoreCase)) ?? "incoming";
        var incoming = root[key] as JsonObject ?? new JsonObject { ["folder"] = "incoming" };
        var enabledKey = incoming.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, "enabled", StringComparison.OrdinalIgnoreCase)) ?? "enabled";
        incoming[enabledKey] = enabled;
        root[key] = incoming;
        return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(ProjectLocalConfig.JsonOptions));
    }

    private void Restart()
    {
        lock (_gate)
        {
            foreach (var dir in _watcher.Watched) _watcher.Unwatch(dir);
            _waiting.Clear();
            _offered.Clear();
            _stable.Clear();
            var settings = SettingsPath() is { } p ? ProjectLocalConfig.TryReadJson<ProjectSettingsFile>(p).Value : null;
            IsEnabled = settings?.Incoming.Enabled == true;
            Folder = IncomingFiles.FolderFor(_session.RootPath, settings);
            if (Folder is not null)
            {
                // Git does not keep an empty folder: a project that turned watching on may arrive without it.
                try { Directory.CreateDirectory(Folder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Warning(ex.Message); }
                _watcher.Watch(Folder);
                _log?.Info($"Watching the incoming folder {Folder}");
            }
            // Poll only while something is watched.
            _poll.Change(Folder is null ? Timeout.Infinite : 1000, Folder is null ? Timeout.Infinite : 1000);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnChanged(string path)
    {
        lock (_gate)
        {
            if (Folder is null || !path.StartsWith(Folder, StringComparison.Ordinal)) return;   // a late event from an old folder
            if (Probe(path) is not { } now) { _offered.Remove(path); _stable.Forget(path); return; }
            if (_offered.TryGetValue(path, out var then) && then == now) return;   // the same file, offered already
            _waiting.Add(path);
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
        lock (_gate)
        {
            foreach (var path in _waiting)
                if (_stable.IsStable(path)) ready.Add(path);
            foreach (var path in ready)
            {
                _waiting.Remove(path);
                _stable.Forget(path);
                if (Probe(path) is { } now) _offered[path] = now;
            }
            _waiting.RemoveWhere(p => !File.Exists(p));
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
