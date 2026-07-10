using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Mcp.Tools;

/// <param name="Kind">What Therion produced: a 3D model, a map, an atlas, a log, …</param>
public sealed record ArtifactDto(string Path, string Kind, long SizeBytes, string LastWriteUtc);

/// <param name="ExitCode">Therion's own exit code. 0 is success; -1 means it never ran.</param>
/// <param name="Output">
/// The compiler's own words, classified. Warnings and errors first, then the tail of the log — a
/// successful build says a lot that nobody needs to read.
/// </param>
/// <param name="OutputLines">How many lines Therion printed in total, before the cap.</param>
public sealed record BuildResult(
    bool Success,
    int ExitCode,
    string EntryPoint,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    IReadOnlyList<ArtifactDto> Artifacts,
    IReadOnlyList<string> Output,
    int OutputLines,
    bool Truncated);

/// <summary>Ring R2 — running the real Therion compiler over the project.</summary>
[McpServerToolType]
public sealed class BuildTools(IWorkspaceHost host, ITherionCompiler compiler)
{
    /// <summary>Therion's own code for "I could not find the executable".</summary>
    private const string ToolMissing = "TH_BUILD_001";

    /// <summary>Enough of the log to see what happened without spending the caller's context on it.</summary>
    private const int MaxOutputLines = 60;

    /// <summary>A single compiler line is normally short; a pathological one must not blow the budget.</summary>
    private const int MaxOutputLineBytes = 2_000;

    // No dry run: producing the artifacts is the whole point. destructiveHint because Therion writes
    // over yesterday's outputs.
    [McpServerTool(Name = "run_build", Title = "Run Therion",
        ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Compiles the project with the real Therion executable, producing whatever the "
               + "thconfig exports — 3D models, maps, atlases. Reports Therion's own errors and "
               + "warnings, the files it wrote, and streams its output as progress while it runs. "
               + "This is the ground truth: get_diagnostics is ThIDE's own analysis, run_build is "
               + "Therion's verdict. It can take minutes on a large cave, and it can be cancelled.")]
    public async Task<ToolResult<BuildResult>> RunBuild(
        [Description("Workspace-relative .thconfig or .th to compile. Defaults to the loaded entry point.")]
        string? entryPoint = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<BuildResult>.Failure(error);

        var target = snapshot!.EntryPointPath;
        if (!string.IsNullOrWhiteSpace(entryPoint))
        {
            if (!WorkspacePaths.TryResolve(snapshot.Root, entryPoint, out target, out var reason))
                return Fail(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (!File.Exists(target))
                return Fail(ToolErrorCodes.FileNotFound, $"No such file: {entryPoint}");
        }

        var lines = new List<CompilerOutputLine>();
        int reported = 0;

        // Not System.Progress<T>: it posts each callback to a captured context, so the lines would be
        // appended *after* CompileAsync returns and Summarize would read a half-filled list — and
        // append to it from another thread while doing so.
        var relay = new InlineProgress<CompilerOutputLine>(line =>
        {
            lock (lines) lines.Add(line);
            // Therion never says how much is left, so there is no total to report — only that it is
            // still going, and what it just said.
            progress?.Report(new ProgressNotificationValue
            {
                Progress = Interlocked.Increment(ref reported),
                Message = line.Text,
            });
        });

        CompileResult result;
        try
        {
            result = await compiler.CompileAsync(target, relay, ct);
        }
        catch (OperationCanceledException)
        {
            throw;   // the host asked us to stop; let it see that, not a fabricated result
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ToolErrorCodes.BuildFailed, ex.Message);
        }

        if (result.Diagnostics.Any(d => d.Code.Value == ToolMissing))
            return Fail(ToolErrorCodes.ToolNotFound,
                "Therion is not installed, or ThIDE cannot find it. It is looked for in the configured "
                + "override path, then the usual install locations, then on PATH. Install Therion, or "
                + "set the ExternalTools.TherionPath override in ThIDE's preferences.");

        var (output, truncated) = Summarize(lines);

        return ToolResult<BuildResult>.Success(new BuildResult(
            Success: result.ExitCode == 0,
            ExitCode: result.ExitCode,
            EntryPoint: WorkspacePaths.ToRelative(snapshot.Root, target),
            Diagnostics: result.Diagnostics.Select(d => DiagnosticDto.From(d, snapshot.Root)).ToList(),
            Artifacts: result.Artifacts.Select(a => ToDto(a, snapshot.Root)).ToList(),
            Output: output,
            OutputLines: lines.Count,
            Truncated: truncated));
    }

    /// <summary>
    /// Every warning and error, then the tail of the rest. A clean build prints hundreds of lines
    /// nobody needs; a broken one hides the reason in the middle of them.
    /// </summary>
    private static (IReadOnlyList<string> Output, bool Truncated) Summarize(List<CompilerOutputLine> lines)
    {
        // Therion prints blank lines around its errors; the classifier gives them the neighbouring
        // severity, so they would otherwise lead the summary.
        var said = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();

        var interesting = said
            .Where(l => l.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .Select(l => ToolLimits.Utf8Prefix(l.Text, MaxOutputLineBytes))
            .ToList();

        if (interesting.Count >= MaxOutputLines)
            return (interesting.Take(MaxOutputLines).ToList(), true);

        var tail = said
            .Where(l => l.Severity is not (DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
            .Select(l => ToolLimits.Utf8Prefix(l.Text, MaxOutputLineBytes))
            .TakeLast(MaxOutputLines - interesting.Count)
            .ToList();

        return ([.. interesting, .. tail], interesting.Count + tail.Count < said.Count);
    }

    private static ArtifactDto ToDto(OutputArtifact artifact, string root) => new(
        Path: WorkspacePaths.IsInside(root, artifact.Path)
            ? WorkspacePaths.ToRelative(root, artifact.Path)
            : artifact.Path,
        Kind: artifact.Kind,
        SizeBytes: artifact.SizeBytes,
        LastWriteUtc: artifact.LastWriteUtc.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture));

    private static ToolResult<BuildResult> Fail(string code, string message) =>
        ToolResult<BuildResult>.Failure(code, message);

    /// <summary>Runs the callback on the reporting thread, so a report has happened by the time it returns.</summary>
    private sealed class InlineProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
