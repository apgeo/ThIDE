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
    OutputSpec Output)
{
    /// <summary>Arguments for the script, passed after <c>--</c> on Blender's command line: for
    /// the player, <c>--plan</c> and the plan to play.</summary>
    public IReadOnlyList<string> ScriptArguments { get; init; } = [];

    /// <summary>Bytes that what follows this job will write beside what the job itself writes:
    /// the movie a second stage makes from this job's frames. The disk preflight counts them, so
    /// a render in two stages does not find the disk full after its long first one.</summary>
    public long ExtraBytes { get; init; }

    /// <summary>The name of the log file in <see cref="WorkingDirectory"/>.</summary>
    public string LogFileName { get; init; } = "job.log";
}

/// <summary>A running Blender process: merged output lines, exit code, and a tree kill.</summary>
public interface IBlenderProcess : IDisposable
{
    /// <summary>Yields each stdout/stderr line as it arrives; completes when the process exits.</summary>
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken ct);

    /// <summary>The exit code — valid only after <see cref="ReadLinesAsync"/> completes.</summary>
    int ExitCode { get; }

    /// <summary>Kills the process and its whole child tree (cancellation).</summary>
    void KillTree();

    /// <summary>Waits, at most <paramref name="timeout"/>, until a process that was killed is gone.
    /// Windows keeps what a dying process holds (its files, its working folder) locked until then,
    /// so a job that goes on with its frames right after a cancel would find its folder in use.</summary>
    Task WaitForExitAsync(TimeSpan timeout) => Task.CompletedTask;
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

    /// <summary>How long a cancel waits for the killed Blender to be gone.</summary>
    internal static readonly TimeSpan KillWait = TimeSpan.FromSeconds(30);

    /// <summary>Said with a crash after Blender found no OpenGL to draw with.</summary>
    internal const string NoOpenGlHint =
        "Blender found no OpenGL to draw with, which a draft (the solid view) and EEVEE need. On Windows "
        + "that is a Blender started outside a signed-in desktop session (a service, a scheduled task that "
        + "runs whether the user is signed in or not, an SSH session): render with Cycles there, or from a desktop session.";

    private readonly IBlenderProcessLauncher _launcher;
    private readonly Func<string, long?> _freeSpaceProbe;

    public BlenderRunner(IBlenderProcessLauncher launcher, Func<string, long?>? freeSpaceProbe = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
        _freeSpaceProbe = freeSpaceProbe ?? DiskPreflight.TryGetFreeBytes;
    }

    /// <summary>True on Windows for a path inside the Microsoft Store's access-restricted <c>WindowsApps</c> folder.</summary>
    public static bool IsWindowsStorePath(string? path) =>
        OperatingSystem.IsWindows() && path is not null
        && path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase);

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
        long estimate = DiskPreflight.EstimateBytes(job.FrameCount, job.Output.Width, job.Output.Height, job.Output.Kind)
                        + Math.Max(0L, job.ExtraBytes);
        // Probe where the outputs land — the caller's output directory when it differs from the job folder.
        string outputVolume = string.IsNullOrWhiteSpace(job.Output.OutputDirectory) ? job.WorkingDirectory : job.Output.OutputDirectory;
        if (_freeSpaceProbe(outputVolume) is { } free && !DiskPreflight.IsSufficient(estimate, free))
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = RenderFailureKind.DiskSpace,
                ErrorMessage = $"Not enough free disk space: the render needs about {estimate / 1_000_000} MB, but only {free / 1_000_000} MB is free.",
                Duration = stopwatch.Elapsed,
            };

        progress?.Report(new RenderProgress(RenderPhase.Rendering, "Starting Blender…")); // tier-3 spinner
        string jobLogPath = Path.Combine(job.WorkingDirectory, job.LogFileName);
        // --python-exit-code: an UNcaught Python exception (e.g. a Blender API rename the
        // script's probes missed) otherwise exits 0 and looks like a silent no-output run.
        // --disable-autoexec: no script a Blender file carries runs, whatever the preferences say.
        string[] args = ["-b", "--factory-startup", "--disable-autoexec", "--python-exit-code", "64", "--python", job.ScriptPath];
        if (job.ScriptArguments.Count > 0) args = [.. args, "--", .. job.ScriptArguments];
        var parser = new RenderProgressParser(job.FrameCount);

        // A render cancelled before it starts starts no Blender.
        if (ct.IsCancellationRequested)
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = RenderFailureKind.Cancelled,
                ErrorMessage = "The render was cancelled.",
                Duration = stopwatch.Elapsed,
            };
        // What the output folder holds before Blender starts: a movie or a still that is there
        // unchanged after it is an earlier render's, not this one's.
        var before = OutputCollector.Snapshot(job.Output);

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
                ErrorMessage = $"Could not start Blender at '{blender.Path}': {ex.Message}"
                               + (IsWindowsStorePath(blender.Path)
                                   ? " A Microsoft Store install can't be used headlessly — its executable is access-restricted " +
                                     "and its launcher returns no output — so use the standard Blender from blender.org."
                                   : ""),
                StartFailedPath = blender.Path,
                JobLogPath = null,
                Duration = stopwatch.Elapsed,
            };
        }

        bool cancelled = false;
        // Set once Blender's output has ended: until then, whatever leaves this method (a cancel,
        // a progress listener that throws) leaves Blender to be killed, never running unseen.
        bool exited = false;
        var tail = new Queue<string>(TailLines);
        var log = new JobLog(jobLogPath);
        int exitCode = -1;
        try
        {
            using var registration = ct.Register(() =>
            {
                cancelled = true;
                process.KillTree();
            });
            try
            {
                await foreach (var line in process.ReadLinesAsync(ct).ConfigureAwait(false))
                {
                    await log.WriteLineAsync(line).ConfigureAwait(false);
                    KeepTail(tail, line);
                    if (parser.Consume(line) is { } tick) progress?.Report(tick);
                }
                exited = true;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            if (cancelled || ct.IsCancellationRequested)
            {
                process.KillTree();
                await process.WaitForExitAsync(KillWait).ConfigureAwait(false);
            }
            try { exitCode = process.ExitCode; } catch { exitCode = -1; }
        }
        finally
        {
            if (!exited)
            {
                process.KillTree();
                await process.WaitForExitAsync(KillWait).ConfigureAwait(false);
            }
            await log.DisposeAsync().ConfigureAwait(false);
            process.Dispose();
        }

        stopwatch.Stop();
        // A cancel that comes after Blender said it was done and exited cleanly is too late: the
        // render is made.
        bool late = parser.Done && exitCode == 0 && !parser.Cancelled;
        return Classify(parser, (cancelled || ct.IsCancellationRequested) && !late, exitCode, tail, jobLogPath, job, stopwatch.Elapsed, progress, before, log.Problem);
    }

    private static RenderResult Classify(
        RenderProgressParser parser, bool cancelled, int exitCode, Queue<string> tail,
        string jobLogPath, RenderJob job, TimeSpan duration, IProgress<RenderProgress>? progress,
        IReadOnlyDictionary<string, (long, DateTime)> before, string? logProblem)
    {
        var warnings = new List<string>(parser.Warnings);
        if (logProblem is not null) warnings.Add(logProblem);
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
        else if (parser.PythonException is { } pythonException)
        {
            // An uncaught exception the script's own fail() never saw — surface the real
            // Python error line instead of a generic "exited with code N".
            kind = RenderFailureKind.ScriptError;
            error = "The generated script failed in Blender: " + pythonException;
        }
        else if (!parser.Done || exitCode != 0)
        {
            kind = RenderFailureKind.Crashed;
            error = $"Blender exited with code {exitCode} before finishing." +
                    (parser.NoOpenGl ? " " + NoOpenGlHint : "") +
                    (tail.Count > 0 ? "\nLast output:\n" + string.Join("\n", tail) : "");
        }
        else
        {
            // Render finished — read back and verify the output files (BA-B11).
            progress?.Report(new RenderProgress(RenderPhase.CollectingOutputs, "Collecting outputs", Device: parser.Device));
            var collection = OutputCollector.Collect(job.Output, frames, before);
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

    /// <summary>Blender's output, teed to the job's log. A log that cannot be written (a full
    /// disk) is not worth stopping a render for: what is lost of it is said in a warning.</summary>
    private sealed class JobLog : IAsyncDisposable
    {
        private readonly string _path;
        private StreamWriter? _writer;
        private string? _problem;

        public JobLog(string path)
        {
            _path = path;
            _writer = Open(path, out _problem);
        }

        /// <summary>Why the log is not whole; null when it is.</summary>
        public string? Problem => _problem;

        private static StreamWriter? Open(string path, out string? problem)
        {
            problem = null;
            try { return new StreamWriter(path, append: false) { AutoFlush = false }; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = $"Blender's output could not be written to {path}: {ex.Message}";
                return null;
            }
        }

        public async Task WriteLineAsync(string line)
        {
            if (_writer is null) return;
            try { await _writer.WriteLineAsync(line).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Lost(ex); }
        }

        public async ValueTask DisposeAsync()
        {
            if (_writer is null) return;
            try { await _writer.FlushAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Lost(ex); }
            finally { Close(); }
        }

        private void Lost(Exception ex)
        {
            _problem ??= $"Blender's output could not all be written to {_path}: {ex.Message}";
            Close();
        }

        private void Close()
        {
            var writer = _writer;
            _writer = null;
            // What the writer still holds cannot be written either.
            try { writer?.Dispose(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void KeepTail(Queue<string> tail, string line)
    {
        tail.Enqueue(line);
        while (tail.Count > TailLines) tail.Dequeue();
    }
}

/// <summary>Real <see cref="IBlenderProcessLauncher"/> over System.Diagnostics.Process. The Blender
/// it starts is tied to this process: on Windows it is in a Job Object that ends it (and what it
/// started) when this process ends, however it ends; on Linux it gets a parent-death signal when
/// setpriv is there to give it one. Disposing the process ends one that is still running.</summary>
public sealed class RealBlenderProcessLauncher : IBlenderProcessLauncher
{
    public IBlenderProcess Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        // A batch file's arguments would reach cmd.exe unescaped.
        if (BlenderLocator.IsBatchFile(executablePath))
            throw new InvalidOperationException($"{executablePath} is a batch file; Blender is started as blender.exe itself.");
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
        BlenderProcessTie.TieToParent(psi);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        return new RealBlenderProcess(process);
    }

    private sealed class RealBlenderProcess : IBlenderProcess
    {
        private readonly Process _process;
        private readonly IDisposable? _job;
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
        private int _openStreams = 2; // stdout + stderr

        public RealBlenderProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += OnData;
            _process.ErrorDataReceived += OnData;
            BlenderProcessTie.Start(_process);
            _job = BlenderProcessTie.Job(_process);
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

        public async Task WaitForExitAsync(TimeSpan timeout)
        {
            using var limit = new CancellationTokenSource(timeout);
            try { await _process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* still going: what it holds stays locked */ }
            catch (InvalidOperationException) { /* never started or already disposed */ }
        }

        public void Dispose()
        {
            KillTree();
            _job?.Dispose();
            _process.Dispose();
        }
    }
}
