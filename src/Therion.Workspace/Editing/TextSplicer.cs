using System.Text;

namespace Therion.Workspace.Editing;

public static class TextSplicer
{
    /// <summary>
    /// Applies the edits to <paramref name="text"/>, or null when any of them overlaps, runs past the
    /// end, or no longer slices to the text it was planned against. All-or-nothing: a partially
    /// applied rename is worse than none. Two insertions at the same offset are kept in the order given.
    /// </summary>
    public static string? Splice(string text, IReadOnlyList<TextEdit> edits)
    {
        if (edits.Count == 0) return text;

        // OrderBy is stable, so same-offset insertions keep the caller's order.
        var ordered = edits.OrderBy(e => e.Start).ToList();

        int previousEnd = 0;
        var result = new StringBuilder(text.Length);
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
}
