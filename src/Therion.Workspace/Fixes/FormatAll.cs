using Therion.Core;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

public enum LineEndings { Keep, Lf, CrLf }

/// <summary>
/// Tools ▸ Format all files: every project file through a formatter, as whole-file edits. The app
/// passes the editor's Format Document (a re-indent that never drops content), not an AST round-trip.
/// </summary>
public static class FormatAll
{
    /// <summary>
    /// A whole-file edit for each file the formatter changes. Files that do not parse cleanly are
    /// skipped (the formatter would rewrite what it misunderstood) and listed.
    /// </summary>
    /// <param name="format">The formatter (text in, text out).</param>
    /// <param name="describe">The preview's text for a file, given how many lines change.</param>
    public static (EditBatch Batch, IReadOnlyList<EditPreviewRow> Rows, IReadOnlyList<string> Skipped) Plan(
        IEnumerable<string> files, Func<string, string?> readText, LineEndings endings, Func<string, string> format, Func<int, string> describe)
    {
        var groups = new List<FileTextEdits>();
        var rows = new List<EditPreviewRow>();
        var skipped = new List<string>();
        foreach (var file in files.Distinct(FilePaths.Comparer).Order(StringComparer.Ordinal))
        {
            if (readText(file) is not { } text) continue;
            var parsed = TherionWorkspace.ParseText(file, text);
            // An unterminated block would come back "fixed" with terminators where the parser gave up.
            if (parsed.Value is null || parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error || d.Code.Value == CodeFixes.UnterminatedBlock))
            {
                skipped.Add(file);
                continue;
            }

            var formatted = format(text);
            var newline = endings switch
            {
                LineEndings.Lf => "\n",
                LineEndings.CrLf => "\r\n",
                _ => text.Contains("\r\n") ? "\r\n" : "\n",
            };
            formatted = formatted.Replace("\r\n", "\n").Replace("\n", newline);
            if (string.Equals(formatted, text, StringComparison.Ordinal)) continue;

            var edit = new TextEdit(0, text.Length, text, formatted);
            groups.Add(new FileTextEdits(file, [edit]));
            rows.Add(new EditPreviewRow(file, 1, describe(ChangedLines(text, formatted)), "", edit));
        }
        return (new EditBatch(groups, [], []), rows, skipped);
    }

    private static int ChangedLines(string a, string b)
    {
        var x = a.Replace("\r\n", "\n").Split('\n');
        var y = b.Replace("\r\n", "\n").Split('\n');
        int same = 0;
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] == y[i]) same++;
        return Math.Max(x.Length, y.Length) - same;
    }
}
