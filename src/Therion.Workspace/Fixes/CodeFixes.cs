// Quick fixes as text edits, so the editor's Ctrl+. menu and "fix all in workspace" share them.
// Line-based on purpose (like the original editor code): they never depend on fragile token spans.

using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

/// <param name="TitleKey">Resource key of the menu title; <paramref name="TitleArgs"/> fill its placeholders.</param>
/// <param name="SafeForBatch">May be applied to every occurrence without looking (fix all).</param>
public sealed record CodeFix(string TitleKey, IReadOnlyList<string> TitleArgs, string? DiagnosticCode, IReadOnlyList<TextEdit> Edits, bool SafeForBatch);

public static partial class CodeFixes
{
    public const string UnresolvedStation = "TH_SEM_001";
    public const string UnterminatedBlock = "TH0020";

    private static readonly Dictionary<string, string> Enders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["survey"] = "endsurvey", ["centreline"] = "endcentreline", ["centerline"] = "endcentreline",
        ["scrap"] = "endscrap", ["map"] = "endmap", ["surface"] = "endsurface", ["line"] = "endline",
        ["area"] = "endarea", ["layout"] = "endlayout", ["group"] = "endgroup",
    };

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"did you mean '([^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex Suggestion();

    /// <summary>Codes "fix all" can handle.</summary>
    public static IReadOnlySet<string> BatchCodes { get; } = new HashSet<string> { UnresolvedStation, UnterminatedBlock };

    /// <summary>
    /// The fixes for one line (1-based), in menu order: rename to a suggested name, insert a missing
    /// terminator, insert a cs line into a centreline without one, comment the line out.
    /// </summary>
    public static IReadOnlyList<CodeFix> ForLine(string text, int lineNumber, IReadOnlyList<Diagnostic> diagnostics)
    {
        var lines = new Lines(text);
        if (lineNumber < 1 || lineNumber > lines.Count) return [];
        var fixes = new List<CodeFix>();
        string lineText = lines.Text(lineNumber);
        string indent = Indent(lineText);
        string fw = FirstWord(lineText);

        foreach (var d in diagnostics.Where(d => d.Span.Start.Line == lineNumber))
            if (Rename(lines, d) is { } rename) fixes.Add(rename);
        if (Terminator(lines, lineNumber) is { } end) fixes.Add(end);
        if (fw is "centreline" or "centerline" && !BodyHasKeyword(lines, lineNumber, "cs"))
            fixes.Add(new CodeFix("Fix_InsertCs", [], null, [TextEdit.Insert(lines.End(lineNumber), lines.NewLine + indent + "  cs ")], false));
        if (!lineText.TrimStart().StartsWith('#'))
            fixes.Add(new CodeFix("Fix_CommentOut", [], null, [TextEdit.Insert(lines.Start(lineNumber) + indent.Length, "# ")], false));
        return fixes;
    }

    /// <summary>The batch-safe fix for one diagnostic, or null when there is none (or it is ambiguous).</summary>
    public static CodeFix? ForDiagnostic(string text, Diagnostic d)
    {
        var lines = new Lines(text);
        int line = d.Span.Start.Line;
        if (line < 1 || line > lines.Count) return null;
        var fix = d.Code.Value switch
        {
            UnresolvedStation => Rename(lines, d),
            UnterminatedBlock => Terminator(lines, line) ?? TerminatorForUnclosed(lines, line),
            _ => null,
        };
        return fix is { SafeForBatch: true } ? fix : null;
    }

    // "did you mean 'X'?": rename the offending token in the line. Safe when there is exactly one
    // suggestion and the token appears once on the line.
    private static CodeFix? Rename(Lines lines, Diagnostic d)
    {
        int line = d.Span.Start.Line;
        var bad = Quoted().Match(d.Message ?? "");
        var suggestions = Suggestion().Matches((d.Message ?? "") + "  " + (d.Hint ?? ""));
        if (!bad.Success || suggestions.Count == 0) return null;
        string from = bad.Groups[1].Value, to = suggestions[0].Groups[1].Value;
        string lineText = lines.Text(line);
        int i = lineText.IndexOf(from, StringComparison.Ordinal);
        if (from == to || i < 0) return null;
        bool once = lineText.IndexOf(from, i + from.Length, StringComparison.Ordinal) < 0;
        bool safe = suggestions.Count == 1 && once;
        return new CodeFix("Fix_Rename", [from, to], d.Code.Value, [new TextEdit(lines.Start(line) + i, from.Length, from, to)], safe);
    }

    // A block opener on this line without its terminator ahead: insert it after the indented body.
    private static CodeFix? Terminator(Lines lines, int line)
    {
        string lineText = lines.Text(line);
        if (!Enders.TryGetValue(FirstWord(lineText), out var ender) || HasTerminatorAhead(lines, line, ender)) return null;
        string indent = Indent(lineText);
        int insertAfter = line;
        for (int n = line + 1; n <= lines.Count; n++)
        {
            string t = lines.Text(n);
            if (t.Trim().Length == 0) continue;
            if (Indent(t).Length > indent.Length) { insertAfter = n; continue; }
            break;
        }
        return new CodeFix("Fix_InsertEnd", [ender], UnterminatedBlock,
            [TextEdit.Insert(lines.End(insertAfter), lines.NewLine + indent + ender)], true);
    }

    // The diagnostic may sit on the end of the file rather than on the opener: find the last
    // unterminated opener before it.
    private static CodeFix? TerminatorForUnclosed(Lines lines, int line)
    {
        for (int n = Math.Min(line, lines.Count); n >= 1; n--)
            if (Terminator(lines, n) is { } fix) return fix;
        return null;
    }

    private static bool HasTerminatorAhead(Lines lines, int opener, string ender)
    {
        for (int n = opener + 1; n <= lines.Count; n++)
            if (string.Equals(FirstWord(lines.Text(n)), ender, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool BodyHasKeyword(Lines lines, int opener, string keyword)
    {
        for (int n = opener + 1; n <= lines.Count; n++)
        {
            var fw = FirstWord(lines.Text(n));
            if (string.Equals(fw, keyword, StringComparison.OrdinalIgnoreCase)) return true;
            if (fw.StartsWith("end", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return false;
    }

    internal static string FirstWord(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text[start..i];
    }

    internal static string Indent(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        return text[..i];
    }

    /// <summary>Line starts and ends (without the line break) of a text, 1-based.</summary>
    private sealed class Lines
    {
        private readonly string _text;
        private readonly List<int> _starts = [0];

        public Lines(string text)
        {
            _text = text;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') _starts.Add(i + 1);
            NewLine = text.Contains("\r\n") ? "\r\n" : "\n";
        }

        public string NewLine { get; }
        public int Count => _starts.Count;
        public int Start(int line) => _starts[line - 1];
        public int End(int line)
        {
            int end = line < _starts.Count ? _starts[line] - 1 : _text.Length;
            if (end > Start(line) && end - 1 < _text.Length && end > 0 && _text[end - 1] == '\r') end--;
            return end;
        }
        public string Text(int line) => _text[Start(line)..End(line)];
    }
}
