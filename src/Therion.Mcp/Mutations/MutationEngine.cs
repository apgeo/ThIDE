// The one path by which an MCP tool is allowed to change a file (02 §B.4, §B.6; D-005).
//
// Everything a mutating tool needs to be safe lives here, so no individual tool has to remember it:
// dry-run by default, the workspace jail, an optional sha256 precondition, a per-edit text-slice
// check that refuses a stale plan outright rather than applying part of it, an atomic
// encoding-preserving write, and a re-lint of the changed files so the result carries its own
// evidence.

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

        // Validate every file before touching any of them: a plan that would fail halfway must fail
        // before it starts, or the caller is left with a workspace in a state nobody planned.
        var prepared = new List<PreparedChange>(plan.Changes.Count);
        foreach (var change in plan.Changes)
        {
            if (!WorkspacePaths.IsInside(root, change.AbsolutePath))
                return Failure(ToolErrorCodes.PathOutsideWorkspace,
                    $"'{WorkspacePaths.ToRelative(root, change.AbsolutePath)}' is outside the workspace.");

            var (preparedChange, failure) = Prepare(change, root, expectedSha256);
            if (failure is not null) return ToolResult<MutationResult>.Failure(failure);
            prepared.Add(preparedChange!);
        }

        var files = prepared.Select(p => p.Describe(root)).ToList();

        if (dryRun)
            return ToolResult<MutationResult>.Success(new MutationResult(true, files, [], 0, 0));

        var before = CountSeverities(DiagnosticsFor(snapshot, prepared.Select(p => p.Change.AbsolutePath)));

        try
        {
            foreach (var change in prepared) change.Write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(ToolErrorCodes.WriteFailed, ex.Message);
        }

        // The workspace on disk has moved; the model must be rebuilt before anything is asked of it.
        var reloaded = await host.ReloadAsync(ct);
        var after = DiagnosticsFor(reloaded, prepared.Select(p => p.Change.AbsolutePath));
        var afterCounts = CountSeverities(after);

        return ToolResult<MutationResult>.Success(new MutationResult(
            DryRun: false,
            Files: files,
            Diagnostics: after.Select(d => DiagnosticDto.From(d, reloaded.Root)).ToList(),
            NewErrors: afterCounts.Errors - before.Errors,
            NewWarnings: afterCounts.Warnings - before.Warnings));

        static ToolResult<MutationResult> Failure(string code, string message) =>
            ToolResult<MutationResult>.Failure(code, message);
    }

    private static (PreparedChange? Prepared, ToolError? Failure) Prepare(
        FileChange change, string root, IReadOnlyDictionary<string, string>? expectedSha256)
    {
        var relative = WorkspacePaths.ToRelative(root, change.AbsolutePath);

        switch (change)
        {
            case CreateFile create:
                if (File.Exists(create.AbsolutePath))
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
                        $"'{relative}' no longer holds the text this plan expected. Nothing was written; re-plan."));

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
        var touched = new HashSet<string>(paths.Select(Path.GetFullPath), PathComparer);

        var diagnostics = new List<Diagnostic>();
        foreach (var model in snapshot.Model.PerFile.Values)
            foreach (var d in model.Diagnostics)
                if (!string.IsNullOrEmpty(d.Span.FilePath) && touched.Contains(Path.GetFullPath(d.Span.FilePath)))
                    diagnostics.Add(d);

        try
        {
            foreach (var d in ProjectDiagnostics.Analyze(snapshot.Model, null, File.Exists))
                if (!string.IsNullOrEmpty(d.Span.FilePath) && touched.Contains(Path.GetFullPath(d.Span.FilePath)))
                    diagnostics.Add(d);
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
    /// between the check and the write.
    /// </summary>
    private sealed record PreparedChange(FileChange Change, SourceFile? Source, string NewText)
    {
        public static PreparedChange ForCreate(CreateFile create) => new(create, null, create.Content);

        public static PreparedChange ForEdit(EditFile edit, SourceFile source, string newText) =>
            new(edit, source, newText);

        public void Write()
        {
            if (Source is null) SourceFileIo.Create(Change.AbsolutePath, NewText);
            else SourceFileIo.Write(Source, NewText);
        }

        public FileChangeDto Describe(string root)
        {
            var relative = WorkspacePaths.ToRelative(root, Change.AbsolutePath);

            if (Source is null)
                return new FileChangeDto(relative, "create", 1, null, [], false);

            var edits = ((EditFile)Change).Edits;
            var preview = Preview.Build(Source.Text, edits, MaxPreviewLinesPerFile);
            return new FileChangeDto(relative, "edit", edits.Count, Source.Sha256, preview.Lines, preview.Truncated);
        }
    }
}
