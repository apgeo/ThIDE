// Implementation Plan §9bis.5 / D #29 — user-configurable external-tool paths.
// JSON-backed override store; mirrors JsonKeyboardShortcutService.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Therion.Processing.Abstractions;

namespace ThIDE.Services;

public sealed class JsonExternalToolPathOverrides : IExternalToolPathOverrides
{
    private readonly string _storagePath;
    private Dictionary<string, string> _map;

    public JsonExternalToolPathOverrides() : this(DefaultStoragePath()) { }

    public JsonExternalToolPathOverrides(string storagePath)
    {
        _storagePath = storagePath;
        _map = new Dictionary<string, string>(StringComparer.Ordinal);
        TryLoad();
    }

    public IReadOnlyDictionary<string, string> Overrides => _map;

    public event EventHandler? OverridesChanged;

    public void Set(string toolId, string? path)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return;
        if (string.IsNullOrWhiteSpace(path)) _map.Remove(toolId);
        else _map[toolId] = path!;
        TrySave();
        OverridesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_storagePath)) return;
            var json = File.ReadAllText(_storagePath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is not null) _map = new Dictionary<string, string>(map, StringComparer.Ordinal);
        }
        catch { /* best-effort */ }
    }

    private void TrySave()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch { /* best-effort */ }
    }

    private static string DefaultStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = Path.Combine(home, ".config");
        }
        return Path.Combine(appData, "ThIDE", "external-tools.json");
    }
}
