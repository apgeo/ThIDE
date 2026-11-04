// Why a file could not be read or written, in the UI language. The .NET message ("Access to the path
// '/home/…/side.th' is denied.") is English on every system and names a full path; the user gets a
// short reason in their language instead, and the exception itself goes behind "Copy details".

using System;
using System.IO;

namespace ThIDE.Resources;

public static class IoErrorText
{
    // Win32 error codes (in the low word of an HRESULT) and the errno values .NET puts in HResult on Unix.
    private const int WinSharingViolation = 0x20, WinLockViolation = 0x21, WinHandleDiskFull = 0x27,
                      WinFileExists = 0x50, WinAlreadyExists = 0xB7, WinDiskFull = 0x70;
    private const int ErrnoBusy = 16, ErrnoExists = 17, ErrnoNoSpace = 28, ErrnoReadOnlyFs = 30;

    /// <summary>A lower-case reason clause for <paramref name="ex"/>, e.g. "the disk is full".</summary>
    public static string Reason(Exception ex) => Tr.Get(ReasonKey(ex));

    internal static string ReasonKey(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException: return "IoErr_Denied";
            case DirectoryNotFoundException: return "IoErr_NoFolder";
            case FileNotFoundException: return "IoErr_NoFile";
            case PathTooLongException: return "IoErr_PathTooLong";
            case IOException io when OperatingSystem.IsWindows():
                // A Windows HRESULT (0x8007xxxx) carries the Win32 error code in its low word.
                if ((io.HResult & unchecked((int)0xFFFF0000)) != unchecked((int)0x80070000)) return "IoErr_Other";
                return (io.HResult & 0xFFFF) switch
                {
                    WinDiskFull or WinHandleDiskFull => "IoErr_DiskFull",
                    WinSharingViolation or WinLockViolation => "IoErr_InUse",
                    WinFileExists or WinAlreadyExists => "IoErr_Exists",
                    _ => "IoErr_Other",
                };
            case IOException io:
                return io.HResult switch
                {
                    ErrnoNoSpace => "IoErr_DiskFull",
                    ErrnoBusy => "IoErr_InUse",
                    ErrnoExists => "IoErr_Exists",
                    ErrnoReadOnlyFs => "IoErr_Denied",
                    _ => "IoErr_Other",
                };
            default: return "IoErr_Other";
        }
    }
}
