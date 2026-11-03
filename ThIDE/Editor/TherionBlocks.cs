// Shared Therion block-structure analysis (survey…endsurvey, scrap…endscrap, line…endline, …).
// Pure text analysis so it runs on every debounced change. Reused by the matching-terminator
//, smart-Enter, document-outline and sticky-scroll features.

using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;

namespace ThIDE.Editor;

internal static class TherionBlocks
{
    /// <summary>Canonical block keyword → the <c>end…</c> keyword it expects.</summary>
    public static readonly IReadOnlyDictionary<string, string> Closers =
        Therion.Workspace.Formatting.TherionReindenter.BlockKeywords
            .ToDictionary(k => k, k => "end" + k, StringComparer.OrdinalIgnoreCase);

    /// <summary>The canonical block type for a first word, or null when it isn't a block opener.</summary>
    public static string? OpenerType(string firstWord)
    {
        var n = Normalize(firstWord);
        return Closers.ContainsKey(n) ? n : null;
    }

    /// <summary>The block type an <c>end…</c> word closes, or null. e.g. <c>endsurvey</c> → <c>survey</c>.</summary>
    public static string? CloserType(string firstWord)
    {
        if (firstWord.Length <= 3 || !firstWord.StartsWith("end", StringComparison.OrdinalIgnoreCase)) return null;
        int n = 3;
        while (n < firstWord.Length && char.IsLetter(firstWord[n])) n++; // strip trailing punctuation
        var rest = Normalize(firstWord.Substring(3, n - 3));
        return Closers.ContainsKey(rest) ? rest : null;
    }

    /// <summary>The <c>end…</c> keyword that closes <paramref name="type"/> (canonical).</summary>
    public static string CloserFor(string type) => Closers.TryGetValue(type, out var c) ? c : "end" + type;

    // centerline/centreline (and their end forms) are interchangeable in Therion.
    public static string Normalize(string keyword) =>
        string.Equals(keyword, "centerline", StringComparison.OrdinalIgnoreCase) ? "centreline" :
        string.Equals(keyword, "endcenterline", StringComparison.OrdinalIgnoreCase) ? "endcentreline" :
        keyword.ToLowerInvariant();

    public static string FirstWord(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text.Substring(start, i - start);
    }

    /// <summary>Leading whitespace (spaces/tabs) of a line.</summary>
    public static string LeadingWhitespace(string text)
    {
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return text.Substring(0, i);
    }

    /// <summary>
    /// True when the line opens a block. For <c>source</c> only a bare <c>source</c> line opens a
    /// block (the thconfig <c>source…endsource</c>); <c>source &lt;file&gt;</c> is a plain command.
    /// </summary>
    public static bool IsBlockOpenerLine(string lineText, out string type)
    {
        type = string.Empty;
        var fw = FirstWord(lineText);
        var t = OpenerType(fw);
        if (t is null) return false;
        if (t == "source" && lineText.Trim().Length > "source".Length) return false;
        type = t;
        return true;
    }

    /// <summary>
    /// Pairs block openers with their closers by 1-based line number (both directions),
    /// tolerant of malformed nesting (an <c>end…</c> closes the innermost block it names).
    /// </summary>
    public static Dictionary<int, int> BuildPairs(TextDocument document)
    {
        var pairs = new Dictionary<int, int>();
        var stack = new Stack<(string Type, int Line)>();
        foreach (var line in document.Lines)
        {
            var text = document.GetText(line);
            var fw = FirstWord(text);
            if (fw.Length == 0) continue;

            if (IsBlockOpenerLine(text, out var otype))
            {
                stack.Push((otype, line.LineNumber));
            }
            else if (CloserType(fw) is { } ctype)
            {
                while (stack.Count > 0 && stack.Peek().Type != ctype) stack.Pop();
                if (stack.Count > 0)
                {
                    var open = stack.Pop();
                    pairs[open.Line] = line.LineNumber;
                    pairs[line.LineNumber] = open.Line;
                }
            }
        }
        return pairs;
    }

    /// <summary>The editor breadcrumb for a caret line: <paramref name="fileLabel"/> (pointing at line 1)
    /// followed by each block opener enclosing the line, outermost first. Never empty.</summary>
    public static List<(string Label, int Line)> BreadcrumbPath(TextDocument doc, int caretLine, string fileLabel)
    {
        var path = EnclosingBlocks(doc, caretLine);
        path.Insert(0, (fileLabel, 1));
        return path;
    }

    private static List<(string Label, int Line)> EnclosingBlocks(TextDocument doc, int caretLine)
    {
        var stack = new List<(string Label, int Line, string Type)>();
        int max = Math.Min(caretLine, doc.LineCount);
        for (int i = 1; i <= max; i++)
        {
            var text = doc.GetText(doc.GetLineByNumber(i));
            var fw = FirstWord(text);
            if (fw.Length == 0) continue;
            if (IsBlockOpenerLine(text, out var type))
                stack.Add((BlockLabel(type, text), i, type));
            else if (CloserType(fw) is { } ctype)
                for (int k = stack.Count - 1; k >= 0; k--)
                    if (stack[k].Type == ctype) { stack.RemoveRange(k, stack.Count - k); break; }
        }
        return stack.ConvertAll(s => (s.Label, s.Line));
    }

    private static string BlockLabel(string type, string lineText)
    {
        var parts = lineText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && !parts[1].StartsWith('-') ? $"{type} {parts[1]}" : type;
    }
}
