using Therion.Core;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

/// <summary>One proposed change, as the preview lists it.</summary>
/// <param name="Line">1-based line the change starts on.</param>
public sealed record EditPreviewRow(string FilePath, int Line, string Before, string After, TextEdit Edit)
{
    /// <summary>Every edit the row stands for, when it is more than <see cref="Edit"/> (a line with several changes).</summary>
    public IReadOnlyList<TextEdit>? Edits { get; init; }

    /// <summary>What the change does, in words, when the row stands for a whole file ("1 line: trailing spaces removed").</summary>
    public string? Description { get; init; }

    /// <summary>The edits applied when the row is kept.</summary>
    public IReadOnlyList<TextEdit> AllEdits => Edits ?? [Edit];
}

public static class FixAll
{
    /// <summary>
    /// The batch-safe fixes for every diagnostic with <paramref name="code"/>, one edit group per
    /// file. A fix that overlaps one already taken (two diagnostics on the same token) is skipped and
    /// counted, as are diagnostics without a safe fix.
    /// </summary>
    public static (EditBatch Batch, int Fixed, int Skipped) Plan(string code, IEnumerable<Diagnostic> diagnostics, Func<string, string?> readText)
    {
        var groups = new List<FileTextEdits>();
        int fixedCount = 0, skipped = 0;
        foreach (var file in diagnostics.Where(d => d.Code.Value == code && !string.IsNullOrEmpty(d.Span.FilePath))
                     .GroupBy(d => d.Span.FilePath!, FilePaths.Comparer))
        {
            if (readText(file.Key) is not { } text) { skipped += file.Count(); continue; }
            var edits = new List<TextEdit>();
            var lines = new CodeFixes.Lines(text);
            foreach (var d in file.OrderBy(d => d.Span.Start.Line))
            {
                if (CodeFixes.ForDiagnostic(lines, d) is not { } fix) { skipped++; continue; }
                if (fix.Edits.Any(e => edits.Any(x => Overlaps(x, e)))) { skipped++; continue; }
                edits.AddRange(fix.Edits);
                fixedCount++;
            }
            if (edits.Count > 0) groups.Add(new FileTextEdits(file.Key, edits));
        }
        return (new EditBatch(groups, [], []), fixedCount, skipped);
    }

    // Two insertions at one offset (two terminators for one block) count as overlapping too.
    private static bool Overlaps(TextEdit a, TextEdit b) =>
        a.Start == b.Start || a.Start < b.End && b.Start < a.End;

    /// <summary>The preview rows of a batch: each edit's line before and after.</summary>
    public static IReadOnlyList<EditPreviewRow> Preview(EditBatch batch, Func<string, string?> readText)
    {
        var rows = new List<EditPreviewRow>();
        foreach (var group in batch.Edits)
        {
            if (readText(group.FilePath) is not { } text) continue;
            var lineIndex = new CodeFixes.Lines(text);
            foreach (var e in group.Edits.OrderBy(e => e.Start))
            {
                int lineStart = e.Start == 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, e.Start - 1)) + 1;
                if (e.Start > 0 && text[e.Start - 1] == '\n') lineStart = e.Start;
                int lineEnd = text.IndexOf('\n', e.End);
                if (lineEnd < 0) lineEnd = text.Length;
                var before = text[lineStart..lineEnd].TrimEnd('\r');
                var after = (text[lineStart..e.Start] + e.NewText + text[e.End..lineEnd]).TrimEnd('\r');
                int line = lineIndex.LineOf(lineStart);
                rows.Add(new EditPreviewRow(group.FilePath, line, before, after, e));
            }
        }
        return rows;
    }

    /// <summary>
    /// The preview rows of a batch with one row per changed line: the line before, and after every edit
    /// on it. A line with two renamed stations reads as the final line, not as two half-changed ones. An
    /// edit that runs past its line takes the lines it touches into the same row.
    /// </summary>
    public static IReadOnlyList<EditPreviewRow> PreviewByLine(EditBatch batch, Func<string, string?> readText)
    {
        var rows = new List<EditPreviewRow>();
        foreach (var group in batch.Edits)
        {
            if (readText(group.FilePath) is not { } text) continue;
            var lineIndex = new CodeFixes.Lines(text);
            var edits = group.Edits.OrderBy(e => e.Start).ToList();
            int i = 0;
            while (i < edits.Count)
            {
                int lineStart = LineStartOf(text, edits[i].Start);
                int lineEnd = LineEndOf(text, edits[i].End);
                int j = i + 1;
                while (j < edits.Count && edits[j].Start <= lineEnd)
                    lineEnd = Math.Max(lineEnd, LineEndOf(text, edits[j++].End));
                var taken = edits.GetRange(i, j - i);
                var after = new System.Text.StringBuilder();
                int at = lineStart;
                foreach (var e in taken)
                {
                    after.Append(text, at, e.Start - at).Append(e.NewText);
                    at = e.End;
                }
                after.Append(text, at, lineEnd - at);
                rows.Add(new EditPreviewRow(group.FilePath, lineIndex.LineOf(lineStart),
                    text[lineStart..lineEnd].TrimEnd('\r'), after.ToString().TrimEnd('\r'), taken[0])
                    { Edits = taken });
                i = j;
            }
        }
        return rows;
    }

    private static int LineStartOf(string text, int offset) =>
        offset == 0 ? 0 : text.LastIndexOf('\n', Math.Min(offset, text.Length) - 1) + 1;

    private static int LineEndOf(string text, int offset)
    {
        int end = text.IndexOf('\n', Math.Min(offset, text.Length));
        return end < 0 ? text.Length : end;
    }

    /// <summary>The batch reduced to the chosen rows.</summary>
    public static EditBatch Keep(EditBatch batch, IEnumerable<EditPreviewRow> chosen)
    {
        var keep = chosen.SelectMany(r => r.AllEdits).ToHashSet();
        var groups = batch.Edits
            .Select(g => new FileTextEdits(g.FilePath, g.Edits.Where(keep.Contains).ToList()))
            .Where(g => g.Edits.Count > 0).ToList();
        return batch with { Edits = groups };
    }
}
