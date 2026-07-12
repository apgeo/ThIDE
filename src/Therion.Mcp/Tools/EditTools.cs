using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Mcp.Mutations;

namespace Therion.Mcp.Tools;

/// <summary>
/// Ring R2 — a single, anchored text edit (D-042). This is the guarded reopening of D-032's dropped
/// <c>apply_text_edit</c>: it is a <em>find-and-replace</em>, never an offset splice, so the model can only
/// change text it correctly quoted. The heavy lifting is the shared <see cref="MutationEngine"/>, so this
/// inherits every R2 guarantee — dry-run preview by default, the expected-text guard, atomic
/// encoding-preserving write with rollback, the open-and-dirty refusal, and a post-apply re-lint.
/// </summary>
[McpServerToolType]
public sealed class EditTools(IWorkspaceHost host, MutationEngine mutations)
{
    // Not readOnly (writes) and destructive (overwrites an existing file). Not idempotent: once `find` is
    // replaced it is gone, so a second identical call misses.
    [McpServerTool(Name = "edit_file", Title = "Edit a file (find & replace)",
        ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description("Replaces one exact, unique run of text in a project file — a find-and-replace, for "
               + "fixing a value or a line a diagnostic flags. `find` must be text you read (e.g. via "
               + "read_file) and must occur EXACTLY ONCE; if it appears more than once, include more "
               + "surrounding text so it is unique. IMPORTANT: it PREVIEWS by default and writes NOTHING — "
               + "to actually apply the fix you MUST call it with dryRun:false, or the file is unchanged. "
               + "(Encoding is preserved and the file is re-linted after a write.) A clean lint means the "
               + "file still parses, NOT that the new value is correct — check the value yourself. For "
               + "renaming a survey/station use rename_symbol; for reformatting use format_file.")]
    public async Task<ToolResult<MutationResult>> EditFile(
        [Description("Workspace-relative path, e.g. 'caves/upper.th'.")]
        string path,
        [Description("The exact text to replace, quoted as read_file shows it. Must occur exactly once.")]
        string find,
        [Description("The replacement text.")]
        string replace,
        [Description("Preview only (default). Pass false to write the change to disk.")]
        bool dryRun = true,
        [Description("The sha256 from an earlier result. The write is refused if the file changed since.")]
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<MutationResult>.Failure(error);

        if (!WorkspacePaths.TryResolve(snapshot!.Root, path, out var full, out var reason))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);
        if (!File.Exists(full))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.FileNotFound, $"No such file: {path}");
        if (string.IsNullOrEmpty(find))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument, "'find' must not be empty.");

        SourceFile current;
        try { current = SourceFileIo.Read(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.ReadFailed, ex.Message);
        }

        int at = current.Text.IndexOf(find, StringComparison.Ordinal);
        if (at < 0)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                "The text to replace was not found. Quote 'find' exactly as read_file shows it, including whitespace.");
        if (current.Text.IndexOf(find, at + 1, StringComparison.Ordinal) >= 0)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                "The text to replace appears more than once. Include more surrounding text so it is unique.");

        if (string.Equals(find, replace, StringComparison.Ordinal))
            return ToolResult<MutationResult>.Success(new MutationResult(dryRun, [], [], 0, 0));

        var plan = new MutationPlan([new EditFile(full, [new TextEdit(at, find.Length, find, replace)])]);
        var expected = expectedSha256 is null
            ? null
            : new Dictionary<string, string> { [WorkspacePaths.ToRelative(snapshot.Root, full)] = expectedSha256 };

        return await mutations.ApplyAsync(plan, dryRun, expected, ct);
    }
}
