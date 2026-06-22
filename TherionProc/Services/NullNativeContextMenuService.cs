// No-op INativeContextMenuService for platforms without a native shell context menu
// (macOS / Linux) — Plan: cross-platform refactor. IsSupported is false so the UI hides
// the corresponding menu entry rather than offering an action that does nothing.

using System;

namespace TherionProc.Services;

public sealed class NullNativeContextMenuService : INativeContextMenuService
{
    public bool IsSupported => false;

    public bool TryShow(IntPtr ownerHandle, string path) => false;
}
