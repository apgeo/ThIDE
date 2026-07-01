// scripting / macro hooks. Runs a user-configured shell command on key events
// (file opened, file saved, build started). Off-by-feature when EnableScriptHooks is false or no
// command is configured. Commands run detached and best-effort — a failing hook never blocks the
// app. `{file}` in the command is substituted with the relevant path.

using System;
using System.Diagnostics;
using System.IO;

namespace TherionProc.Services;

public enum ScriptHookEvent { Open, Save, Build }

public interface IScriptHookService
{
    /// <summary>Runs the hook configured for <paramref name="ev"/> (no-op when disabled/unset).</summary>
    void Run(ScriptHookEvent ev, string? filePath = null);
}

public sealed class ScriptHookService : IScriptHookService
{
    private readonly IAppSettingsService _settings;
    private readonly ILogService? _log;

    public ScriptHookService(IAppSettingsService settings, ILogService? log = null)
    {
        _settings = settings;
        _log = log;
    }

    public void Run(ScriptHookEvent ev, string? filePath = null)
    {
        var s = _settings.Current;
        if (!s.EnableScriptHooks) return;

        var command = ev switch
        {
            ScriptHookEvent.Open => s.HookOnOpen,
            ScriptHookEvent.Save => s.HookOnSave,
            ScriptHookEvent.Build => s.HookOnBuild,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(command)) return;

        command = command.Replace("{file}", filePath ?? string.Empty);
        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsWindows()) { psi.FileName = "cmd.exe"; psi.ArgumentList.Add("/c"); }
            else { psi.FileName = "/bin/sh"; psi.ArgumentList.Add("-c"); }
            psi.ArgumentList.Add(command);

            var dir = TryDir(filePath);
            if (dir is not null) psi.WorkingDirectory = dir;

            Process.Start(psi);   // detached — we don't await the hook
            _log?.Verbose($"Ran {ev} hook: {command}");
        }
        catch (Exception ex)
        {
            _log?.Warning($"{ev} hook failed: {ex.Message}");
        }
    }

    private static string? TryDir(string? filePath)
    {
        try { return string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(Path.GetFullPath(filePath)); }
        catch { return null; }
    }
}
