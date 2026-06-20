// Implementation Plan §9bis.5a / Decision #29 — Settings ? Keyboard sub-panel.
// One row per shell command; user can edit gesture text inline.

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Processing.Abstractions;

namespace TherionProc.ViewModels;

public sealed partial class KeyboardShortcutRow : ObservableObject
{
    private readonly IKeyboardShortcutService _service;

    public string CommandId { get; }
    public string DefaultGesture { get; }

    [ObservableProperty] private string _gesture;

    public KeyboardShortcutRow(IKeyboardShortcutService service, string commandId, string gesture, string defaultGesture)
    {
        _service = service;
        CommandId = commandId;
        _gesture = gesture;
        DefaultGesture = defaultGesture;
    }

    partial void OnGestureChanged(string value) => _service.Set(CommandId, value ?? string.Empty);

    [RelayCommand]
    private void ResetToDefault() => _service.ResetToDefault(CommandId);
}

public partial class KeyboardShortcutsViewModel : ViewModelBase
{
    private readonly IKeyboardShortcutService _service;

    [ObservableProperty] private IReadOnlyList<KeyboardShortcutRow> _rows = Array.Empty<KeyboardShortcutRow>();

    public KeyboardShortcutsViewModel() : this(new Services.JsonKeyboardShortcutService()) { }

    public KeyboardShortcutsViewModel(IKeyboardShortcutService service)
    {
        _service = service;
        _service.GesturesChanged += (_, _) => Refresh();
        Refresh();
    }

    [RelayCommand]
    private void ResetAll() => _service.ResetAllToDefaults();

    private void Refresh()
    {
        Rows = _service.Defaults.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(id => new KeyboardShortcutRow(
                _service, id,
                _service.Gestures.TryGetValue(id, out var g) ? g : string.Empty,
                _service.Defaults.TryGetValue(id, out var d) ? d : string.Empty))
            .ToList();
    }
}
