// Implementation Plan �9bis.2 � Therion process runner.
// Decision #27: one compile at a time per workspace (gate at workspace level,
// not here). This class is reentrant-safe but does not enforce concurrency.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Build;

/// <summary>
/// How <see cref="TherionCompiler"/> runs Therion. The defaults reproduce a plain
/// <c>therion &lt;entry&gt;</c> started in the entry file's directory.
/// </summary>
public sealed record TherionCompileOptions
{
    public static TherionCompileOptions Default { get; } = new();

    /// <summary>
    /// Passed as <c>-l &lt;path&gt;</c> so the log goes to a known file. A relative path is resolved
    /// against the entry file's directory (the working directory). Null leaves Therion's default,
    /// <c>therion.log</c> in the working directory.
    /// </summary>
    public string? LogFile { get; init; }

    /// <summary>Passes <c>-q</c>: Therion prints less to stdout (its TeX/MetaPost children are not silenced).</summary>
    public bool Quiet { get; init; }

    /// <summary>
    /// A last say over the process before it starts: receives the fully built start info (Therion's
    /// path as <c>FileName</c>, its arguments in <c>ArgumentList</c>, the working directory, the
    /// stream redirections) and returns the one to start — for example a sandbox launcher as
    /// <c>FileName</c> with Therion's command after its own arguments, limits applied by a wrapper
    /// command, or <c>TMPDIR</c> pointed into the project. Keep the redirections: output is read
    /// from stdout/stderr and stdin is closed at once. Artefacts are still collected from the entry
    /// file's directory on this side.
    /// </summary>
    public Func<ProcessStartInfo, ProcessStartInfo>? ConfigureProcess { get; init; }
}

public sealed class TherionCompiler : ITherionCompiler
{
    /// <summary>The log file Therion writes in its working directory when no <c>-l</c> is given.</summary>
    public const string DefaultLogFileName = "therion.log";

    private readonly IExternalToolLocator _locator;
    private readonly ITherionOutputParser _outputParser;
    private readonly IOutputArtifactCollector _artifacts;
    private readonly TherionCompileOptions _defaults;

    public TherionCompiler(
        IExternalToolLocator locator,
        ITherionOutputParser? outputParser = null,
        IOutputArtifactCollector? artifacts = null,
        TherionCompileOptions? defaults = null)
    {
        _locator = locator;
        _outputParser = outputParser ?? new HeuristicTherionOutputParser();
        _artifacts = artifacts ?? new OutputArtifactCollector();
        _defaults = defaults ?? TherionCompileOptions.Default;
    }

    /// <summary>Compiles with the options given to the constructor (or the defaults).</summary>
    public ValueTask<CompileResult> CompileAsync(
        string entryPointPath,
        IProgress<CompilerOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
        => CompileAsync(entryPointPath, _defaults, progress, cancellationToken);

    /// <summary>
    /// Compiles <paramref name="entryPointPath"/> (resolved to a full path; Therion runs in its
    /// directory) with <paramref name="options"/>. Cancelling kills the process tree and throws.
    /// The result's <see cref="CompileResult.LogPath"/> names the log when one was written.
    /// </summary>
    public async ValueTask<CompileResult> CompileAsync(
        string entryPointPath,
        TherionCompileOptions options,
        IProgress<CompilerOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var tool = await _locator.FindAsync(ExternalToolLocator.Therion, cancellationToken).ConfigureAwait(false);
        if (tool is null)
        {
            diags.Add(Diagnostic.Create(
                "TH_BUILD_001",
                DiagnosticSeverity.Error,
                "Therion executable not found. Configure ExternalTools.TherionPath or install Therion.",
                SourceSpan.None));
            return new CompileResult(-1, diags.ToImmutable(), ImmutableArray<OutputArtifact>.Empty);
        }

        // A relative entry would be resolved twice — once here for the working directory and again
        // by Therion inside it — so Therion gets the full path.
        var entryFull = Path.GetFullPath(entryPointPath);
        var workDir = Path.GetDirectoryName(entryFull)!;
        var logPath = Path.GetFullPath(Path.Combine(workDir, options.LogFile ?? DefaultLogFileName));
        var startedAt = DateTimeOffset.UtcNow;

        var psi = new ProcessStartInfo
        {
            FileName = tool.Path,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // We own stdin so we can close it: Therion (notably the Windows build) ends an errored
            // run with a "Press ENTER to exit!" prompt and blocks on a stdin read. With no console
            // attached that read would never return, so therion.exe never exits and WaitForExitAsync
            // would hang forever, leaving the app stuck "Compiling…". Closing stdin makes that read
            // hit EOF immediately so the process terminates and we detect it (#3).
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (options.Quiet) psi.ArgumentList.Add("-q");
        if (options.LogFile is not null) { psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(logPath); }
        psi.ArgumentList.Add(entryFull);
        if (options.ConfigureProcess is { } configure)
            psi = configure(psi) ?? throw new InvalidOperationException("ConfigureProcess returned no start info.");

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var diagLock = new object();

        try
        {
            if (!proc.Start())
            {
                diags.Add(Diagnostic.Create(
                    "TH_BUILD_002",
                    DiagnosticSeverity.Error,
                    "Failed to start Therion process.",
                    SourceSpan.None));
                return new CompileResult(-1, diags.ToImmutable(), ImmutableArray<OutputArtifact>.Empty);
            }

            // Signal EOF on stdin right away so any interactive pause ("Press ENTER to exit!")
            // returns instead of blocking. Therion reads its input from files, never stdin.
            try { proc.StandardInput.Close(); } catch { /* stdin already gone — fine */ }

            // Drain stdout/stderr on tasks we own. Process.WaitForExitAsync would otherwise wait
            // for the redirected pipes to reach EOF as well — but Therion shells out to child
            // processes (metapost / tex) that inherit those pipes and can keep them open AFTER
            // therion.exe itself has exited. That EOF may never arrive, so the build would appear
            // to run forever (button kept flashing, spinner stuck). Owning the readers lets us
            // bound how long we wait for them once the process is gone (#3).
            var outTask = PumpAsync(proc.StandardOutput, isStderr: false, progress, diags, diagLock, cancellationToken);
            var errTask = PumpAsync(proc.StandardError, isStderr: true, progress, diags, diagLock, cancellationToken);

            using (cancellationToken.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { } }))
            {
                await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }

            // therion.exe has exited — flush whatever the readers have buffered, but never block on
            // a leaked child-held pipe handle: cap the post-exit drain so the build always finishes.
            try
            {
                await Task.WhenAll(outTask, errTask)
                    .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException) { /* a child still holds the pipe; proceed with what we have */ }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diags.Add(Diagnostic.Create(
                "TH_BUILD_003",
                DiagnosticSeverity.Error,
                $"Therion compile failed: {ex.Message}",
                SourceSpan.None));
        }

        var artifacts = _artifacts.Collect(workDir, since: startedAt);
        return new CompileResult(SafeExitCode(proc), diags.ToImmutable(), artifacts)
        {
            LogPath = WrittenSince(logPath, startedAt) ? logPath : null,
        };
    }

    /// <summary>Reads a redirected stream line-by-line until EOF, forwarding each line.</summary>
    private async Task PumpAsync(
        StreamReader reader, bool isStderr,
        IProgress<CompilerOutputLine>? progress,
        ImmutableArray<Diagnostic>.Builder diags, object diagLock,
        CancellationToken cancellationToken)
    {
        try
        {
            string? text;
            while ((text = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                Forward(text, isStderr, progress, diags, diagLock);
        }
        catch (OperationCanceledException) { /* build cancelled — stop reading */ }
        catch (Exception) { /* pipe closed / process gone — stop reading */ }
    }

    /// <summary>ExitCode is only valid once the process has exited; fall back to -1 if not.</summary>
    private static int SafeExitCode(Process proc)
    {
        try { return proc.HasExited ? proc.ExitCode : -1; }
        catch { return -1; }
    }

    private void Forward(
        string? text, bool isStderr,
        IProgress<CompilerOutputLine>? progress,
        ImmutableArray<Diagnostic>.Builder diags, object diagLock)
    {
        if (text is null) return;
        var line = _outputParser.Classify(text, isStderr);
        progress?.Report(line);
        if (line.Severity >= DiagnosticSeverity.Warning && line.Span is { } span)
        {
            lock (diagLock)
                diags.Add(Diagnostic.Create(
                    "TH_BUILD_OUT",
                    line.Severity,
                    line.Text,
                    span));
        }
    }

    private static bool WrittenSince(string path, DateTimeOffset since)
    {
        try { return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= since.UtcDateTime; }
        catch { return false; }
    }
}
