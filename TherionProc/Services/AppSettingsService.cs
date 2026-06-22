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

/// <summary>Workspace-panel presentation: relational object graph vs. plain file explorer.</summary>
public enum WorkspaceViewMode
{
    Relational = 0,
    FileExplorer = 1,
}

public sealed record AppSettings
{
    // ---- session ----
    /// <summary>Reopen the files that were open when the app last closed.</summary>
    public bool RestoreSessionOnStartup { get; init; } = true;
    /// <summary>Absolute paths of files open at last shutdown (most-recent active last).</summary>
    public IReadOnlyList<string> LastSessionFiles { get; init; } = Array.Empty<string>();

    // ---- workspace session (single root + single active thconfig) ----
    /// <summary>Root directory of the workspace at last shutdown (restored on launch, #9).</summary>
    public string? LastWorkspaceRoot { get; init; }
    /// <summary>Active thconfig path at last shutdown (restored on launch, #9).</summary>
    public string? LastActiveThconfig { get; init; }
    /// <summary>
    /// Last active thconfig chosen per workspace-root directory (full root path → full
    /// thconfig path). When a root is reopened, its remembered thconfig is reactivated
    /// instead of auto-picking one (task 1).
    /// </summary>
    public IReadOnlyDictionary<string, string> LastThconfigByRoot { get; init; } =
        new Dictionary<string, string>();
    /// <summary>Reload an open editor when its file changes on disk (clean files only; #6).</summary>
    public bool AutoReloadExternalChanges { get; init; } = true;
    /// <summary>Rebuild the object graph when a tracked file changes on disk (#5b).</summary>
    public bool AutoReloadGraphOnExternalChange { get; init; } = true;

    // ---- editor preferences ----
    public double EditorFontSize { get; init; } = 13;
    public bool ShowLineNumbers { get; init; } = true;
    public bool HighlightCurrentLine { get; init; } = true;
    public bool ConvertTabsToSpaces { get; init; } = true;
    public int IndentationSize { get; init; } = 2;
    public bool EditorWordWrap { get; init; }

    // ---- workspace panel ----
    public bool WorkspaceShowObjectModel { get; init; } = true;
    public bool WorkspaceRevealOnHover { get; init; }
    public bool WorkspaceRevealOnTabSwitch { get; init; }
    /// <summary>Relational (object graph) vs. file-explorer view (#5).</summary>
    public WorkspaceViewMode WorkspaceViewMode { get; init; } = WorkspaceViewMode.Relational;
    /// <summary>Show file nodes in the relational view; when off, only logical objects (#5a).</summary>
    public bool WorkspaceShowFilesInModel { get; init; } = true;

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
