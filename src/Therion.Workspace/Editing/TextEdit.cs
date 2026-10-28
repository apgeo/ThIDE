namespace Therion.Workspace.Editing;

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

    /// <summary>An insertion at <paramref name="offset"/>: nothing is replaced, nothing is expected.</summary>
    public static TextEdit Insert(int offset, string text) => new(offset, 0, "", text);
}

/// <summary>
/// Edits to one file, planned against its text at one moment. Whoever applies them decides where that
/// text lives — the file on disk, or the editor buffer when the file is open.
/// </summary>
public sealed record FileTextEdits(string FilePath, IReadOnlyList<TextEdit> Edits);
