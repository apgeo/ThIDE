// The headless Blender runner (BA-B10, doc 04). Launches `blender -b --factory-startup
// --python render.py`, tees every output line to job.log, feeds the 3-tier progress parser,
// honours cancellation (process-tree kill), and classifies the outcome into the failure
// taxonomy. The process is behind IBlenderProcessLauncher so the runner is unit-testable
// with a fake; the real launcher is a thin System.Diagnostics.Process wrapper.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Therion.Blender.Execution;

/// <summary>What to render: the generated script, its job folder, the expected frame count,
/// and the output spec (drives the disk preflight and the output collection).</summary>
public sealed record RenderJob(
    string ScriptPath,
    string WorkingDirectory,
    int FrameCount,
    OutputSpec Output);

/// <summary>A running Blender process: merged output lines, exit code, and a tree kill.</summary>
public interface IBlenderProcess : IDisposable
{
    /// <summary>Yields each stdout/stderr line as it arrives; completes when the process exits.</summary>
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken ct);

    /// <summary>The exit code — valid only after <see cref="ReadLinesAsync"/> completes.</summary>
    int ExitCode { get; }

    /// <summary>Kills the process and its whole child tree (cancellation).</summary>
    void KillTree();
}

/// <summary>Starts a Blender process. Injected so the runner is testable without Blender.</summary>
public interface IBlenderProcessLauncher
{
    IBlenderProcess Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory);
}

/// <summary>Runs a generated script through headless Blender.</summary>
public sealed class BlenderRunner
{
    private const int TailLines = 40;

    private readonly IBlenderProcessLauncher _launcher;
    private readonly Func<string, long?> _freeSpaceProbe;

    public BlenderRunner(IBlenderProcessLauncher launcher, Func<string, long?>? freeSpaceProbe = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
        _freeSpaceProbe = freeSpaceProbe ?? DiskPreflight.TryGetFreeBytes;
    }

    /// <summary>Runs the job, reporting progress and honouring cancellation. Never throws for a
    /// render failure — the outcome (including cancellation) comes back in the result.</summary>
    public async Task<RenderResult> RunAsync(
        BlenderInstallation blender,
        RenderJob job,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blender);
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        // Disk preflight: fail fast rather than mid-render (skipped when free space is unknown).
        long estimate = DiskPreflight.EstimateBytes(job.FrameCount, job.Output.Width, job.Output.Height, job.Output.Kind);
        if (_freeSpaceProbe(job.WorkingDirectory) is { } free && !DiskPreflight.IsSufficient(estimate, free))
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = RenderFailureKind.DiskSpace,
                ErrorMessage = $"Not enough free disk space: the render needs about {estimate / 1_000_000} MB, but only {free / 1_000_000} MB is free.",
                Duration = stopwatch.Elapsed,
            };

        progress?.Report(new RenderProgress(RenderPhase.Rendering, "Starting Blender…")); // tier-3 spinner
        string jobLogPath = Path.Combine(job.WorkingDirectory, "job.log");
        var args = new[] { "-b", "--factory-startup", "--python", job.ScriptPath };
        var parser = new RenderProgressParser(job.FrameCount);

        IBlenderProcess process;
        try
        {
            process = _launcher.Start(blender.Path, args, job.WorkingDirectory);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = RenderFailureKind.Crashed,
                ErrorMessage = "Could not start Blender. A Microsoft Store install can't be used headlessly — its executable is " +
                               "access-restricted and its launcher returns no output — so install the standard Blender from " +
                               "blender.org and set its path in Preferences ▸ External tools: " + ex.Message,
                JobLogPath = null,
                Duration = stopwatch.Elapsed,
            };
        }

        bool cancelled = false;
        var tail = new Queue<string>(TailLines);
        int exitCode = -1;
        try
        {
            using var registration = ct.Register(() =>
            {
                cancelled = true;
                process.KillTree();
            });
            await using (var log = CreateLog(jobLogPath))
            {
                try
                {
                    await foreach (var line in process.ReadLinesAsync(ct).ConfigureAwait(false))
                    {
                        if (log is not null) await log.WriteLineAsync(line).ConfigureAwait(false);
                        KeepTail(tail, line);
                        if (parser.Consume(line) is { } tick) progress?.Report(tick);
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                if (log is not null) await log.FlushAsync().ConfigureAwait(false);
            }
            try { exitCode = process.ExitCode; } catch { exitCode = -1; }
        }
        finally
        {
            process.Dispose();
        }

        stopwatch.Stop();
        return Classify(parser, cancelled || ct.IsCancellationRequested, exitCode, tail, jobLogPath, job, stopwatch.Elapsed, progress);
    }

    private static RenderResult Classify(
        RenderProgressParser parser, bool cancelled, int exitCode, Queue<string> tail,
        string jobLogPath, RenderJob job, TimeSpan duration, IProgress<RenderProgress>? progress)
    {
        var warnings = new List<string>(parser.Warnings);
        int frames = parser.FrameCount ?? job.FrameCount;
        var outputs = ImmutableArray<string>.Empty;

        RenderFailureKind kind;
        string? error;
        bool ok = false;

        if (cancelled || parser.Cancelled)
        {
            kind = RenderFailureKind.Cancelled;
            error = "The render was cancelled.";
        }
        else if (parser.Error is { } scriptError)
        {
            kind = RenderFailureKind.ScriptError;
            error = scriptError;
        }
        else if (!parser.Done || exitCode != 0)
        {
            kind = RenderFailureKind.Crashed;
            error = $"Blender exited with code {exitCode} before finishing." +
                    (tail.Count > 0 ? "\nLast output:\n" + string.Join("\n", tail) : "");
        }
        else
        {
            // Render finished — read back and verify the output files (BA-B11).
            progress?.Report(new RenderProgress(RenderPhase.CollectingOutputs, "Collecting outputs", Device: parser.Device));
            var collection = OutputCollector.Collect(job.Output, frames);
            if (!collection.HasOutputs)
            {
                kind = RenderFailureKind.NoOutput;
                error = "Blender reported success but wrote no output files." +
                        (collection.Problem is { } p ? " " + p : "");
            }
            else
            {
                ok = true;
                kind = RenderFailureKind.None;
                error = null;
                outputs = collection.Files.Select(f => f.Path).ToImmutableArray();
                if (!collection.Verified && collection.Problem is { } problem) warnings.Add(problem);
            }
        }

        progress?.Report(new RenderProgress(
            RenderPhase.Done, ok ? "Render complete" : "Render did not finish", ok ? 1.0 : null, Device: parser.Device));

        return new RenderResult
        {
            Succeeded = ok,
            FailureKind = kind,
            ErrorMessage = error,
            OutputPaths = outputs,
            Device = parser.Device,
            FrameCount = frames,
            JobLogPath = jobLogPath,
            Duration = duration,
            Warnings = warnings,
        };
    }

    private static StreamWriter? CreateLog(string path)
    {
        try { return new StreamWriter(path, append: false) { AutoFlush = false }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void KeepTail(Queue<string> tail, string line)
    {
        tail.Enqueue(line);
        while (tail.Count > TailLines) tail.Dequeue();
    }
}

/// <summary>Real <see cref="IBlenderProcessLauncher"/> over System.Diagnostics.Process.</summary>
public sealed class RealBlenderProcessLauncher : IBlenderProcessLauncher
{
    public IBlenderProcess Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        psi.Environment["PYTHONIOENCODING"] = "utf-8"; // non-ASCII station names (R-08)

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        return new RealBlenderProcess(process);
    }

    private sealed class RealBlenderProcess : IBlenderProcess
    {
        private readonly Process _process;
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
        private int _openStreams = 2; // stdout + stderr

        public RealBlenderProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += OnData;
            _process.ErrorDataReceived += OnData;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                if (Interlocked.Decrement(ref _openStreams) == 0) _lines.Writer.TryComplete();
            }
            else
            {
                _lines.Writer.TryWrite(e.Data);
            }
        }

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var line in _lines.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return line;
            // Both streams closed ⇒ the process is finishing; make ExitCode valid.
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public int ExitCode => _process.ExitCode;

        public void KillTree()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* already gone */ }
        }

        public void Dispose() => _process.Dispose();
    }
}
