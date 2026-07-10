using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Core;
using Therion.Mcp.Mutations;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Mcp.Tools;

/// <param name="Text">The formatted source, when <c>write</c> was false. Null when the file was written.</param>
/// <param name="Changed">False when the file was already formatted; nothing was written.</param>
/// <param name="Mutation">Present when <c>write</c> was true.</param>
public sealed record FormatResult(
    string? Text,
    bool Truncated,
    bool Changed,
    MutationResult? Mutation);

/// <summary>Ring R2 — re-emitting a file from its parse tree.</summary>
[McpServerToolType]
public sealed class FormatTools(WorkspaceHost host, MutationEngine mutations)
{
    /// <summary>Enough of the errors to act on without burying the message.</summary>
    private const int MaxReportedErrors = 5;

    // Not readOnly (it can write) and destructive (it rewrites an existing file). Idempotent: running
    // it twice leaves the same bytes, which is the property the formatter is tested against.
    [McpServerTool(Name = "format_file", Title = "Format file",
        ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Pretty-prints a Therion file by re-emitting it from its parse tree. Returns the "
               + "formatted text by default; pass write:true to replace the file, preserving its "
               + "encoding. Refuses a file with parse errors, because a broken tree cannot be "
               + "re-emitted without losing text. The write:true preview shows the file as one "
               + "elided change — read the text with write:false first if you want to see it.")]
    public async Task<ToolResult<FormatResult>> FormatFile(
        [Description("Workspace-relative path, e.g. 'caves/upper.th'.")]
        string path,
        [Description("Replace the file on disk. Defaults to false, which only returns the text.")]
        bool write = false,
        [Description("The sha256 from an earlier result. The write is refused if the file changed since.")]
        string? expectedSha256 = null,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<FormatResult>.Failure(error);

        if (!WorkspacePaths.TryResolve(snapshot!.Root, path, out var full, out var reason))
            return ToolResult<FormatResult>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        if (snapshot.Workspace.TryGetFile(full) is null)
            return ToolResult<FormatResult>.Failure(ToolErrorCodes.FileNotFound,
                $"'{path}' is not a parsed file in the loaded project. Call list_files to see what is.");

        SourceFile current;
        try { current = SourceFileIo.Read(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<FormatResult>.Failure(ToolErrorCodes.ReadFailed, ex.Message);
        }

        // Re-parse the file as it stands *now*, not the tree the workspace parsed when it loaded.
        // Re-emitting a stale tree over the current text silently discards whatever changed on disk
        // since — and the plan's whole-file text-slice guard cannot notice, because the text it
        // expects is the very text it just read.
        var parsed = TherionWorkspace.ParseText(full, current.Text);
        if (parsed.Value is null)
            return ToolResult<FormatResult>.Failure(ToolErrorCodes.ParseErrors, $"'{path}' could not be parsed.");

        if (Describe(parsed.Diagnostics, snapshot.Root) is { } errors)
            return ToolResult<FormatResult>.Failure(ToolErrorCodes.ParseErrors, errors);

        var formatted = new TherionWriter().Write(parsed.Value);

        bool changed = !string.Equals(current.Text, formatted, StringComparison.Ordinal);

        if (!write)
        {
            int budget = ToolLimits.ClampBytes(maxBytes);
            bool truncated = formatted.Length > budget;
            return ToolResult<FormatResult>.Success(
                new FormatResult(truncated ? formatted[..budget] : formatted, truncated, changed, null));
        }

        if (!changed)
            return ToolResult<FormatResult>.Success(
                new FormatResult(null, false, false, new MutationResult(false, [], [], 0, 0)));

        var plan = new MutationPlan([
            new EditFile(full, [new TextEdit(0, current.Text.Length, current.Text, formatted)])]);

        var expected = expectedSha256 is null
            ? null
            : new Dictionary<string, string> { [WorkspacePaths.ToRelative(snapshot.Root, full)] = expectedSha256 };

        var applied = await mutations.ApplyAsync(plan, dryRun: false, expected, ct);
        if (applied.Error is { } failure) return ToolResult<FormatResult>.Failure(failure);

        return ToolResult<FormatResult>.Success(new FormatResult(null, false, true, applied.Data!));
    }

    /// <summary>
    /// A summary of the parse errors, or null when there are none. Warnings do not block formatting —
    /// they mean the file is odd, not that its tree is missing text.
    /// </summary>
    private static string? Describe(IReadOnlyList<Diagnostic> diagnostics, string root)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count == 0) return null;

        var lines = errors.Take(MaxReportedErrors).Select(d =>
        {
            var where = string.IsNullOrEmpty(d.Span.FilePath)
                ? ""
                : $" at {WorkspacePaths.ToRelative(root, d.Span.FilePath)}:{d.Span.Start.Line}";
            return $"{d.Code.Value}{where}: {d.Message}";
        });

        var suffix = errors.Count > MaxReportedErrors ? $" (+{errors.Count - MaxReportedErrors} more)" : "";
        return $"Cannot format a file with {errors.Count} parse error(s); fix them first. "
             + string.Join(" | ", lines) + suffix;
    }
}
