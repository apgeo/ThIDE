// The one path by which an MCP tool is allowed to change a file (02 §B.4, §B.6; D-005).
//
// Everything a mutating tool needs to be safe lives here, so no individual tool has to remember it:
// dry-run by default, the workspace jail, an optional sha256 precondition, a per-edit text-slice
// check that refuses a stale plan outright rather than applying part of it, an atomic
// encoding-preserving write with rollback, and a re-lint of the changed files so the result carries
// its own evidence.

using Therion.Core;
using Therion.Mcp.Tools;
using Therion.Semantics;

namespace Therion.Mcp.Mutations;

public sealed class MutationEngine(WorkspaceHost host)
{
    /// <summary>Preview lines returned per file before the rest are elided.</summary>
    private const int MaxPreviewLinesPerFile = 20;

    /// <summary>
    /// Validates <paramref name="plan"/>, and — unless <paramref name="dryRun"/> — writes it and
    /// re-lints what it touched.
    /// </summary>
    /// <param name="expectedSha256">
    /// Workspace-relative path → digest the caller believes the file has. Any file named here whose
    /// digest has changed fails the whole plan. Omit to skip the precondition.
    /// </param>
    public async Task<ToolResult<MutationResult>> ApplyAsync(
        MutationPlan plan,
        bool dryRun,
        IReadOnlyDictionary<string, string>? expectedSha256 = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<MutationResult>.Failure(error);

        var root = snapshot!.Root;

        if (plan.IsEmpty)
            return ToolResult<MutationResult>.Success(new MutationResult(dryRun, [], [], 0, 0));

        if (DuplicatePath(plan) is { } duplicate)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"The plan changes '{WorkspacePaths.ToRelative(root, duplicate)}' twice. "
                + "Each file may appear once, or the second change would silently win.");

        // Validate every file before touching any of them: a plan that would fail halfway must fail
        // before it starts, or the caller is left with a workspace in a state nobody planned.
        var prepared = new List<PreparedChange>(plan.Changes.Count);
        foreach (var change in plan.Changes)
        {
            if (!WorkspacePaths.IsInside(root, change.AbsolutePath))
                return ToolResult<MutationResult>.Failure(ToolErrorCodes.PathOutsideWorkspace,
                    $"'{WorkspacePaths.ToRelative(root, change.AbsolutePath)}' is outside the workspace.");

            var (preparedChange, failure) = Prepare(change, root, expectedSha256);
            if (failure is not null) return ToolResult<MutationResult>.Failure(failure);
            prepared.Add(preparedChange!);
        }

        if (dryRun)
            return ToolResult<MutationResult>.Success(
                new MutationResult(true, prepared.Select(p => p.Describe(root)).ToList(), [], 0, 0));

        var before = CountSeverities(DiagnosticsFor(snapshot, prepared.Select(p => p.Change.AbsolutePath)));

        if (Commit(prepared, root) is { } writeFailure)
            return ToolResult<MutationResult>.Failure(writeFailure);

        // The digest a caller passes back on its next call must describe the file as it is now, not as
        // it was before this write.
        var files = prepared.Select(p => p.DescribeAfterWrite(root)).ToList();

        // The workspace on disk has moved; the model must be rebuilt before anything is asked of it.
        WorkspaceSnapshot reloaded;
        try
        {
            reloaded = await host.ReloadAsync(ct);
        }
        catch (Exception ex) when (ex is Workspace.WorkspaceLoadException or IOException or UnauthorizedAccessException)
        {
            // The write already happened. Reporting a failure here would tell the caller its edit was
            // rejected, which is worse than telling it the edit landed but could not be re-checked.
            return ToolResult<MutationResult>.Success(new MutationResult(
                DryRun: false, Files: files, Diagnostics: [], NewErrors: 0, NewWarnings: 0,
                LintSkipped: true,
                Note: $"The files were written, but the project could not be reloaded to check them: {ex.Message}. "
                    + "Call load_workspace before relying on any further answer."));
        }

        var after = DiagnosticsFor(reloaded, prepared.Select(p => p.Change.AbsolutePath));
        var afterCounts = CountSeverities(after);

        return ToolResult<MutationResult>.Success(new MutationResult(
            DryRun: false,
            Files: files,
            Diagnostics: after.Select(d => DiagnosticDto.From(d, reloaded.Root)).ToList(),
            NewErrors: afterCounts.Errors - before.Errors,
            NewWarnings: afterCounts.Warnings - before.Warnings));
    }

    /// <summary>
    /// Writes every change, rolling the committed ones back if a later one fails. Per-file writes are
    /// atomic on their own; without this the second of three writes failing would leave the first
    /// permanently applied and the caller none the wiser.
    /// </summary>
    private static ToolError? Commit(List<PreparedChange> prepared, string root)
    {
        var committed = new List<PreparedChange>(prepared.Count);
        foreach (var change in prepared)
        {
            try
            {
                change.Write();
                committed.Add(change);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnrepresentableCharacterException)
            {
                var unrolled = RollBack(committed, root);
                var code = ex is UnrepresentableCharacterException
                    ? ToolErrorCodes.UnrepresentableCharacter
                    : ToolErrorCodes.WriteFailed;

                var message = $"'{WorkspacePaths.ToRelative(root, change.Change.AbsolutePath)}': {ex.Message}";
                return new ToolError(code, unrolled is null
                    ? $"{message} Nothing was written; the files already changed were restored."
                    : $"{message} WARNING: rolling back left '{unrolled}' in an unknown state.");
            }
        }
        return null;
    }

    /// <summary>Undoes committed writes, newest first. Returns the path it failed to restore, or null.</summary>
    private static string? RollBack(List<PreparedChange> committed, string root)
    {
        for (int i = committed.Count - 1; i >= 0; i--)
        {
            try { committed[i].Undo(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return WorkspacePaths.ToRelative(root, committed[i].Change.AbsolutePath);
            }
        }
        return null;
    }

    /// <summary>The first path a plan changes twice, or null. Two edits to one file would fight over its offsets.</summary>
    private static string? DuplicatePath(MutationPlan plan)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        foreach (var change in plan.Changes)
            if (!seen.Add(change.AbsolutePath)) return change.AbsolutePath;
        return null;
    }

    private static (PreparedChange? Prepared, ToolError? Failure) Prepare(
        FileChange change, string root, IReadOnlyDictionary<string, string>? expectedSha256)
    {
        var relative = WorkspacePaths.ToRelative(root, change.AbsolutePath);

        switch (change)
        {
            case CreateFile create:
                if (File.Exists(create.AbsolutePath) || Directory.Exists(create.AbsolutePath))
                    return (null, new ToolError(ToolErrorCodes.FileExists,
                        $"'{relative}' already exists. Nothing was written; delete or rename it first."));
                return (PreparedChange.ForCreate(create), null);

            case EditFile edit:
                if (!File.Exists(edit.AbsolutePath))
                    return (null, new ToolError(ToolErrorCodes.FileNotFound, $"No such file: {relative}"));

                SourceFile source;
                try { source = SourceFileIo.Read(edit.AbsolutePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { return (null, new ToolError(ToolErrorCodes.ReadFailed, ex.Message)); }

                if (expectedSha256 is not null
                    && expectedSha256.TryGetValue(relative, out var expected)
                    && !string.Equals(expected, source.Sha256, StringComparison.OrdinalIgnoreCase))
                    return (null, new ToolError(ToolErrorCodes.FileChanged,
                        $"'{relative}' changed since the plan was made. Re-plan against the current file."));

                if (Splice(source.Text, edit.Edits) is not { } rewritten)
                    return (null, new ToolError(ToolErrorCodes.StalePlan,
                        $"'{relative}' no longer holds the text this plan expected. Nothing was written; "
                        + "call load_workspace to pick up the current file, then re-plan."));

                return (PreparedChange.ForEdit(edit, source, rewritten), null);

            default:
                return (null, new ToolError(ToolErrorCodes.InvalidArgument, $"Unsupported change for '{relative}'."));
        }
    }

    /// <summary>
    /// Applies the edits to <paramref name="text"/>, or null when any of them overlaps, runs past the
    /// end, or no longer slices to the text it was planned against. All-or-nothing: a partially
    /// applied rename is worse than none.
    /// </summary>
    public static string? Splice(string text, IReadOnlyList<TextEdit> edits)
    {
        if (edits.Count == 0) return text;

        var ordered = edits.OrderBy(e => e.Start).ToList();

        int previousEnd = 0;
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var edit in ordered)
        {
            if (edit.Start < previousEnd) return null;                       // overlapping edits
            if (edit.Start < 0 || edit.End > text.Length) return null;       // off the end of the file
            if (!text.AsSpan(edit.Start, edit.Length).SequenceEqual(edit.ExpectedText)) return null;

            result.Append(text, previousEnd, edit.Start - previousEnd);
            result.Append(edit.NewText);
            previousEnd = edit.End;
        }
        result.Append(text, previousEnd, text.Length - previousEnd);
        return result.ToString();
    }

    /// <summary>Diagnostics belonging to the touched files only — the caller changed nothing else.</summary>
    private static List<Diagnostic> DiagnosticsFor(WorkspaceSnapshot snapshot, IEnumerable<string> paths)
    {
        var touched = new HashSet<string>(paths, PathComparer);

        bool Touched(Diagnostic d) =>
            !string.IsNullOrEmpty(d.Span.FilePath) && touched.Contains(WorkspacePaths.Canonicalize(d.Span.FilePath));

        var diagnostics = new List<Diagnostic>();
        foreach (var model in snapshot.Model.PerFile.Values)
            foreach (var d in model.Diagnostics)
                if (Touched(d)) diagnostics.Add(d);

        try
        {
            foreach (var d in ProjectDiagnostics.Analyze(snapshot.Model, null, File.Exists))
                if (Touched(d)) diagnostics.Add(d);
        }
        catch { /* best-effort, as `lint` is */ }

        return diagnostics;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static (int Errors, int Warnings) CountSeverities(List<Diagnostic> diagnostics) =>
        (diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
         diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning));

    /// <summary>
    /// A change that has passed every check, carrying the text it verified so nothing is re-read
    /// between the check and the write, and the bytes needed to undo it.
    /// </summary>
    private sealed record PreparedChange(FileChange Change, SourceFile? Source, string NewText)
    {
        private byte[]? _originalBytes;
        private string? _newDigest;

        public static PreparedChange ForCreate(CreateFile create) => new(create, null, create.Content);

        public static PreparedChange ForEdit(EditFile edit, SourceFile source, string newText) =>
            new(edit, source, newText);

        public void Write()
        {
            if (Source is null)
            {
                SourceFileIo.Create(Change.AbsolutePath, NewText);
            }
            else
            {
                _originalBytes = File.ReadAllBytes(Change.AbsolutePath);
                SourceFileIo.Write(Source, NewText);
            }
            _newDigest = SourceFileIo.DigestOf(Change.AbsolutePath);
        }

        /// <summary>Restores what <see cref="Write"/> replaced: the original bytes, or nothing at all.</summary>
        public void Undo()
        {
            if (_originalBytes is { } original) SourceFileIo.WriteOver(Change.AbsolutePath, original);
            else File.Delete(Change.AbsolutePath);
        }

        public FileChangeDto Describe(string root) => Describe(root, Source?.Sha256);

        /// <summary>The digest of the file as it now stands, so a caller can chain a second edit onto it.</summary>
        public FileChangeDto DescribeAfterWrite(string root) => Describe(root, _newDigest);

        private FileChangeDto Describe(string root, string? sha256)
        {
            var relative = WorkspacePaths.ToRelative(root, Change.AbsolutePath);

            if (Source is null)
                return new FileChangeDto(relative, "create", 1, sha256, [], false);

            var edits = ((EditFile)Change).Edits;
            var preview = Preview.Build(Source.Text, edits, MaxPreviewLinesPerFile);
            return new FileChangeDto(relative, "edit", edits.Count, sha256, preview.Lines, preview.Truncated);
        }
    }
}
