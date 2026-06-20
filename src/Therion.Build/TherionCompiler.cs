// Implementation Plan §9bis.2 — Therion process runner.
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
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) => Forward(e.Data, isStderr: false, progress, diags);
        proc.ErrorDataReceived  += (_, e) => Forward(e.Data, isStderr: true,  progress, diags);

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

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using (cancellationToken.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { } }))
            {
                await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
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
        return new CompileResult(proc.ExitCode, diags.ToImmutable(), artifacts);
    }

    private void Forward(
        string? text, bool isStderr,
        IProgress<CompilerOutputLine>? progress,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        if (text is null) return;
        var line = _outputParser.Classify(text, isStderr);
        progress?.Report(line);
        if (line.Severity >= DiagnosticSeverity.Warning && line.Span is { } span)
        {
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
