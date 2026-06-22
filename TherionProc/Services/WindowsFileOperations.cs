// Windows IFileOperations implementation (Plan: cross-platform refactor). Delete sends
// the item to the Recycle Bin (undoable) via shell32 SHFileOperation. This whole type is
// [SupportedOSPlatform("windows")] and is only ever instantiated by FileOperationsFactory
// on Windows, so the P/Invoke never loads on macOS / Linux.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TherionProc.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileOperations : IFileOperations
{
    public bool DeleteIsUndoable => true;

    public string DeleteActionLabel => "Move to Recycle Bin";

    /// <summary>Deletes a file/folder, sending it to the Recycle Bin.</summary>
    public bool Delete(string path)
    {
        try { return RecycleBin(path); }
        catch { return false; }
    }

    [SupportedOSPlatform("windows")]
    private static bool RecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // double-null terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
