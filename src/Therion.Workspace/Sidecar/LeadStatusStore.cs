// lead lifecycle status, persisted in a local sidecar.
//
// The leads register is derived fresh from the source each time; the user's triage
// decisions (open → checked → pushed → dead) are layered on top here. We keep them in a per-root
// JSON sidecar rather than rewriting the Therion source, so marking a lead is instant, reversible,
// and never risks corrupting survey data. Keyed by the lead's location (station QN / scrap id).
//
// More than one store can hold the same sidecar (the IDE and its MCP server each have one), so a
// read revalidates the cached map against the file and a write re-reads the file, applies its one
// change and replaces the file atomically.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Therion.Workspace.Editing;

namespace Therion.Workspace;

public interface ILeadStatusStore
{
    /// <summary>Current status for a lead location ("open" when unset).</summary>
    string Get(string? root, string location);
    /// <summary>Sets (or clears, when "open") the status for a lead location.</summary>
    void Set(string? root, string location, string status);
}

public sealed class LeadStatusStore : ILeadStatusStore
{
    public const string Open = "open";

    private readonly string _dir;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    // Cache the currently-loaded root's map so repeated Get calls don't re-read disk while the
    // file is unchanged (same stamp).
    private string? _loadedKey;
    private FileStamp _loadedStamp;
    private Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public LeadStatusStore(ILogger<LeadStatusStore>? logger = null) : this(DefaultDir(), logger) { }

    public LeadStatusStore(string dir, ILogger<LeadStatusStore>? logger = null)
    {
        _dir = dir;
        _logger = logger;
    }

    public string Get(string? root, string location)
    {
        if (string.IsNullOrEmpty(location)) return Open;
        lock (_gate)
        {
            Load(root);
            return _map.TryGetValue(location, out var s) ? s : Open;
        }
    }

    public void Set(string? root, string location, string status)
    {
        if (string.IsNullOrEmpty(location)) return;
        lock (_gate)
        {
            // Always from disk: another store may have written within the file time's resolution.
            Load(root, force: true);
            if (string.IsNullOrEmpty(status) || string.Equals(status, Open, StringComparison.OrdinalIgnoreCase))
                _map.Remove(location);
            else
                _map[location] = status;
            Save(root);
        }
    }

    private void Load(string? root, bool force = false)
    {
        var key = Key(root);
        var stamp = key is null ? default : FileStamp.Of(PathFor(key));
        bool sameKey = string.Equals(key, _loadedKey, StringComparison.Ordinal);
        if (sameKey && !force && stamp == _loadedStamp) return;
        var previous = sameKey ? _map : null;
        _loadedKey = key;
        _loadedStamp = stamp;
        _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (key is null || !stamp.Exists) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PathFor(key)));
            if (loaded is not null) _map = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // Unreadable (locked, or damaged by an older non-atomic write): keep what this store
            // already had for the root rather than starting empty and writing that back.
            if (previous is not null) _map = previous;
            _logger?.LogWarning(ex, "Failed to load lead statuses.");
        }
    }

    private void Save(string? root)
    {
        var key = Key(root);
        if (key is null) return;
        try
        {
            Directory.CreateDirectory(_dir);
            // Atomic replace, so a crash mid-write never leaves truncated JSON behind.
            SourceFileIo.WriteOver(PathFor(key), Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true })));
            _loadedStamp = FileStamp.Of(PathFor(key));
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save lead statuses."); }
    }

    private string PathFor(string key) => Path.Combine(_dir, key + ".json");

    private static string? Key(string? root) => ProjectSidecar.KeyFor(root);

    private static string DefaultDir() => ProjectSidecar.DirectoryFor("leads");

    /// <summary>What a cached map was read from: a change in any part means the file was rewritten.</summary>
    private readonly record struct FileStamp(bool Exists, DateTime WriteTimeUtc, long Length)
    {
        public static FileStamp Of(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? new FileStamp(true, info.LastWriteTimeUtc, info.Length) : default;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return default; }
        }
    }
}
