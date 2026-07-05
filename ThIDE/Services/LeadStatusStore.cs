// lead lifecycle status, persisted in a local sidecar.
//
// The leads register is derived fresh from the source each time; the user's triage
// decisions (open → checked → pushed → dead) are layered on top here. We keep them in a per-root
// JSON sidecar rather than rewriting the Therion source, so marking a lead is instant, reversible,
// and never risks corrupting survey data. Keyed by the lead's location (station QN / scrap id).

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ThIDE.Services;

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
    // Cache the currently-loaded root's map so repeated Get/Set don't re-read disk.
    private string? _loadedKey;
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
            Load(root);
            if (string.IsNullOrEmpty(status) || string.Equals(status, Open, StringComparison.OrdinalIgnoreCase))
                _map.Remove(location);
            else
                _map[location] = status;
            Save(root);
        }
    }

    private void Load(string? root)
    {
        var key = Key(root);
        if (string.Equals(key, _loadedKey, StringComparison.Ordinal)) return;
        _loadedKey = key;
        _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (key is null) return;
        try
        {
            var path = Path.Combine(_dir, key + ".json");
            if (!File.Exists(path)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (loaded is not null) _map = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to load lead statuses."); }
    }

    private void Save(string? root)
    {
        var key = Key(root);
        if (key is null) return;
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, key + ".json"),
                JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save lead statuses."); }
    }

    private static string? Key(string? root)
    {
        if (string.IsNullOrEmpty(root)) return null;
        string norm;
        try { norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToLowerInvariant(); }
        catch { norm = root.ToLowerInvariant(); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
    }

    private static string DefaultDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", "leads");
    }
}
