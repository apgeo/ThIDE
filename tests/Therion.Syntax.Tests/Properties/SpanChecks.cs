// Independent oracle for source positions, and the span invariants every token, AST node and
// diagnostic must satisfy. Rename, code fixes, renumber, map sync and the Diagnostics panel all
// splice text by these spans, so a span that is off by one corrupts the file.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Tests.Properties;

internal static class SpanChecks
{
    /// <summary>
    /// The 1-based (line, column) of every offset 0..text.Length. A line break is <c>\r\n</c>,
    /// <c>\n</c> or a lone <c>\r</c>; a column counts UTF-16 code units, so a character outside the
    /// BMP takes two columns (the convention of the editor and of LSP positions).
    /// </summary>
    public static SourceLocation[] Positions(string text)
    {
        var table = new SourceLocation[text.Length + 1];
        int line = 1, col = 1;
        for (int i = 0; i < text.Length; i++)
        {
            table[i] = new SourceLocation(line, col);
            char c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                table[i + 1] = new SourceLocation(line, col + 1);
                i++;
                line++;
                col = 1;
            }
            else if (c is '\r' or '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
        table[text.Length] = new SourceLocation(line, col);
        return table;
    }

    private static bool IsTrivia(TherionTokenKind k) =>
        k is TherionTokenKind.Whitespace or TherionTokenKind.NewLine or TherionTokenKind.LineContinuation;

    private static bool EndsWithLineBreak(string slice) =>
        slice.Length > 0 && slice[^1] is '\r' or '\n';

    /// <summary>Position problems of the tokens of <paramref name="text"/>; empty when every invariant holds.</summary>
    /// <param name="checkEnds">Also require the exclusive end location of tokens that do not end in a line break.</param>
    public static List<string> TokenProblems(
        string path, string text, ImmutableArray<TherionToken> tokens,
        bool checkEnds = true)
    {
        var problems = new List<string>();
        var pos = Positions(text);
        int expectedStart = 0;
        foreach (var t in tokens)
        {
            var s = t.Span;
            if (s.FilePath != path) problems.Add($"{t}: file path '{s.FilePath}'");
            if (s.StartOffset != expectedStart)
                problems.Add($"{t}: starts at {s.StartOffset}, previous token ended at {expectedStart} (gap or overlap)");
            if (s.Length <= 0) problems.Add($"{t}: empty token");
            if (s.StartOffset < 0 || s.StartOffset + s.Length > text.Length)
            {
                problems.Add($"{t}: outside the text (length {text.Length})");
                break;
            }
            var slice = text.Substring(s.StartOffset, s.Length);
            if (IsTrivia(t.Kind))
            {
                if (t.Text.Length != 0) problems.Add($"{t}: trivia carries text");
            }
            else if (t.Text != slice)
            {
                problems.Add($"{t}: text differs from the source slice '{Escape(slice)}'");
            }
            if (s.Start != pos[s.StartOffset])
                problems.Add($"{t}: start {s.Start}, offset {s.StartOffset} is at {pos[s.StartOffset]}");
            if (checkEnds && !EndsWithLineBreak(slice) &&
                s.End != pos[s.StartOffset + s.Length])
                problems.Add($"{t}: end {s.End}, end offset {s.StartOffset + s.Length} is at {pos[s.StartOffset + s.Length]}");
            expectedStart = s.StartOffset + s.Length;
            if (problems.Count > 5) break;
        }
        if (problems.Count == 0 && expectedStart != text.Length)
            problems.Add($"tokens end at {expectedStart}, the text at {text.Length}");
        return problems;
    }

    /// <summary>
    /// A string token holds a line break only right after a continuation backslash (optionally
    /// followed by blanks): a quote error must stay on its own line.
    /// </summary>
    public static List<string> StringProblems(ImmutableArray<TherionToken> tokens)
    {
        var problems = new List<string>();
        foreach (var t in tokens)
        {
            if (t.Kind != TherionTokenKind.String) continue;
            var s = t.Text;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] is not ('\r' or '\n')) continue;
                if (s[i] == '\n' && i > 0 && s[i - 1] == '\r') continue;   // second half of \r\n
                int j = i - 1;
                while (j >= 0 && s[j] is ' ' or '\t') j--;
                if (j < 0 || s[j] != '\\')
                    problems.Add($"{t}: string runs over an unescaped line break at {t.Span.StartOffset + i}");
            }
        }
        return problems;
    }

    /// <summary>The direct children of a node, for the node kinds that have any.</summary>
    public static IEnumerable<TherionNode> ChildrenOf(TherionNode node) => node switch
    {
        TherionFile f => f.Children,
        BlockCommand b => b.Children,
        _ => Enumerable.Empty<TherionNode>(),
    };

    /// <summary>
    /// Every node lies inside the text, its parent and its file, its start (and, unless it ends in
    /// a line break, its end) location matches its offsets, and siblings are ordered without overlap.
    /// The root's own end location is checked separately.
    /// </summary>
    public static List<string> AstProblems(string path, string text, TherionFile root)
    {
        var problems = new List<string>();
        var pos = Positions(text);

        void Visit(TherionNode node, SourceSpan parent)
        {
            int prevEnd = -1;
            string prevName = "";
            foreach (var child in ChildrenOf(node))
            {
                var s = child.Span;
                var name = $"{child.GetType().Name}@{s.StartOffset}+{s.Length}";
                if (s.IsEmpty)
                {
                    problems.Add($"{name}: empty span");
                    continue;
                }
                if (s.FilePath != path) problems.Add($"{name}: file path '{s.FilePath}'");
                if (s.StartOffset < 0 || s.StartOffset + s.Length > text.Length)
                {
                    problems.Add($"{name}: outside the text (length {text.Length})");
                    continue;
                }
                if (s.StartOffset < parent.StartOffset || s.StartOffset + s.Length > parent.StartOffset + parent.Length)
                    problems.Add($"{name}: not inside its parent {node.GetType().Name}@{parent.StartOffset}+{parent.Length}");
                if (s.StartOffset < prevEnd)
                    problems.Add($"{name}: overlaps or precedes its previous sibling {prevName}");
                if (s.Start != pos[s.StartOffset])
                    problems.Add($"{name}: start {s.Start}, offset is at {pos[s.StartOffset]}");
                var slice = text.Substring(s.StartOffset, s.Length);
                if (!EndsWithLineBreak(slice) && s.End != pos[s.StartOffset + s.Length])
                    problems.Add($"{name}: end {s.End}, end offset is at {pos[s.StartOffset + s.Length]}");
                prevEnd = s.StartOffset + s.Length;
                prevName = name;
                Visit(child, s);
            }
        }

        var rootSpan = new SourceSpan(path, SourceLocation.Start, pos[text.Length], 0, text.Length);
        if (root.Span.StartOffset != 0 || root.Span.Length != text.Length)
            problems.Add($"file node covers {root.Span.StartOffset}+{root.Span.Length}, the text is {text.Length} long");
        Visit(root, rootSpan);
        return problems;
    }

    /// <summary>All nodes of the tree, depth first.</summary>
    public static IEnumerable<TherionNode> Descendants(TherionNode node)
    {
        foreach (var child in ChildrenOf(node))
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>
    /// Every value and name span a node exposes for token-level editing slices the source to
    /// exactly the text the node reports for it.
    /// </summary>
    public static List<string> ValueSpanProblems(string text, TherionFile root)
    {
        var problems = new List<string>();

        void Expect(string what, SourceSpan span, string value)
        {
            if (span.IsEmpty && value.Length == 0) return;
            if (span.StartOffset < 0 || span.StartOffset + span.Length > text.Length)
            {
                problems.Add($"{what} '{Escape(value)}': span {span.StartOffset}+{span.Length} outside the text");
                return;
            }
            var slice = text.Substring(span.StartOffset, span.Length);
            if (slice != value)
                problems.Add($"{what} '{Escape(value)}': span slices to '{Escape(slice)}'");
        }

        foreach (var node in Descendants(root))
        {
            switch (node)
            {
                case DataRow row:
                    if (row.ValueSpans.Length != row.Values.Length)
                        problems.Add($"row at {row.Span.StartOffset}: {row.Values.Length} values, {row.ValueSpans.Length} spans");
                    for (int i = 0; i < Math.Min(row.Values.Length, row.ValueSpans.Length); i++)
                        Expect("row value", row.ValueSpans[i], row.Values[i]);
                    break;
                case StationFix fix:
                    Expect("fix station", fix.StationSpan, fix.Station);
                    break;
                case EquateCommand eq:
                    for (int i = 0; i < eq.Stations.Length; i++)
                        Expect("equate member", i < eq.StationSpans.Length ? eq.StationSpans[i] : default, eq.Stations[i]);
                    break;
                case MarkCommand mk:
                    for (int i = 0; i < mk.Stations.Length; i++)
                        Expect("mark station", i < mk.StationSpans.Length ? mk.StationSpans[i] : default, mk.Stations[i]);
                    break;
                case StationCommand st:
                    Expect("station command", st.StationSpan, st.Station);
                    break;
                case SurveyCommand sv:
                    Expect("survey name", sv.NameSpan, sv.Name);
                    break;
                case MapCommand map:
                    foreach (var m in map.Members) Expect("map member", m.Span, m.Id);
                    break;
            }
            if (problems.Count > 5) break;
        }
        return problems;
    }

    /// <summary>Diagnostic spans point into the parsed file, and their start location matches their offset.</summary>
    public static List<string> DiagnosticProblems(string path, string text, ImmutableArray<Diagnostic> diagnostics)
    {
        var problems = new List<string>();
        var pos = Positions(text);
        foreach (var d in diagnostics)
        {
            var s = d.Span;
            if (s.IsEmpty && s.StartOffset == 0) continue;   // file-level diagnostic without a position
            if (s.FilePath != path) { problems.Add($"{d.Code}: file path '{s.FilePath}'"); continue; }
            if (s.StartOffset < 0 || s.StartOffset + s.Length > text.Length)
            {
                problems.Add($"{d.Code}: span {s.StartOffset}+{s.Length} outside the text (length {text.Length})");
                continue;
            }
            if (s.Start != pos[s.StartOffset])
                problems.Add($"{d.Code}: start {s.Start}, offset {s.StartOffset} is at {pos[s.StartOffset]}");
        }
        return problems;
    }

    public static string Escape(string s)
    {
        var e = s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return e.Length > 1200 ? e[..1200] + "…" : e;
    }
}
