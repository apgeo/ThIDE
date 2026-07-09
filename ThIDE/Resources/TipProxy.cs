// Reactive source behind the {l:Tip Key} markup extension: the localized tooltip text for a toolbar
// button, with the keyboard shortcut currently bound to that action appended as " (gesture)" when it
// has one. It combines LocProxy (UI language) with the configurable IKeyboardShortcutService
// (remappable gestures) plus a fixed table (the shell's built-in, non-remappable chords). Invalidate()
// re-reads every {l:Tip} binding when the language OR a keybinding changes — so tooltips relocalize on
// a language switch and reflect a remapped shortcut immediately.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Therion.Processing.Abstractions;

namespace ThIDE.Resources;

public sealed class TipProxy : INotifyPropertyChanged
{
    public static TipProxy Instance { get; } = new();

    private TipProxy()
    {
        // Relocalize when the UI language switches (LocProxy is invalidated by LanguageService).
        LocProxy.Instance.PropertyChanged += (_, _) => Invalidate();
    }

    private IKeyboardShortcutService? _service;

    // Toolbar tooltip loc-key → remappable command id (gesture read live from the service).
    private static readonly Dictionary<string, string> CommandByKey = new(StringComparer.Ordinal)
    {
        ["Tb_Back"]             = ShellCommandIds.GoBack,
        ["Tb_Forward"]          = ShellCommandIds.GoForward,
        ["Tb_Save"]             = ShellCommandIds.Save,
        ["Tb_FindInFiles"]      = ShellCommandIds.FindInFiles,
        ["Tb_RenameSymbol"]     = ShellCommandIds.RenameSymbol,
        ["Tb_OpenLoch"]         = ShellCommandIds.OpenInLoch,
        ["Tb_OpenAven"]         = ShellCommandIds.OpenInAven,
        ["Tb_Compile"]          = ShellCommandIds.Build,
        ["Tb_CancelCompile"]    = ShellCommandIds.CancelBuild,
        ["Menu_View_Workspace"] = ShellCommandIds.ToggleWorkspaceExplorer,
        ["Tool_Diagnostics"]    = ShellCommandIds.ToggleDiagnostics,
        ["Tb_GoToFile"]         = ShellCommandIds.QuickOpen,
        ["Tb_GoToAction"]       = ShellCommandIds.CommandPalette,
        ["Tb_FullScreen"]       = ShellCommandIds.ToggleFullScreen,
    };

    // Toolbar tooltip loc-key → fixed chord. Only genuinely non-remappable ones belong here: these
    // are AvaloniaEdit's own clipboard bindings, not gestures the shortcut service dispatches.
    private static readonly Dictionary<string, string> FixedByKey = new(StringComparer.Ordinal)
    {
        ["Tb_Cut"]   = "Ctrl+X",
        ["Tb_Copy"]  = "Ctrl+C",
        ["Tb_Paste"] = "Ctrl+V",
    };

    /// <summary>Wires the configurable shortcut service and relays its change notifications.</summary>
    public void Attach(IKeyboardShortcutService service)
    {
        if (ReferenceEquals(_service, service)) return;
        if (_service is not null) _service.GesturesChanged -= OnGesturesChanged;
        _service = service;
        _service.GesturesChanged += OnGesturesChanged;
        Invalidate();
    }

    private void OnGesturesChanged(object? sender, EventArgs e) => Invalidate();

    /// <summary>The localized tooltip for <paramref name="key"/> plus " (gesture)" when it has a shortcut.</summary>
    public string this[string key]
    {
        get
        {
            var text = Tr.Get(key);
            var gesture = GestureFor(key);
            return string.IsNullOrEmpty(gesture) ? text : $"{text} ({gesture})";
        }
    }

    private string GestureFor(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (CommandByKey.TryGetValue(key, out var id) && _service is { } svc &&
            svc.Gestures.TryGetValue(id, out var g) && !string.IsNullOrWhiteSpace(g))
            return g;
        return FixedByKey.TryGetValue(key, out var fixedG) ? fixedG : string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Avalonia's reflection indexer binding re-reads on the canonical indexer name (see LocProxy).
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    public void Invalidate() => PropertyChanged?.Invoke(this, IndexerChanged);
}
