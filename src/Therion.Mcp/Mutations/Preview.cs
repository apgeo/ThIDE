namespace Therion.Mcp.Mutations;

/// <summary>
/// Turns offset edits into the before/after lines a human — or a model deciding whether to confirm —
/// can actually read. A plan that says "12 edits in survey.th" is not reviewable; one that shows
/// <c>1 2 10.0 90 0</c> becoming <c>1 2a 10.0 90 0</c> is.
/// </summary>
internal static class Preview
{
    public static (IReadOnlyList<PreviewLine> Lines, bool Truncated) Build(
        string text, IReadOnlyList<TextEdit> edits, int maxLines)
    {
        if (edits.Count == 0) return ([], false);

        var lineStarts = LineStarts(text);
        var byLine = new SortedDictionary<int, List<TextEdit>>();

        foreach (var edit in edits.OrderBy(e => e.Start))
        {
            int line = LineIndexOf(lineStarts, edit.Start);
            if (!byLine.TryGetValue(line, out var list)) byLine[line] = list = [];
            list.Add(edit);
        }

        var lines = new List<PreviewLine>();
        bool truncated = false;

        foreach (var (lineIndex, lineEdits) in byLine)
        {
            if (lines.Count == maxLines) { truncated = true; break; }

            int start = lineStarts[lineIndex];
            int end = lineIndex + 1 < lineStarts.Count ? lineStarts[lineIndex + 1] : text.Length;
            var before = text[start..end].TrimEnd('\n', '\r');

            // Only the edits that stay on this line can be shown here; an edit spanning a newline
            // (a whole-file rewrite, say) would run past `end`, so it is clamped and elided.
            var after = ApplyWithin(before, start, end, lineEdits, out bool spilled);
            if (spilled) truncated = true;

            lines.Add(new PreviewLine(lineIndex + 1, before, after));
        }

        return (lines, truncated || byLine.Count > lines.Count);
    }

    private static string ApplyWithin(string line, int lineStart, int lineEnd, List<TextEdit> edits, out bool spilled)
    {
        spilled = false;
        var result = new System.Text.StringBuilder(line.Length);
        int cursor = 0;

        foreach (var edit in edits)
        {
            if (edit.End > lineEnd) { spilled = true; break; }

            int localStart = edit.Start - lineStart;
            if (localStart < cursor || localStart > line.Length) { spilled = true; break; }

            result.Append(line, cursor, localStart - cursor);
            result.Append(edit.NewText);
            cursor = Math.Min(localStart + edit.Length, line.Length);
        }

        result.Append(line, cursor, line.Length - cursor);
        return spilled ? "…" : result.ToString();
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    /// <summary>Index of the line containing <paramref name="offset"/>, by binary search on line starts.</summary>
    private static int LineIndexOf(List<int> lineStarts, int offset)
    {
        int lo = 0, hi = lineStarts.Count - 1, found = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lineStarts[mid] <= offset) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }
}
