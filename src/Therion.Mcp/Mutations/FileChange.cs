namespace Therion.Mcp.Mutations;

/// <summary>
/// One replacement inside a file, at character offsets into the text as it stood when the plan was
/// made. <see cref="ExpectedText"/> is what those offsets sliced to then; the apply step re-reads the
/// file and refuses the whole plan if any slice has moved. This is the guard
/// <c>SymbolRenamePlan.Compute</c> already applies — except that it silently drops the drifted spans,
/// which would leave a rename half-done. A tool must not half-do things to a caver's survey.
/// </summary>
public sealed record TextEdit(int Start, int Length, string ExpectedText, string NewText)
{
    public int End => Start + Length;
}

/// <summary>What a plan proposes to do to one path.</summary>
public abstract record FileChange(string AbsolutePath);

/// <summary>Rewrite spans of an existing file, preserving its encoding.</summary>
public sealed record EditFile(string AbsolutePath, IReadOnlyList<TextEdit> Edits) : FileChange(AbsolutePath);

/// <summary>Write a file that does not exist. Never overwrites: an existing target fails the plan.</summary>
public sealed record CreateFile(string AbsolutePath, string Content) : FileChange(AbsolutePath);

/// <summary>Create a directory. Existing ones are left alone and are not rolled back.</summary>
public sealed record CreateDirectory(string AbsolutePath) : FileChange(AbsolutePath);

/// <summary>
/// Copy a file byte for byte. Not read-then-write: a survey declaring <c>encoding iso-8859-1</c>
/// re-encoded as UTF-8 leaves the directive lying about the bytes (D-020).
/// </summary>
public sealed record CopyFile(string AbsolutePath, string SourceAbsolutePath) : FileChange(AbsolutePath);

/// <summary>
/// A whole mutation, one or more files at a time. Tools build this; <see cref="MutationEngine"/>
/// validates, previews and applies it.
/// </summary>
public sealed record MutationPlan(IReadOnlyList<FileChange> Changes)
{
    public static MutationPlan Empty { get; } = new([]);

    public bool IsEmpty => Changes.Count == 0;
}
