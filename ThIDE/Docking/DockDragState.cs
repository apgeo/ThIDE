// Whether a panel is being dragged by the dock right now. Dock.Avalonia sets IsDraggingDock on the
// DockControl that runs the drag once the pointer has moved past the drag threshold, and clears it
// after the drop has been carried out (a drop outside every target floats the panel first).
// The 3D viewer hides its native view while it is set; the factory uses it to tell a dragged-out
// float from one asked for from a menu.

using System;
using Avalonia;
using Dock.Avalonia.Controls;

namespace ThIDE.Docking;

internal static class DockDragState
{
    static DockDragState() =>
        DockControl.IsDraggingDockProperty.Changed.AddClassHandler<DockControl>((_, e) => Set(e.NewValue is true));

    /// <summary>True between the start of a panel drag and the end of its drop.</summary>
    public static bool IsDragging { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="IsDragging"/> changes.</summary>
    public static event EventHandler<bool>? Changed;

    /// <summary>Starts watching (idempotent): the watch only begins once the type is first used.</summary>
    public static void EnsureWatching() { }

    internal static void Set(bool dragging)
    {
        if (IsDragging == dragging) return;
        IsDragging = dragging;
        Changed?.Invoke(null, dragging);
    }
}
