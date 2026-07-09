// Reactive binding source behind the {l:Gesture CommandId} markup extension: the KeyGesture
// currently bound to a shell command, or null when the command is unbound. Menu items display
// their shortcut via MenuItem.InputGesture, which used to be a hardcoded XAML literal — so a
// remapped command kept advertising its old chord. Binding through this proxy makes the menu
// labels follow IKeyboardShortcutService live, the same way {l:Tip} does for toolbar tooltips.

using System;
using System.ComponentModel;
using Avalonia.Input;
using Therion.Processing.Abstractions;

namespace ThIDE.Resources;

public sealed class GestureProxy : INotifyPropertyChanged
{
    public static GestureProxy Instance { get; } = new();
    private GestureProxy() { }

    private IKeyboardShortcutService? _service;

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

    /// <summary>
    /// The gesture bound to <paramref name="commandId"/>, or null when it is unbound, unparseable,
    /// or the service has not been attached yet (design time). A null simply hides the menu label.
    /// </summary>
    public KeyGesture? this[string commandId]
    {
        get
        {
            if (string.IsNullOrEmpty(commandId) || _service is not { } svc) return null;
            if (!svc.Gestures.TryGetValue(commandId, out var text) || string.IsNullOrWhiteSpace(text))
                return null;
            try { return KeyGesture.Parse(text); } catch { return null; }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Avalonia's reflection indexer binding re-reads on this exact name (see LocProxy).
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    public void Invalidate() => PropertyChanged?.Invoke(this, IndexerChanged);
}
