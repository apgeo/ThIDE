// "Open in Blender GUI" (BA-B13): launch the desktop Blender on a generated script so the user
// can inspect/tweak the scene interactively — `blender --python <script>` WITHOUT -b, so a window
// opens. Behind an interface so the ViewModel is testable with a fake (no process spawned).

using System;
using System.Diagnostics;
using Therion.Blender.Execution;

namespace ThIDE.Services;

/// <summary>Opens a generated script in the interactive Blender GUI.</summary>
public interface IBlenderGuiLauncher
{
    /// <summary>Launches Blender's GUI running <paramref name="scriptPath"/>. Returns false when
    /// no usable Blender is found or the launch fails (the caller surfaces a not-found message).</summary>
    bool Launch(string scriptPath);
}

/// <summary>Real launcher: locates Blender (honouring the Preferences override) and starts its GUI.</summary>
public sealed class BlenderGuiLauncher : IBlenderGuiLauncher
{
    private readonly BlenderLocator _locator;
    private readonly Func<string?> _overridePath;

    public BlenderGuiLauncher(BlenderLocator locator, Func<string?> overridePath)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _overridePath = overridePath ?? throw new ArgumentNullException(nameof(overridePath));
    }

    public bool Launch(string scriptPath)
    {
        var located = _locator.Locate(_overridePath());
        if (!located.IsUsable) return false;
        try
        {
            var psi = new ProcessStartInfo(located.Installation!.Path)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            psi.ArgumentList.Add("--python");
            psi.ArgumentList.Add(scriptPath);
            return Process.Start(psi) is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            return false; // e.g. a Microsoft Store install that can't be launched by its raw WindowsApps path
        }
    }
}
