// Windows IFileAssociationService: registers a per-user ProgId + open command under
// HKCU\Software\Classes and points each extension's default at it. HKCU needs no admin rights.
// Note: on Windows 10/11 an existing Explorer "UserChoice" (set when the user previously picked a
// default app) is hash-protected and can't be overwritten programmatically, so this makes ThIDE
// the default only when the user hasn't already chosen another app — otherwise it just registers the
// app so it appears under "Open with". This whole type is Windows-only and instantiated solely by the
// factory on Windows.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ThIDE.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileAssociationService : IFileAssociationService
{
    private const string ClassesRoot = @"Software\Classes";
    private readonly string _exePath;

    public WindowsFileAssociationService() => _exePath = FileAssociationCatalog.ExecutablePath();

    public bool IsSupported => !string.IsNullOrEmpty(_exePath);

    public IReadOnlyList<string> SupportedExtensions => FileAssociationCatalog.Extensions();

    private static string ProgId(string ext) => "ThIDE" + ext; // e.g. "ThIDE.th"

    public FileAssociationInfo GetInfo(string extension)
    {
        var ext = FileAssociationCatalog.Normalize(extension);
        var desc = FileAssociationCatalog.DescriptionFor(ext);
        try
        {
            using var extKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}");
            var current = extKey?.GetValue(null) as string;   // the ext's default ProgId
            if (string.IsNullOrEmpty(current))
                return new(ext, desc, FileAssociationState.NotAssociated, null);
            if (string.Equals(current, ProgId(ext), StringComparison.OrdinalIgnoreCase))
                return new(ext, desc, FileAssociationState.Associated, "ThIDE");
            return new(ext, desc, FileAssociationState.AssociatedWithOther, FriendlyName(current) ?? current);
        }
        catch { return new(ext, desc, FileAssociationState.Unknown, null); }
    }

    public bool Associate(string extension)
    {
        var ext = FileAssociationCatalog.Normalize(extension);
        if (!IsSupported || ext.Length < 2) return false;
        try
        {
            var progId = ProgId(ext);
            using (var progKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}"))
            {
                progKey.SetValue(null, FileAssociationCatalog.DescriptionFor(ext));
                using (var icon = progKey.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{_exePath}\",0");
                using (var cmd = progKey.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, $"\"{_exePath}\" \"%1\"");
            }
            using (var extKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}"))
                extKey.SetValue(null, progId);
            NotifyShell();
            return true;
        }
        catch { return false; }
    }

    public bool Unassociate(string extension)
    {
        var ext = FileAssociationCatalog.Normalize(extension);
        try
        {
            // Clear the ext's default only if it points at us, then remove our ProgId tree.
            using (var extKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}", writable: true))
            {
                if (extKey?.GetValue(null) as string is { } cur &&
                    string.Equals(cur, ProgId(ext), StringComparison.OrdinalIgnoreCase))
                    extKey.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{ProgId(ext)}", throwOnMissingSubKey: false);
            NotifyShell();
            return true;
        }
        catch { return false; }
    }

    private static string? FriendlyName(string progId)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{progId}")
                          ?? Registry.ClassesRoot.OpenSubKey(progId);
            return k?.GetValue(null) as string;
        }
        catch { return null; }
    }

    // Ask Explorer to refresh its association/icon cache (SHCNE_ASSOCCHANGED).
    private static void NotifyShell()
    {
        try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); } catch { /* best-effort */ }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
