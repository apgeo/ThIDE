// Factory Pattern (Plan: cross-platform refactor) selecting the native context-menu
// implementation for the host OS, so the Windows-only shell interop type is never
// instantiated on macOS / Linux.

namespace TherionProc.Services;

public static class NativeContextMenuServiceFactory
{
    public static INativeContextMenuService Create()
        => OperatingSystem.IsWindows()
            ? new WindowsNativeContextMenuService()   // real shell32 IContextMenu
            : new NullNativeContextMenuService();      // no native menu elsewhere
}
