namespace Therion.Workspace.Editing;

/// <summary>Copy a file byte for byte, so a Latin-1 survey stays Latin-1. Never overwrites.</summary>
public sealed record FileCopy(string SourcePath, string DestinationPath);

/// <summary>Create a file that does not exist yet (UTF-8, no BOM). Never overwrites.</summary>
public sealed record FileCreate(string FilePath, string Content);

/// <summary>
/// Everything one user action writes: new files, copies, and edits to existing files. Applied
/// all-or-nothing as far as checking goes — every edit must still slice to its expected text and no
/// target may exist before anything is written.
/// </summary>
/// <param name="RequiresWorkspaceReload">
/// The batch adds an <c>input</c> (or otherwise changes which files the project includes). The
/// workspace does not follow new includes on a re-parse, so the caller has to reload it.
/// </param>
public sealed record EditBatch(
    IReadOnlyList<FileTextEdits> Edits,
    IReadOnlyList<FileCreate> Creates,
    IReadOnlyList<FileCopy> Copies,
    bool RequiresWorkspaceReload = false)
{
    public static EditBatch Empty { get; } = new([], [], []);

    public bool IsEmpty => Edits.Count == 0 && Creates.Count == 0 && Copies.Count == 0;
}

/// <summary>Why a batch was refused before anything was written.</summary>
public sealed record EditProblem(string FilePath, EditProblemKind Kind);

public enum EditProblemKind
{
    /// <summary>The file holds different text from what the edits were planned against.</summary>
    Stale,
    /// <summary>A file to edit or a copy source does not exist.</summary>
    Missing,
    /// <summary>A file to create or a copy destination is already there.</summary>
    Exists,
    /// <summary>The same path is targeted twice (two edit groups, or a create and a copy).</summary>
    Duplicate,
}

/// <param name="NewTexts">The rewritten text of every edited file, keyed by the path as given.</param>
public sealed record EditBatchCheck(IReadOnlyList<EditProblem> Problems, IReadOnlyDictionary<string, string> NewTexts)
{
    public bool Ok => Problems.Count == 0;
}

public static class EditBatchChecker
{
    /// <summary>
    /// Checks a batch against the current state without writing anything.
    /// </summary>
    /// <param name="currentText">The text an edited file holds now — the editor buffer when it is open,
    /// the file on disk otherwise; null when it does not exist.</param>
    /// <param name="exists">Whether a path exists on disk (file or directory).</param>
    public static EditBatchCheck Check(EditBatch batch, Func<string, string?> currentText, Func<string, bool> exists)
    {
        var problems = new List<EditProblem>();
        var newTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(PathComparer);

        foreach (var group in batch.Edits)
        {
            if (!targets.Add(Normalize(group.FilePath)))
            {
                problems.Add(new(group.FilePath, EditProblemKind.Duplicate));
                continue;
            }
            if (currentText(group.FilePath) is not { } text)
            {
                problems.Add(new(group.FilePath, EditProblemKind.Missing));
                continue;
            }
            if (TextSplicer.Splice(text, group.Edits) is { } rewritten) newTexts[group.FilePath] = rewritten;
            else problems.Add(new(group.FilePath, EditProblemKind.Stale));
        }

        foreach (var create in batch.Creates)
            CheckNewTarget(create.FilePath);

        foreach (var copy in batch.Copies)
        {
            if (!exists(copy.SourcePath)) problems.Add(new(copy.SourcePath, EditProblemKind.Missing));
            CheckNewTarget(copy.DestinationPath);
        }

        return new EditBatchCheck(problems, newTexts);

        void CheckNewTarget(string path)
        {
            if (!targets.Add(Normalize(path))) problems.Add(new(path, EditProblemKind.Duplicate));
            else if (exists(path)) problems.Add(new(path, EditProblemKind.Exists));
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    // Windows and macOS file systems are case-insensitive by default; Linux is not.
    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
