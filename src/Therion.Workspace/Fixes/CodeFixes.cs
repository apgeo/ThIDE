// Quick fixes as text edits, so the editor's Ctrl+. menu and "fix all in workspace" share them.
// Line-based on purpose (like the original editor code): they never depend on fragile token spans.

using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Semantics;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

/// <param name="TitleKey">Resource key of the menu title; <paramref name="TitleArgs"/> fill its placeholders.</param>
/// <param name="SafeForBatch">May be applied to every occurrence without looking (fix all).</param>
public sealed record CodeFix(string TitleKey, IReadOnlyList<string> TitleArgs, string? DiagnosticCode, IReadOnlyList<TextEdit> Edits, bool SafeForBatch);

public static partial class CodeFixes
{
    public const string UnresolvedStation = "TH_SEM_001";
    public const string UnterminatedBlock = "TH0020";
    public const string WindowsPathSeparator = "TH_SEM_016";
    public const string ShotOutlier = SemanticDiagnosticCodes.ShotOutlier;

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
    public static IReadOnlySet<string> BatchCodes { get; } = new HashSet<string> { UnresolvedStation, UnterminatedBlock, WindowsPathSeparator };

    /// <summary>
    /// The fixes for one line (1-based), in menu order: rename to a suggested name, rewrite a Windows
    /// path separator, turn a zero-length leg into an equate, insert a missing terminator, insert a cs
    /// line into a centreline without one, comment the line out (naming the stations that disconnects).
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
        foreach (var d in diagnostics.Where(d => d.Span.Start.Line == lineNumber && d.Code.Value == WindowsPathSeparator))
            if (Separators(lines, d) is { } separators) fixes.Add(separators);
        // TH_SEM_012 comes from the project analysis, which the editor's own diagnostics do not hold, so
        // a line that may be a zero-length leg is checked without it: Ctrl+. in the editor and Fix in the
        // Diagnostics panel then offer the same fixes.
        var outlier = diagnostics.FirstOrDefault(d => d.Span.Start.Line == lineNumber && d.Code.Value == ShotOutlier);
        var zeroLeg = outlier is not null || HasZeroReading(lineText) ? ZeroLengthLeg(text, lines, lineNumber, outlier?.Span.FilePath) : null;
        if (zeroLeg?.Equate is { } equate) fixes.Add(equate);
        if (Terminator(lines, lineNumber) is { } end) fixes.Add(end);
        if (fw is "centreline" or "centerline" && !BodyHasKeyword(lines, lineNumber, "cs"))
            fixes.Add(new CodeFix("Fix_InsertCs", [], null, [TextEdit.Insert(lines.End(lineNumber), lines.NewLine + indent + "  cs ")], false));
        // A fix that disconnects a station is never the only one offered.
        if (!lineText.TrimStart().StartsWith('#') && zeroLeg is not { Equate: null, Disconnects.Length: > 0 })
            fixes.Add(zeroLeg?.Disconnects is { Length: > 0 } cut
                ? new CodeFix("Fix_CommentOutDisconnects", [cut], null, [TextEdit.Insert(lines.Start(lineNumber) + indent.Length, "# ")], false)
                : new CodeFix("Fix_CommentOut", [], null, [TextEdit.Insert(lines.Start(lineNumber) + indent.Length, "# ")], false));
        return fixes;
    }

    // A zero-length leg between two stations says they are the same point: `equate` says so without a
    // shot. Commenting the leg out instead drops that link, so the stations it alone connects (in this
    // file: no other leg, no equate) are named in that fix's title.
    private static (CodeFix? Equate, string Disconnects)? ZeroLengthLeg(string text, Lines lines, int line, string? filePath)
    {
        var ast = new Therion.Syntax.ThParser().Parse(filePath is { Length: > 0 } p ? p : "untitled.th", text).Value;
        if (ast is null) return null;
        var model = new SemanticBinder().Bind(ast, buildOccurrences: false);
        static bool Leg(ShotSymbol s) => (s.Flags & ShotFlags.Splay) == 0 && !s.From.Equals(s.To);
        var leg = model.Shots.FirstOrDefault(s => s.Span.Start.Line == line && Leg(s) && s.Length is <= 0.0001);
        if (leg is null) return null;

        bool Connected(QualifiedName station) =>
            model.Shots.Any(s => !ReferenceEquals(s, leg) && Leg(s) && (s.From.Equals(station) || s.To.Equals(station)))
            || model.EquateRecords.Any(e => e.ResolvedMembers.Any(m => m is { } q && q.Equals(station)));
        var cut = string.Join(", ", new[] { leg.From, leg.To }.Where(st => !Connected(st)).Select(st => st.Last));

        // The station tokens as written on the line; the rest of the line (readings) goes, a comment stays.
        string lineText = lines.Text(line);
        int indent = Indent(lineText).Length, hash = lineText.IndexOf('#');
        string body = (hash < 0 ? lineText[indent..] : lineText[indent..hash]).TrimEnd();
        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!tokens.Contains(leg.From.Last) || !tokens.Contains(leg.To.Last)) return (null, cut);
        var equate = new CodeFix("Fix_Equate", [leg.From.Last, leg.To.Last], ShotOutlier,
            [new TextEdit(lines.Start(line) + indent, body.Length, body, $"equate {leg.From.Last} {leg.To.Last}")], false);
        return (equate, cut);
    }

    // Cheap test before parsing the file: a zero-length leg has a reading of 0 among its values.
    private static bool HasZeroReading(string lineText)
    {
        int hash = lineText.IndexOf('#');
        var tokens = (hash < 0 ? lineText : lineText[..hash]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 3 && tokens.Any(t =>
            double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v == 0);
    }

    /// <summary>The batch-safe fix for one diagnostic, or null when there is none (or it is ambiguous).</summary>
    public static CodeFix? ForDiagnostic(string text, Diagnostic d) => ForDiagnostic(new Lines(text), d);

    /// <summary>As above, reusing the line index of a file with many diagnostics.</summary>
    internal static CodeFix? ForDiagnostic(Lines lines, Diagnostic d)
    {
        int line = d.Span.Start.Line;
        if (line < 1 || line > lines.Count) return null;
        var fix = d.Code.Value switch
        {
            UnresolvedStation => Rename(lines, d),
            UnterminatedBlock => Terminator(lines, line) ?? TerminatorForUnclosed(lines, line),
            WindowsPathSeparator => Separators(lines, d),
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
        // Whole tokens only: 'a1' must not match inside 'a10'.
        var hits = TokenHits(lineText, from).ToList();
        if (from == to || hits.Count == 0) return null;
        int i = hits[0];
        bool safe = suggestions.Count == 1 && hits.Count == 1;
        return new CodeFix("Fix_Rename", [from, to], d.Code.Value, [new TextEdit(lines.Start(line) + i, from.Length, from, to)], safe);
    }

    // `input date\a.th` → `input date/a.th`: the path is the first quoted text of the message. Safe when
    // it is an argument of the line exactly once.
    private static CodeFix? Separators(Lines lines, Diagnostic d)
    {
        int line = d.Span.Start.Line;
        var quoted = Quoted().Match(d.Message ?? "");
        if (!quoted.Success || Therion.Syntax.SourceGraph.PortableSeparators(quoted.Groups[1].Value) is not { } portable) return null;
        string from = quoted.Groups[1].Value;
        var hits = PathSeparatorFixes.Arguments(lines.Text(line)).Where(a => a.Token == from).ToList();
        if (hits.Count == 0) return null;
        return new CodeFix("Fix_PathSeparators", [from, portable], d.Code.Value,
            [new TextEdit(lines.Start(line) + hits[0].Column, from.Length, from, portable)], hits.Count == 1);
    }

    // Positions where `token` stands alone (whitespace, line ends or a comment around it).
    private static IEnumerable<int> TokenHits(string lineText, string token)
    {
        for (int i = lineText.IndexOf(token, StringComparison.Ordinal); i >= 0; i = lineText.IndexOf(token, i + 1, StringComparison.Ordinal))
        {
            bool startOk = i == 0 || char.IsWhiteSpace(lineText[i - 1]);
            int end = i + token.Length;
            bool endOk = end == lineText.Length || char.IsWhiteSpace(lineText[end]) || lineText[end] == '#';
            if (startOk && endOk) yield return i;
        }
    }

    /// <summary>Whether "fix all" can do anything for this diagnostic (a rename needs a suggestion).</summary>
    public static bool CanBatchFix(Diagnostic d) =>
        BatchCodes.Contains(d.Code.Value)
        && (d.Code.Value != UnresolvedStation || Suggestion().IsMatch((d.Message ?? "") + "  " + (d.Hint ?? "")));

    // A block opener on this line without its terminator ahead: insert it after the indented body.
    // Only batch-safe when the body is indented under the opener (or there is none): with a flat
    // body the end of the block cannot be told from the indentation.
    private static CodeFix? Terminator(Lines lines, int line)
    {
        string lineText = lines.Text(line);
        if (!Enders.TryGetValue(FirstWord(lineText), out var ender) || HasTerminatorAhead(lines, line, ender)) return null;
        string indent = Indent(lineText);
        int insertAfter = line;
        bool flatBody = false;
        for (int n = line + 1; n <= lines.Count; n++)
        {
            string t = lines.Text(n);
            if (t.Trim().Length == 0) continue;
            if (Indent(t).Length > indent.Length) { insertAfter = n; continue; }
            flatBody = insertAfter == line && !FirstWord(t).StartsWith("end", StringComparison.OrdinalIgnoreCase);
            break;
        }
        return new CodeFix("Fix_InsertEnd", [ender], UnterminatedBlock,
            [TextEdit.Insert(lines.End(insertAfter), lines.NewLine + indent + ender)], !flatBody);
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
    internal sealed class Lines
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

        /// <summary>The 1-based line holding <paramref name="offset"/>.</summary>
        public int LineOf(int offset)
        {
            int i = _starts.BinarySearch(offset);
            return i >= 0 ? i + 1 : ~i;
        }
    }
}
