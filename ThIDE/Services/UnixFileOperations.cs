// Cross-platform (macOS / Linux) IFileOperations implementation (Plan: cross-platform
// refactor). There is no managed API for the desktop trash, so we shell out to the
// platform's standard helper and fall back to a permanent delete when none is present:
//   * macOS  — Finder via `osascript` (always available on a desktop session).
//   * Linux  — `gio trash` (GLib/GVfs), then `trash-put`/`trash` (trash-cli), per the
//              freedesktop.org Trash spec.
// Trash-backend availability is probed once (a cheap PATH scan, no process spawn) so the
// UI can label the action correctly. Everything is best-effort; failures return false.

using System;
using System.Diagnostics;
using System.IO;

namespace ThIDE.Services;

public sealed class UnixFileOperations : IFileOperations
{
    private readonly bool _useFinder;          // macOS: route through Finder
    private readonly string? _trashCommand;    // resolved CLI absolute path, or null
    private readonly bool _gioStyle;           // command needs a "trash" sub-command (gio)

    public UnixFileOperations()
    {
        if (OperatingSystem.IsMacOS())
        {
            _useFinder = true;
        }
        else
        {
            // Prefer GLib's `gio trash`; otherwise trash-cli's `trash-put` / `trash`.
            if (FindOnPath("gio") is { } gio) { _trashCommand = gio; _gioStyle = true; }
            else if (FindOnPath("trash-put") is { } tp) { _trashCommand = tp; }
            else if (FindOnPath("trash") is { } tr) { _trashCommand = tr; }
        }
    }

    public bool DeleteIsUndoable => _useFinder || _trashCommand is not null;

    public string DeleteActionLabel => DeleteIsUndoable ? "Move to Trash" : "Delete Permanently";

    public bool Delete(string path)
    {
        try
        {
            if (_useFinder && TrashViaFinder(path)) return true;
            if (_trashCommand is not null && TrashViaCommand(path)) return true;
            return PermanentDelete(path); // last resort when no trash backend is usable
        }
        catch { return false; }
    }

    private bool TrashViaCommand(string path)
    {
        var psi = NewSilentProcess(_trashCommand!);
        if (_gioStyle) psi.ArgumentList.Add("trash");
        psi.ArgumentList.Add(path); // ArgumentList escapes per-OS — no manual quoting
        return RunToSuccess(psi, timeoutMs: 5000);
    }

    private static bool TrashViaFinder(string path)
    {
        // AppleScript string literal: escape backslashes then double-quotes.
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var script = $"tell application \"Finder\" to delete (POSIX file \"{escaped}\" as alias)";
        var psi = NewSilentProcess("osascript");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);
        return RunToSuccess(psi, timeoutMs: 10000);
    }

    private static bool PermanentDelete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
        return true;
    }

    private static bool RunToSuccess(ProcessStartInfo psi, int timeoutMs)
    {
        using var p = Process.Start(psi);
        if (p is null) return false;
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return false; }
        return p.ExitCode == 0;
    }

    private static ProcessStartInfo NewSilentProcess(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    // PATH lookup mirroring ExternalToolLocator — no process spawn, so it stays cheap
    // enough to run in the constructor.
    private static string? FindOnPath(string command)
    {
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(envPath)) return null;
        foreach (var dir in envPath.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var full = Path.Combine(dir, command);
                if (File.Exists(full)) return full;
            }
            catch { /* skip malformed PATH entries */ }
        }
        return null;
    }
}
