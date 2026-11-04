// ThIDE's per-user data folder as this platform names it, for help text that points at it
// (%AppData% exists only on Windows; Linux and macOS use the XDG/.config location).

using System;
using System.IO;

namespace ThIDE.Services;

public static class UserDataFolder
{
    /// <summary>The folder holding settings, plugins, the MCP endpoint file and the like.</summary>
    public static string FullPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(appData, "ThIDE");
        }
    }
}
