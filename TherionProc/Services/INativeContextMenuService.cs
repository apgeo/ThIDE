// Cross-platform abstraction (Plan: cross-platform refactor) over the OS native shell
// context menu for a filesystem item. Only Windows currently provides one (via the
// shell32 IContextMenu P/Invoke in WindowsShellContextMenu); other platforms get a
// no-op implementation. Consuming this through DI keeps the Win32 interop out of the
// views and lets the UI hide the menu entry where it is unsupported.

using System;

namespace TherionProc.Services;

public interface INativeContextMenuService
{
    /// <summary>True when this platform exposes a native shell context menu.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Shows the OS native shell context menu for <paramref name="path"/> at the cursor.
    /// Best-effort; returns false (and does nothing) on unsupported platforms or failure.
    /// </summary>
    bool TryShow(IntPtr ownerHandle, string path);
}
