using Therion.Core;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

public enum LineEndings { Keep, Lf, CrLf }

/// <summary>The kinds of change a formatter made to a file (see <see cref="FormatChange"/>).</summary>
[Flags]
public enum FormatChangeKinds
{
    None = 0,
    /// <summary>Spaces or tabs at the end of a line removed or added.</summary>
    TrailingWhitespace = 1,
    /// <summary>The leading whitespace of a line changed.</summary>
    Indentation = 2,
    /// <summary>CRLF and LF line ends changed.</summary>
    LineEndings = 4,
    /// <summary>An <c>encoding</c> line now names another encoding.</summary>
    Encoding = 8,
    /// <summary>The same lines in another order (sorted <c>input</c> lines).</summary>
    Reordered = 16,
    /// <summary>Anything else: lines added, removed or rewritten.</summary>
    Other = 32,
}

/// <summary>
/// What a whole-file reformat changes, for a preview that says it in words.
/// </summary>
/// <param name="Lines">Lines whose text changed (line ends not counted).</param>
/// <param name="FirstLine">1-based line of the first change.</param>
/// <param name="FirstBefore">That line before (without its line end).</param>
/// <param name="FirstAfter">That line after.</param>
public sealed record FormatChange(int Lines, FormatChangeKinds Kinds, int FirstLine, string FirstBefore, string FirstAfter);

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
        IEnumerable<string> files, Func<string, string?> readText, LineEndings endings, Func<string, string> format, Func<int, string> describe) =>
        PlanCore(files, readText, endings, format,
            (file, text, formatted, edit) => new EditPreviewRow(file, 1, describe(ChangedLines(text, formatted)), "", edit));

    /// <summary>
    /// <see cref="Plan"/> with rows that show the change: each row is on the first changed line, with that line before and
    /// after, and <see cref="EditPreviewRow.Description"/> from <paramref name="describe"/>.
    /// </summary>
    public static (EditBatch Batch, IReadOnlyList<EditPreviewRow> Rows, IReadOnlyList<string> Skipped) PlanShowingChanges(
        IEnumerable<string> files, Func<string, string?> readText, LineEndings endings, Func<string, string> format, Func<FormatChange, string> describe) =>
        PlanCore(files, readText, endings, format, (file, text, formatted, edit) =>
        {
            var change = Describe(text, formatted);
            return new EditPreviewRow(file, change.FirstLine, change.FirstBefore, change.FirstAfter, edit) { Description = describe(change) };
        });

    private static (EditBatch Batch, IReadOnlyList<EditPreviewRow> Rows, IReadOnlyList<string> Skipped) PlanCore(
        IEnumerable<string> files, Func<string, string?> readText, LineEndings endings, Func<string, string> format,
        Func<string, string, string, TextEdit, EditPreviewRow> row)
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
            // Keep leaves every line its own ending, so a file that mixes CRLF and LF stays mixed.
            var newline = endings switch
            {
                LineEndings.Lf => "\n",
                LineEndings.CrLf => "\r\n",
                _ => MixesCrLfAndLf(text) ? null : text.Contains("\r\n") ? "\r\n" : "\n",
            };
            if (newline is not null) formatted = formatted.Replace("\r\n", "\n").Replace("\n", newline);
            if (string.Equals(formatted, text, StringComparison.Ordinal)) continue;

            var edit = new TextEdit(0, text.Length, text, formatted);
            groups.Add(new FileTextEdits(file, [edit]));
            rows.Add(row(file, text, formatted, edit));
        }
        return (new EditBatch(groups, [], []), rows, skipped);
    }

    private static bool MixesCrLfAndLf(string text)
    {
        bool crlf = false, lf = false;
        for (int i = text.IndexOf('\n'); i >= 0 && !(crlf && lf); i = text.IndexOf('\n', i + 1))
            if (i > 0 && text[i - 1] == '\r') crlf = true; else lf = true;
        return crlf && lf;
    }

    /// <summary>What turning <paramref name="before"/> into <paramref name="after"/> changes, line by line.</summary>
    public static FormatChange Describe(string before, string after)
    {
        var x = before.Replace("\r\n", "\n").Split('\n');
        var y = after.Replace("\r\n", "\n").Split('\n');
        var kinds = FormatChangeKinds.None;
        if (CountCrLf(before) != CountCrLf(after)) kinds |= FormatChangeKinds.LineEndings;

        int first = -1;
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            if (i >= x.Length || i >= y.Length || x[i] != y[i]) { first = i; break; }

        if (x.Length != y.Length)
            kinds |= FormatChangeKinds.Other;
        else if (first >= 0 && !x.Order(StringComparer.Ordinal).SequenceEqual(y.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            for (int i = first; i < x.Length; i++)
            {
                if (x[i] == y[i]) continue;
                var (a, b) = (x[i].TrimEnd(' ', '\t'), y[i].TrimEnd(' ', '\t'));
                if (x[i][a.Length..] != y[i][b.Length..])
                    kinds |= FormatChangeKinds.TrailingWhitespace;
                if (a == b) continue;
                if (a.TrimStart(' ', '\t') == b.TrimStart(' ', '\t')) kinds |= FormatChangeKinds.Indentation;
                else if (IsEncodingLine(a) && IsEncodingLine(b)) kinds |= FormatChangeKinds.Encoding;
                else kinds |= FormatChangeKinds.Other;
            }
        }
        else if (first >= 0)
            kinds |= FormatChangeKinds.Reordered;

        int line = first < 0 ? 0 : first;
        return new FormatChange(ChangedLines(before, after), kinds, line + 1,
            line < x.Length ? x[line].TrimEnd('\r') : "", line < y.Length ? y[line].TrimEnd('\r') : "");
    }

    private static int CountCrLf(string text)
    {
        int n = 0;
        for (int i = text.IndexOf("\r\n", StringComparison.Ordinal); i >= 0; i = text.IndexOf("\r\n", i + 2, StringComparison.Ordinal)) n++;
        return n;
    }

    private static bool IsEncodingLine(string line) =>
        line.TrimStart(' ', '\t').StartsWith("encoding", StringComparison.OrdinalIgnoreCase);

    private static int ChangedLines(string a, string b)
    {
        var x = a.Replace("\r\n", "\n").Split('\n');
        var y = b.Replace("\r\n", "\n").Split('\n');
        int same = 0;
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] == y[i]) same++;
        return Math.Max(x.Length, y.Length) - same;
    }
}
