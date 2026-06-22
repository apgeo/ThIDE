// Windows INativeContextMenuService implementation (Plan: cross-platform refactor).
// Thin wrapper around the shell32 IContextMenu interop in WindowsShellContextMenu.
// [SupportedOSPlatform("windows")] and only built by NativeContextMenuServiceFactory
// on Windows, so the P/Invoke never loads elsewhere.

using System;
using System.Runtime.Versioning;

namespace TherionProc.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsNativeContextMenuService : INativeContextMenuService
{
    public bool IsSupported => true;

    public bool TryShow(IntPtr ownerHandle, string path)
    {
        if (ownerHandle == IntPtr.Zero || string.IsNullOrEmpty(path)) return false;
        try { WindowsShellContextMenu.Show(ownerHandle, path); return true; }
        catch { return false; } // best-effort: no shell menu
    }
}
