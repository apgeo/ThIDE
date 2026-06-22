// Implementation Plan �7.2 / M6 #1 � shell layout persistence (interim).
//
// Stores the user's adjustable shell metrics:
//   - left tool pane width (Workspace Explorer)
//   - bottom tool pane height (tabbed tool panes)
//   - visibility of left + bottom tool panes
//   - window size + position
//
// Persisted as JSON under %AppData%/TherionProc/layout.json (XDG fallback on
// POSIX). The full Dock.Avalonia migration is tracked as a post-M6 follow-up;
// this service intentionally exposes a minimal contract that a future Dock.Avalonia
// `IFactory.LoadLayout` / `SaveLayout` adapter can implement without changing
// consumers.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace TherionProc.Services;

public interface ILayoutService
{
    LayoutState Current { get; }
    /// <summary>Persist the entire snapshot.</summary>
    void Save(LayoutState state);
    /// <summary>Raised when <see cref="Current"/> is replaced.</summary>
    event EventHandler? LayoutChanged;
}

public sealed record LayoutState
{
    public double LeftPaneWidth { get; init; } = 280;
    public double BottomPaneHeight { get; init; } = 260;
    public bool LeftPaneVisible { get; init; } = true;
    public bool BottomPaneVisible { get; init; } = true;
    public double WindowWidth { get; init; } = 1300;
    public double WindowHeight { get; init; } = 800;
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }

    public static LayoutState Default { get; } = new();
}

public sealed class JsonLayoutService : ILayoutService
{
    private readonly string _path;
    private readonly ILogger? _logger;
    private LayoutState _state;

    public JsonLayoutService(ILogger<JsonLayoutService>? logger = null) : this(DefaultPath(), logger) { }

    public JsonLayoutService(string path, ILogger<JsonLayoutService>? logger = null)
    {
        _path = path;
        _logger = logger;
        _state = TryLoad() ?? LayoutState.Default;
    }

    public LayoutState Current => _state;

    public event EventHandler? LayoutChanged;

    public void Save(LayoutState state)
    {
        _state = state;
        TryWrite();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private LayoutState? TryLoad()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(_path));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load layout from {Path}; using defaults.", _path);
            return null;
        }
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist layout to {Path}; window bounds may be lost.", _path);
        }
    }

    private static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = Path.Combine(home, ".config");
        }
        return Path.Combine(appData, "TherionProc", "layout.json");
    }
}
