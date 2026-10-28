using Therion.Core;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

public enum LineEndings { Keep, Lf, CrLf }

/// <summary>Tools ▸ Format all files: every project file through the formatter, as whole-file edits.</summary>
public static class FormatAll
{
    /// <summary>
    /// A whole-file edit for each file the formatter changes. Files that do not parse cleanly are
    /// skipped (the formatter would rewrite what it misunderstood) and listed.
    /// </summary>
    public static (EditBatch Batch, IReadOnlyList<EditPreviewRow> Rows, IReadOnlyList<string> Skipped) Plan(
        IEnumerable<string> files, Func<string, string?> readText, LineEndings endings)
    {
        var groups = new List<FileTextEdits>();
        var rows = new List<EditPreviewRow>();
        var skipped = new List<string>();
        var writer = new TherionWriter();
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

            var formatted = writer.Write(parsed.Value);
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
            rows.Add(new EditPreviewRow(file, 1, $"{ChangedLines(text, formatted)} line(s) differ", "", edit));
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
