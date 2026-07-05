// Implementation Plan �9bis.2 � Therion process runner.
// Decision #27: one compile at a time per workspace (gate at workspace level,
// not here). This class is reentrant-safe but does not enforce concurrency.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Build;

public sealed class TherionCompiler : ITherionCompiler
{
    private readonly IExternalToolLocator _locator;
    private readonly ITherionOutputParser _outputParser;
    private readonly IOutputArtifactCollector _artifacts;

    public TherionCompiler(
        IExternalToolLocator locator,
        ITherionOutputParser? outputParser = null,
        IOutputArtifactCollector? artifacts = null)
    {
        _locator = locator;
        _outputParser = outputParser ?? new HeuristicTherionOutputParser();
        _artifacts = artifacts ?? new OutputArtifactCollector();
    }

    public async ValueTask<CompileResult> CompileAsync(
        string entryPointPath,
        IProgress<CompilerOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
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

        var workDir = Path.GetDirectoryName(Path.GetFullPath(entryPointPath))!;
        var startedAt = DateTimeOffset.UtcNow;

        var psi = new ProcessStartInfo
        {
            FileName = tool.Path,
            Arguments = QuoteIfNeeded(entryPointPath),
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
        return new CompileResult(SafeExitCode(proc), diags.ToImmutable(), artifacts);
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

    private static string QuoteIfNeeded(string path)
        => path.Contains(' ') ? "\"" + path + "\"" : path;
}
