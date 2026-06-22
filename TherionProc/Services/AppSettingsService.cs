// Application preferences + session state, persisted as JSON under
// %AppData%/TherionProc/settings.json (XDG fallback on POSIX). Mirrors the
// JsonLayoutService pattern. Holds both user-editable preferences (shown in the
// Preferences window) and auto-managed session state (the files that were open
// at last shutdown, restored on next launch when enabled).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TherionProc.Services;

public sealed record AppSettings
{
    // ---- session ----
    /// <summary>Reopen the files that were open when the app last closed.</summary>
    public bool RestoreSessionOnStartup { get; init; } = true;
    /// <summary>Absolute paths of files open at last shutdown (most-recent active last).</summary>
    public IReadOnlyList<string> LastSessionFiles { get; init; } = Array.Empty<string>();

    // ---- editor preferences ----
    public double EditorFontSize { get; init; } = 13;
    public bool ShowLineNumbers { get; init; } = true;
    public bool HighlightCurrentLine { get; init; } = true;
    public bool ConvertTabsToSpaces { get; init; } = true;
    public int IndentationSize { get; init; } = 2;

    // ---- workspace panel ----
    public bool WorkspaceShowObjectModel { get; init; } = true;
    public bool WorkspaceRevealOnHover { get; init; }
    public bool WorkspaceRevealOnTabSwitch { get; init; }

    public static AppSettings Default { get; } = new();
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    void Save(AppSettings settings);
    event EventHandler? Changed;
}

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly string _path;
    private AppSettings _state;

    public AppSettingsService() : this(DefaultPath()) { }

    public AppSettingsService(string path)
    {
        _path = path;
        _state = TryLoad() ?? AppSettings.Default;
    }

    public AppSettings Current => _state;

    public event EventHandler? Changed;

    public void Save(AppSettings settings)
    {
        _state = settings;
        TryWrite();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings? TryLoad()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
        }
        catch { return null; }
    }

    private void TryWrite()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path,
                JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }

    private static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = Path.Combine(home, ".config");
        }
        return Path.Combine(appData, "TherionProc", "settings.json");
    }
}
