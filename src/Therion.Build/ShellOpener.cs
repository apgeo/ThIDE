// Implementation Plan §9bis.4 — cross-platform shell-open for viewers.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Therion.Build;

/// <summary>Opens a file with the OS default handler.</summary>
public interface IShellOpener
{
    bool Open(string path);
    bool RevealInFileManager(string path);
}

public sealed class ShellOpener : IShellOpener
{
    public bool Open(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", QuoteIfNeeded(path));
                return true;
            }
            Process.Start("xdg-open", QuoteIfNeeded(path));
            return true;
        }
        catch { return false; }
    }

    public bool RevealInFileManager(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
                return true;
            }
            var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
            return Open(dir);
        }
        catch { return false; }
    }

    private static string QuoteIfNeeded(string s)
        => s.Contains(' ') ? "\"" + s + "\"" : s;
}
