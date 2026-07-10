using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Core;
using Therion.Semantics;

namespace Therion.Mcp.Tools;

/// <param name="Code">Stable code, e.g. <c>TH_SEM_015</c>. Feed it to explain_diagnostic.</param>
/// <param name="Severity">One of hint, info, warning, error.</param>
/// <param name="File">Workspace-relative path, or null for a diagnostic with no location.</param>
/// <param name="Line">1-based line of the span start; 0 when there is no span.</param>
/// <param name="Column">1-based column of the span start; 0 when there is no span.</param>
public sealed record DiagnosticDto(
    string Code,
    string Severity,
    string Message,
    string? File,
    int Line,
    int Column,
    string? Hint)
{
    /// <summary>The wire form of a diagnostic, with its location made workspace-relative.</summary>
    public static DiagnosticDto From(Diagnostic diagnostic, string root)
    {
        bool located = !string.IsNullOrEmpty(diagnostic.Span.FilePath);
        return new DiagnosticDto(
            Code: diagnostic.Code.Value,
            Severity: diagnostic.Severity.ToString().ToLowerInvariant(),
            Message: diagnostic.Message,
            File: located ? WorkspacePaths.ToRelative(root, diagnostic.Span.FilePath) : null,
            Line: located ? diagnostic.Span.Start.Line : 0,
            Column: located ? diagnostic.Span.Start.Column : 0,
            Hint: diagnostic.Hint);
    }
}

/// <param name="Total">Diagnostics matching the filters, before paging.</param>
public sealed record DiagnosticList(
    IReadOnlyList<DiagnosticDto> Diagnostics,
    int Total,
    int Offset,
    bool Truncated,
    int Errors,
    int Warnings);

/// <param name="DocTerm">A thbook term naming the manual section that covers this code.</param>
public sealed record DiagnosticExplanationDto(string Code, string Summary, string? Example, string? DocTerm);

/// <summary>Ring R1 — what is wrong with this project, and what does that mean.</summary>
[McpServerToolType]
public sealed class DiagnosticsTools(IWorkspaceHost host)
{
    /// <summary>Prefixes a model can expect from this server, for the unknown-code message.</summary>
    private static readonly string[] KnownCodePrefixes = ["TH0", "TH_SEM_", "TH_XVI_", "TH_WS_"];

    [McpServerTool(Name = "get_diagnostics", Title = "Get diagnostics", ReadOnly = true, Idempotent = true)]
    [Description("Every parse and semantic problem in the project, or in one file. This is the same "
               + "analysis 'therion-cli lint' runs: syntax errors, unresolved stations, loop "
               + "misclosures, disconnected surveys, missing includes. Use explain_diagnostic on a "
               + "code you do not recognise.")]
    public async Task<ToolResult<DiagnosticList>> GetDiagnostics(
        [Description("Workspace-relative file to restrict to. Omit for the whole project.")]
        string? file = null,
        [Description("Lowest severity to include: hint, info, warning, or error.")]
        string minSeverity = "hint",
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        if (!TryParseSeverity(minSeverity, out var floor))
            return ToolResult<DiagnosticList>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown severity '{minSeverity}'. Use hint, info, warning, or error.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<DiagnosticList>.Failure(error);

        string? scopedFile = null;
        if (!string.IsNullOrWhiteSpace(file))
        {
            if (!WorkspacePaths.TryResolve(snapshot!.Root, file, out scopedFile, out var reason))
                return ToolResult<DiagnosticList>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

            if (!snapshot.Model.PerFile.ContainsKey(scopedFile))
                return ToolResult<DiagnosticList>.Failure(ToolErrorCodes.FileNotFound,
                    $"'{file}' is not part of the loaded project. Call list_files to see what is.");
        }

        var all = Collect(snapshot!).Where(d => d.Severity >= floor);
        if (scopedFile is not null)
            all = all.Where(d => PathMatches(d.Span.FilePath, scopedFile));

        var ordered = all
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Span.FilePath, StringComparer.Ordinal)
            .ThenBy(d => d.Span.Start.Line)
            .ToList();

        int errors = ordered.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warnings = ordered.Count(d => d.Severity == DiagnosticSeverity.Warning);

        int start = Math.Clamp(offset, 0, ordered.Count);
        var page = ordered.Skip(start).Take(ToolLimits.ClampLimit(limit))
            .Select(d => DiagnosticDto.From(d, snapshot!.Root))
            .ToList();

        return ToolResult<DiagnosticList>.Success(new DiagnosticList(
            Diagnostics: page,
            Total: ordered.Count,
            Offset: start,
            Truncated: start + page.Count < ordered.Count,
            Errors: errors,
            Warnings: warnings));
    }

    [McpServerTool(Name = "explain_diagnostic", Title = "Explain diagnostic", ReadOnly = true, Idempotent = true)]
    [Description("Explains a diagnostic code in plain language, with an example of the correct form "
               + "and the thbook term that documents it. Not every code has an explanation; an "
               + "unexplained code is reported as such, not as a failure of the tool.")]
    public ToolResult<DiagnosticExplanationDto> ExplainDiagnostic(
        [Description("A diagnostic code exactly as get_diagnostics reported it, e.g. 'TH_SEM_015'.")]
        string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return ToolResult<DiagnosticExplanationDto>.Failure(ToolErrorCodes.InvalidArgument, "No code given.");

        var trimmed = code.Trim();
        if (DiagnosticExplanations.For(trimmed) is { } explanation)
            return ToolResult<DiagnosticExplanationDto>.Success(new DiagnosticExplanationDto(
                trimmed, explanation.Summary, explanation.Example, explanation.DocTerm));

        return ToolResult<DiagnosticExplanationDto>.Failure(
            ToolErrorCodes.UnknownDiagnosticCode,
            $"No explanation for '{trimmed}'. Codes this server explains start with "
            + $"{string.Join(", ", KnownCodePrefixes)}.");
    }

    /// <summary>Per-file parse/bind diagnostics plus the cross-file project analysis, exactly as `lint` does.</summary>
    private static List<Diagnostic> Collect(WorkspaceSnapshot snapshot)
    {
        var all = new List<Diagnostic>();
        foreach (var model in snapshot.Model.PerFile.Values) all.AddRange(model.Diagnostics);

        // Best-effort, as in `therion-cli lint`: a failure in the cross-file analysis must not cost
        // the caller the per-file findings it already has.
        try { all.AddRange(ProjectDiagnostics.Analyze(snapshot.Model, null, File.Exists)); }
        catch { /* keep what we have */ }

        return all;
    }

    /// <summary>
    /// Both sides are canonicalized: `resolvedFile` came through the jail, which resolves symlinks, and
    /// a diagnostic's FilePath has not. Comparing one form against the other silently matches nothing.
    /// </summary>
    private static bool PathMatches(string diagnosticPath, string resolvedFile) =>
        !string.IsNullOrEmpty(diagnosticPath)
        && WorkspacePaths.Canonicalize(diagnosticPath).Equals(resolvedFile,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool TryParseSeverity(string value, out DiagnosticSeverity severity) =>
        ToolEnums.TryParse(value, out severity);
}
