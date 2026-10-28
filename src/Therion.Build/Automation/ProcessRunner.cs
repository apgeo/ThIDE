// Runs one command for hooks, tasks and the batch build: output streamed line by line, a timeout,
// cancellation that kills the whole process tree, and stdin closed so nothing can sit waiting for a
// key press. Modelled on TherionCompiler's handling of the same problems.

using System.Diagnostics;

namespace Therion.Build.Automation;

/// <param name="UseShell">Run <paramref name="FileName"/> as a shell command line (<c>sh -c</c> /
/// <c>cmd /c</c>); <paramref name="Args"/> are then ignored.</param>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Args,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null,
    bool UseShell = false);

public sealed record ProcessOutcome(int ExitCode, bool TimedOut, bool Cancelled, TimeSpan Elapsed, string? StartError = null)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled && StartError is null;
}

public readonly record struct ProcessLine(string Text, bool IsError);

/// <summary>
/// An <see cref="IProgress{T}"/> that calls back on the reporting thread, in order. (The BCL
/// Progress posts each report to the thread pool when there is no synchronisation context, which
/// reorders lines and can deliver them after the run is over.)
/// </summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    private readonly object _gate = new();
    public void Report(T value) { lock (_gate) report(value); }
}

public static class ProcessRunner
{
    /// <summary>
    /// Starts the process and waits for it. Never throws for the process's own failures: a command
    /// that cannot start comes back with <see cref="ProcessOutcome.StartError"/>.
    /// </summary>
    public static async Task<ProcessOutcome> RunAsync(ProcessSpec spec, IProgress<ProcessLine>? lines = null, CancellationToken ct = default)
    {
        var psi = StartInfo(spec);
        var clock = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return new ProcessOutcome(-1, false, false, clock.Elapsed, "the process did not start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new ProcessOutcome(-1, false, false, clock.Elapsed, ex.Message);
        }
        try { proc.StandardInput.Close(); } catch { /* already gone */ }

        using var timeout = spec.Timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var outTask = Pump(proc.StandardOutput, false, lines, linked.Token);
        var errTask = Pump(proc.StandardError, true, lines, linked.Token);

        bool stopped = false;
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            try { proc.Kill(entireProcessTree: true); } catch { /* exited meanwhile */ }
            try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }

        // A child that inherited the pipes may hold them open after the process exits: cap the drain.
        try { await Task.WhenAll(outTask, errTask).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException) { }

        bool timedOut = stopped && timeout.IsCancellationRequested && !ct.IsCancellationRequested;
        bool cancelled = stopped && ct.IsCancellationRequested;
        int code = proc.HasExited ? proc.ExitCode : -1;
        return new ProcessOutcome(stopped ? -1 : code, timedOut, cancelled, clock.Elapsed);
    }

    /// <summary>The start info for a spec (exposed for tests and for callers that need to adjust it).</summary>
    public static ProcessStartInfo StartInfo(ProcessSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (spec.UseShell && OperatingSystem.IsWindows())
        {
            // cmd does not undo the \" escaping ArgumentList would apply to the quotes inside the
            // line, so the line goes in raw; /s makes cmd strip only the outer pair of quotes.
            psi.FileName = "cmd.exe";
            psi.Arguments = "/d /s /c \"" + spec.FileName + "\"";
        }
        else if (spec.UseShell)
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(spec.FileName);
        }
        else
        {
            psi.FileName = spec.FileName;
            foreach (var a in spec.Args) psi.ArgumentList.Add(a);
        }
        if (spec.WorkingDirectory is { Length: > 0 } wd) psi.WorkingDirectory = wd;
        if (spec.Environment is { } env)
            foreach (var (k, v) in env) psi.Environment[k] = v;
        return psi;
    }

    private static async Task Pump(StreamReader reader, bool isError, IProgress<ProcessLine>? lines, CancellationToken ct)
    {
        try
        {
            string? text;
            while ((text = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                lines?.Report(new ProcessLine(text, isError));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* pipe closed */ }
    }
}

/// <summary>Quoting a value for the shell <see cref="ProcessRunner"/> uses on this platform.</summary>
public static class ShellQuote
{
    /// <summary>
    /// One shell word that expands to exactly <paramref name="value"/>: single quotes for sh (an
    /// embedded quote closed, escaped and reopened), double quotes for cmd (embedded quotes doubled).
    /// </summary>
    public static string ForArgument(string value, bool windows)
    {
        if (windows) return "\"" + value.Replace("\"", "\"\"") + "\"";
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    public static string ForArgument(string value) => ForArgument(value, OperatingSystem.IsWindows());
}
