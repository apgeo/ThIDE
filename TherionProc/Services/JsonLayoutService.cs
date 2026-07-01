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
using System.Collections.Generic;
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

    /// <summary>Avalonia <c>WindowState</c> as an int (0=Normal, 1=Minimized, 2=Maximized, 3=FullScreen).</summary>
    public int WindowState { get; init; }

    // ---- dock proportions + active tabs (#17) ----
    // Persisted as plain numbers/ids and re-applied onto the freshly-built default layout, so
    // the restored tree always renders (deserializing a full Dock tree leaves panels blank in
    // Dock.Avalonia 12). Captures the common adjustments: pane sizes and which tab is selected.
    public double LeftProportion { get; init; } = 0.18;
    public double RightProportion { get; init; } = 0.22;
    public double BottomProportion { get; init; } = 0.28;
    public double CenterProportion { get; init; } = 0.60;
    /// <summary>Id of the active tab in the bottom tool dock (Diagnostics/CompilerOutput/GeneratedFiles).</summary>
    public string? BottomActiveTab { get; init; }
    /// <summary>Id of the active tab in the right tool dock.</summary>
    public string? RightActiveTab { get; init; }

    // ---- floated tool/document windows ----
    // The dock TREE itself is rebuilt fresh each launch (a deserialized Dock.Avalonia 12 tree
    // does not render), so float windows are persisted SEPARATELY and re-created at runtime by
    // re-floating the live dockables (the same code path the user's tear-off uses, which renders
    // correctly). Each entry records a window's bounds + the ids of the dockables it held.
    public IReadOnlyList<FloatWindowState> FloatWindows { get; init; } = Array.Empty<FloatWindowState>();

    public static LayoutState Default { get; } = new();
}

/// <summary>A persisted floating window: its bounds plus the ids of the dockables it contained
/// (tool singleton ids, or document file paths). Restored by re-floating those dockables.</summary>
public sealed record FloatWindowState
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 700;
    public double Height { get; init; } = 500;
    public IReadOnlyList<string> DockableIds { get; init; } = Array.Empty<string>();
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
