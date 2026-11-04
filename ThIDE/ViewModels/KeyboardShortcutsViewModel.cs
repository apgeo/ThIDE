// Implementation Plan §9bis.5a / Decision #29 — Settings ▸ Keyboard sub-panel.
// One row per shell command; the user records a gesture in the row's capture box. Edits are staged
// in the rows and reach the shortcut service only on Commit (Settings ▸ OK); Discard drops them.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed partial class KeyboardShortcutRow : ObservableObject
{
    private readonly KeyboardShortcutsViewModel _owner;
    private string _beforeConflict = string.Empty;

    public string CommandId { get; }
    /// <summary>The command's name in the UI language (the id when no translation exists).</summary>
    public string DisplayName { get; }
    public string DefaultGesture { get; }
    public string DefaultDisplay => KeyGestureText.Format(DefaultGesture);

    /// <summary>The staged gesture, in the stored spelling.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GestureDisplay))]
    private string _gesture;

    /// <summary>The other row that already has the gesture just recorded here, until the user decides.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict), nameof(ConflictText))]
    private KeyboardShortcutRow? _conflictWith;

    public string GestureDisplay => KeyGestureText.Format(Gesture);
    public bool HasConflict => ConflictWith is not null;
    public string ConflictText => ConflictWith is { } other ? string.Format(Tr.Get("Kb_ConflictFmt"), other.DisplayName) : string.Empty;

    internal KeyboardShortcutRow(KeyboardShortcutsViewModel owner, string commandId, string gesture, string defaultGesture)
    {
        _owner = owner;
        CommandId = commandId;
        DisplayName = Tr.Find("Kb_Cmd_" + commandId) ?? commandId;
        _gesture = gesture;
        DefaultGesture = defaultGesture;
    }

    /// <summary>
    /// Records <paramref name="gesture"/> ("" clears). A gesture another command already has is shown
    /// with the question "Already used by … — replace?" instead of being taken silently.
    /// </summary>
    public void Capture(string gesture)
    {
        gesture ??= string.Empty;
        var previous = HasConflict ? _beforeConflict : Gesture;
        ConflictWith = null;
        Gesture = gesture;
        if (_owner.FindOwner(gesture, except: this) is { } other)
        {
            _beforeConflict = previous;
            ConflictWith = other;
        }
    }

    /// <summary>Takes the gesture from the other command, which is left without one.</summary>
    [RelayCommand]
    private void ReplaceConflict()
    {
        if (ConflictWith is not { } other) return;
        other.Capture(string.Empty);
        ConflictWith = null;
    }

    /// <summary>Leaves the other command its gesture; this row goes back to what it had.</summary>
    [RelayCommand]
    private void KeepConflict()
    {
        if (!HasConflict) return;
        ConflictWith = null;
        Gesture = _beforeConflict;
    }

    [RelayCommand]
    private void ResetToDefault() => Capture(DefaultGesture);

    /// <summary>Drops an undecided conflict, keeping the gesture the row had before it.</summary>
    internal void DropConflict() => KeepConflict();
}

public partial class KeyboardShortcutsViewModel : ViewModelBase
{
    private readonly IKeyboardShortcutService _service;
    private IReadOnlyList<KeyboardShortcutRow> _allRows = Array.Empty<KeyboardShortcutRow>();
    private string _filter = string.Empty;
    private bool _committing;

    /// <summary>Rows currently shown, after applying the Preferences search filter (#11).</summary>
    [ObservableProperty] private IReadOnlyList<KeyboardShortcutRow> _rows = Array.Empty<KeyboardShortcutRow>();

    /// <summary>Designer-only: reads and writes a throw-away file, never the user's keyboard.json.</summary>
    public KeyboardShortcutsViewModel() : this(new Services.JsonKeyboardShortcutService(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ThIDE-design", "keyboard.json"))) { }

    public KeyboardShortcutsViewModel(IKeyboardShortcutService service)
    {
        _service = service;
        _service.GesturesChanged += (_, _) => { if (!_committing) Refresh(); };
        Refresh();
    }

    /// <summary>Asks before Reset all throws away the custom gestures (the view sets it; null resets without asking).</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    /// <summary>
    /// Stages every command's default gesture, after asking when any row has another one; nothing is
    /// saved until <see cref="Commit"/>.
    /// </summary>
    [RelayCommand]
    private async Task ResetAll()
    {
        int custom = _allRows.Count(r => !string.Equals(r.Gesture, r.DefaultGesture, StringComparison.Ordinal));
        if (custom > 0 && Confirm is { } confirm && !await confirm(string.Format(Tr.Get("Kb_ResetAllConfirm"), custom)).ConfigureAwait(true))
            return;
        foreach (var row in _allRows)
        {
            row.ConflictWith = null;
            row.Gesture = row.DefaultGesture;
        }
    }

    /// <summary>
    /// Saves the staged gestures that differ from the service's. A conflict still undecided keeps the
    /// old gestures on both commands, so OK never leaves one of them silently dead.
    /// </summary>
    public void Commit()
    {
        foreach (var row in _allRows.Where(r => r.HasConflict)) row.DropConflict();
        _committing = true;
        try
        {
            foreach (var row in _allRows)
            {
                var current = _service.Gestures.TryGetValue(row.CommandId, out var g) ? g : string.Empty;
                if (!string.Equals(current, row.Gesture, StringComparison.Ordinal))
                    _service.Set(row.CommandId, row.Gesture);
            }
        }
        finally { _committing = false; }
        Refresh();
    }

    /// <summary>Forgets the staged edits: the rows show the service's gestures again.</summary>
    public void Discard() => Refresh();

    /// <summary>True when a staged gesture differs from the saved one.</summary>
    public bool HasChanges => _allRows.Any(r =>
        !string.Equals(_service.Gestures.TryGetValue(r.CommandId, out var g) ? g : string.Empty, r.Gesture, StringComparison.Ordinal));

    /// <summary>Narrows the visible rows to commands whose name or id contains the query (#11).</summary>
    public void SetFilter(string? query)
    {
        _filter = query?.Trim() ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>The row, other than <paramref name="except"/>, whose staged gesture is <paramref name="gesture"/>.</summary>
    internal KeyboardShortcutRow? FindOwner(string gesture, KeyboardShortcutRow except) =>
        _allRows.FirstOrDefault(r => !ReferenceEquals(r, except) && KeyGestureText.Same(r.Gesture, gesture));

    private void Refresh()
    {
        _allRows = _service.Defaults.Keys
            .Select(id => new KeyboardShortcutRow(
                this, id,
                _service.Gestures.TryGetValue(id, out var g) ? g : string.Empty,
                _service.Defaults.TryGetValue(id, out var d) ? d : string.Empty))
            .OrderBy(r => r.DisplayName, StringComparer.Create(Tr.Culture, ignoreCase: true))
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter() =>
        Rows = string.IsNullOrEmpty(_filter)
            ? _allRows
            : _allRows.Where(r => r.CommandId.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                                  || Tr.Culture.CompareInfo.IndexOf(r.DisplayName, _filter, System.Globalization.CompareOptions.IgnoreCase) >= 0).ToList();
}
