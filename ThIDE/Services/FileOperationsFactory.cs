// Factory Pattern (Plan: cross-platform refactor) selecting the IFileOperations
// implementation for the host OS. Centralising the OperatingSystem.IsWindows()
// branch here means the Windows-only P/Invoke type (WindowsFileOperations) is never
// instantiated — and never JIT-loaded — on macOS / Linux.

namespace ThIDE.Services;

public static class FileOperationsFactory
{
    public static IFileOperations Create()
        => OperatingSystem.IsWindows()
            ? new WindowsFileOperations()   // Recycle Bin via shell32 SHFileOperation
            : new UnixFileOperations();     // freedesktop / macOS trash, permanent fallback
}
