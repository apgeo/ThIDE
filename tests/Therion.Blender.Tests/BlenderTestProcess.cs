// Runs a real Blender for the opt-in tests and returns its exit code and combined output.

using System.Diagnostics;
using System.Text;

namespace Therion.Blender.Tests;

internal static class BlenderTestProcess
{
    /// <summary>Exit code reported when the run was killed for exceeding its time limit.</summary>
    public const int TimedOut = -2;

    /// <summary>Starts <paramref name="blender"/> in the background with factory settings and
    /// <paramref name="arguments"/> after them. <c>--python-exit-code 64</c> makes an uncaught
    /// Python exception fail the process instead of exiting 0.</summary>
    public static async Task<(int ExitCode, string Log)> RunAsync(
        string blender, IEnumerable<string> arguments, string workDir, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo(blender)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        };
        foreach (var arg in new[] { "-b", "--factory-startup", "--python-exit-code", "64" }.Concat(arguments))
            psi.ArgumentList.Add(arg);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        var log = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            lock (log) log.AppendLine($"[blender] TIMEOUT after {timeoutSeconds}s — killed.");
            return (TimedOut, log.ToString());
        }
        return (process.ExitCode, log.ToString());
    }

    /// <summary>The first meaningful Python exception line of a log (e.g. "TypeError: …"), for
    /// compact failure messages; "?" when the log holds no traceback.</summary>
    public static string ExceptionLine(string log)
    {
        string[] lines = log.Split('\n');
        int start = Array.FindIndex(lines, l => l.StartsWith("Traceback", StringComparison.Ordinal));
        if (start < 0) return "?";
        // The exception line is the first non-indented line after the frames of the traceback
        // block; Blender prints its own noise after it, so stop at the first hit.
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length > 0 && !char.IsWhiteSpace(lines[i][0]))
                return line;
        }
        return lines[start].TrimEnd();
    }
}
