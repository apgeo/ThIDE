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
                // Blender's GUI has no visible console for a --python script, so a failed
                // scene build looks like "nothing happened". Capture stdout/stderr into a
                // log beside the script so the traceback is inspectable afterwards.
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(scriptPath) ?? string.Empty,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8"; // non-ASCII station names (R-08)
            // Same environment as the headless runner: user addons/startup files must not
            // break the scene build (and Blender won't auto-save prefs over the user's own).
            psi.ArgumentList.Add("--factory-startup");
            psi.ArgumentList.Add("--python");
            psi.ArgumentList.Add(scriptPath);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var log = new StreamWriter(scriptPath + ".blender.log", append: false) { AutoFlush = true };
            void Write(string? data)
            {
                if (data is null) return;
                lock (log)
                {
                    try { log.WriteLine(data); }
                    catch (ObjectDisposedException) { /* late line after exit */ }
                }
            }
            process.OutputDataReceived += (_, e) => Write(e.Data);
            process.ErrorDataReceived += (_, e) => Write(e.Data);
            process.Exited += (_, _) =>
            {
                lock (log) { try { log.Dispose(); } catch (System.IO.IOException) { } }
                process.Dispose();
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            return false; // e.g. a Microsoft Store install that can't be launched by its raw WindowsApps path
        }
    }
}
