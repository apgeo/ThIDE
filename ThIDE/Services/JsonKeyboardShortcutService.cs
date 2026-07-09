// Implementation Plan �9bis.5a / Decision #29 / D19 � configurable keyboard shortcuts.
//
// JSON-backed implementation of IKeyboardShortcutService. Persistence uses the
// user-profile location described in �6.2 (sidecar override is not yet wired �
// keyboard map is global by design; per-project remap can land later via the
// same `.thp.json` infrastructure once it exists).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Therion.Processing.Abstractions;

namespace ThIDE.Services;

public sealed class JsonKeyboardShortcutService : IKeyboardShortcutService
{
    // Every gesture the app responds to lives here — nothing is hardcoded in the window or the
    // editor any more. An empty default means "bindable but unbound": the command shows up as a
    // row in Settings ▸ Keyboard so the user can assign a gesture, but ships with none.
    private static readonly IReadOnlyDictionary<string, string> _defaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ---- Shell scope (Window.KeyBindings) ----
            [ShellCommandIds.Build]                   = "F5",
            [ShellCommandIds.Rebuild]                 = "Ctrl+F5",
            [ShellCommandIds.CancelBuild]             = "Shift+F5",
            [ShellCommandIds.OpenInLoch]              = "F9",
            [ShellCommandIds.OpenInAven]              = "F10",
            [ShellCommandIds.ToggleWorkspaceExplorer] = "Ctrl+Alt+L",
            [ShellCommandIds.ToggleDiagnostics]       = "Ctrl+Shift+D",
            [ShellCommandIds.Save]                    = "Ctrl+S",
            [ShellCommandIds.GoBack]                  = "Alt+Left",
            [ShellCommandIds.GoForward]               = "Alt+Right",
            [ShellCommandIds.FindInFiles]             = "Ctrl+Shift+F",
            [ShellCommandIds.ReplaceInFiles]          = "Ctrl+Shift+H",
            [ShellCommandIds.QuickOpen]               = "Ctrl+P",
            [ShellCommandIds.CommandPalette]          = "Ctrl+Shift+P",
            [ShellCommandIds.ReopenClosedTab]         = "Ctrl+Shift+T",
            [ShellCommandIds.ToggleFullScreen]        = "F11",
            [ShellCommandIds.NextProblem]             = "F8",
            [ShellCommandIds.PreviousProblem]         = "Shift+F8",
            [ShellCommandIds.NewFile]                 = "",
            [ShellCommandIds.OpenFile]                = "",
            [ShellCommandIds.OpenFolder]              = "",
            [ShellCommandIds.OpenThconfig]            = "",
            [ShellCommandIds.ToggleObjectBrowser]     = "",
            [ShellCommandIds.ToggleOutline]           = "",
            [ShellCommandIds.ToggleProject]           = "",
            [ShellCommandIds.ToggleLog]               = "",
            [ShellCommandIds.ToggleLivePreview]       = "",
            [ShellCommandIds.ToggleMapViewer]         = "",
            [ShellCommandIds.ToggleModel3DViewer]     = "",
            [ShellCommandIds.ToggleStructuralGeology] = "",
            [ShellCommandIds.SplitEditor]             = "",
            [ShellCommandIds.ResetLayout]             = "",
            [ShellCommandIds.FloatActiveDocument]     = "",
            [ShellCommandIds.QuickExport]             = "",
            [ShellCommandIds.OpenOutputFolder]        = "",
            [ShellCommandIds.ToggleWordWrap]          = "",
            [ShellCommandIds.NewScrapScaffold]        = "",
            [ShellCommandIds.GenerateReport]          = "",

            // ---- Editor scope (matched by the focused TherionTextEditor) ----
            [ShellCommandIds.GoToDefinition]          = "F12",
            [ShellCommandIds.FindReferences]          = "Shift+F12",
            [ShellCommandIds.RenameSymbol]            = "F2",
            [ShellCommandIds.PeekDefinition]          = "Alt+F12",
            [ShellCommandIds.GoToMatchingBlock]       = "Ctrl+OemCloseBrackets",
            [ShellCommandIds.StepIntoInclude]         = "Alt+Down",
            [ShellCommandIds.StepOutInclude]          = "Alt+Up",
            [ShellCommandIds.TriggerCompletion]       = "Ctrl+Space",
            [ShellCommandIds.GoToLine]                = "Ctrl+G",
            [ShellCommandIds.ToggleComment]           = "Ctrl+OemQuestion",
            [ShellCommandIds.FormatDocument]          = "Shift+Alt+F",
            [ShellCommandIds.EncloseInRegion]         = "Ctrl+Shift+R",
            [ShellCommandIds.QuickFixes]              = "Ctrl+OemPeriod",
            [ShellCommandIds.DuplicateLines]          = "",
            [ShellCommandIds.MoveLinesUp]             = "",
            [ShellCommandIds.MoveLinesDown]           = "",
            [ShellCommandIds.SortLines]               = "",
        };

    private readonly string _storagePath;
    private Dictionary<string, string> _gestures;

    public JsonKeyboardShortcutService() : this(DefaultStoragePath()) { }

    public JsonKeyboardShortcutService(string storagePath)
    {
        _storagePath = storagePath;
        _gestures = new Dictionary<string, string>(_defaults, StringComparer.Ordinal);
        TryLoad();
    }

    public IReadOnlyDictionary<string, string> Gestures => _gestures;
    public IReadOnlyDictionary<string, string> Defaults => _defaults;

    public event EventHandler? GesturesChanged;

    public void Set(string commandId, string gesture)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return;
        _gestures[commandId] = gesture ?? string.Empty;
        TrySave();
        GesturesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDefault(string commandId)
    {
        if (_defaults.TryGetValue(commandId, out var g))
            _gestures[commandId] = g;
        else
            _gestures.Remove(commandId);
        TrySave();
        GesturesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAllToDefaults()
    {
        _gestures = new Dictionary<string, string>(_defaults, StringComparer.Ordinal);
        TrySave();
        GesturesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_storagePath)) return;
            var json = File.ReadAllText(_storagePath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null) return;
            foreach (var kv in map)
                _gestures[kv.Key] = kv.Value;
        }
        catch { /* best-effort */ }
    }

    private void TrySave()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Persist only entries that differ from defaults to keep the file readable.
            var deltas = _gestures
                .Where(kv => !_defaults.TryGetValue(kv.Key, out var d) || d != kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var json = JsonSerializer.Serialize(deltas, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch { /* best-effort */ }
    }

    private static string DefaultStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            // Linux/macOS fallback when XDG is unset.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = Path.Combine(home, ".config");
        }
        return Path.Combine(appData, "ThIDE", "keyboard.json");
    }
}
