// Include paths written on Windows (`input date\cave\a.th`) opened where '\' is not a separator:
// Therion cannot open them, though the '/' spelling names an existing file. These find the paths a
// separator change repairs and rewrite only those. Text in, edits out; existence comes from the caller.

using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

public static class PathSeparatorFixes
{
    private static readonly HashSet<string> IncludeKeywords = new(StringComparer.OrdinalIgnoreCase) { "input", "load", "source" };

    /// <summary>
    /// One edit per <c>input</c>/<c>load</c>/<c>source</c> argument in <paramref name="text"/> that a
    /// <c>\</c> → <c>/</c> rewrite turns into an existing file (see <see cref="SourceGraph.WindowsSeparatorTarget"/>),
    /// resolved against the folder of <paramref name="filePath"/>. Empty on Windows, where both spellings agree.
    /// </summary>
    public static IReadOnlyList<TextEdit> Find(string text, string filePath, Func<string, bool> exists)
    {
        var edits = new List<TextEdit>();
        if (string.IsNullOrEmpty(text) || !text.Contains('\\')) return edits;
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
        var lines = new CodeFixes.Lines(text);
        for (int n = 1; n <= lines.Count; n++)
        {
            var lineText = lines.Text(n);
            if (!lineText.Contains('\\') || !IncludeKeywords.Contains(CodeFixes.FirstWord(lineText))) continue;
            foreach (var (col, token) in Arguments(lineText))
            {
                if (SourceGraph.WindowsSeparatorTarget(token, dir, exists) is null) continue;
                edits.Add(new TextEdit(lines.Start(n) + col, token.Length, token, SourceGraph.PortableSeparators(token)!));
            }
        }
        return edits;
    }

    /// <summary>
    /// The rewrites for every file in <paramref name="files"/>, one edit group per file that has any,
    /// with the preview rows. Files that cannot be read are passed over.
    /// </summary>
    public static (EditBatch Batch, IReadOnlyList<EditPreviewRow> Rows) Plan(
        IEnumerable<string> files, Func<string, string?> readText, Func<string, bool> exists)
    {
        var groups = new List<FileTextEdits>();
        foreach (var file in files.Distinct(FilePaths.Comparer))
        {
            if (readText(file) is not { } text) continue;
            var edits = Find(text, file, exists);
            if (edits.Count > 0) groups.Add(new FileTextEdits(file, edits));
        }
        var batch = new EditBatch(groups, [], []);
        return (batch, FixAll.Preview(batch, readText));
    }

    /// <summary>
    /// The arguments after the keyword with their column: whitespace-separated or double-quoted (the
    /// column is then inside the quotes), up to a <c>#</c> comment.
    /// </summary>
    internal static IEnumerable<(int Column, string Token)> Arguments(string lineText)
    {
        int i = 0;
        while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
        while (i < lineText.Length && !char.IsWhiteSpace(lineText[i])) i++;
        while (true)
        {
            while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
            if (i >= lineText.Length || lineText[i] == '#') yield break;
            if (lineText[i] == '"')
            {
                int close = lineText.IndexOf('"', i + 1);
                if (close < 0) yield break;
                yield return (i + 1, lineText[(i + 1)..close]);
                i = close + 1;
            }
            else
            {
                int start = i;
                while (i < lineText.Length && !char.IsWhiteSpace(lineText[i])) i++;
                yield return (start, lineText[start..i]);
            }
        }
    }
}
