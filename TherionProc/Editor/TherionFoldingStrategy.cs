// Computes foldable regions for Therion's block commands (survey…endsurvey,
// centreline…endcentreline, scrap…endscrap, map, line, area, group, and the
// .thconfig layout…endlayout block). Driven purely off the document text so it
// can run on every (debounced) change.
//
// Closing keywords are matched to the innermost matching opener: an "end*" token
// only closes a block whose keyword it names. That keeps unrelated end* tokens —
// e.g. metapost 'endcode'/'enddef;' nested inside a layout block — from
// prematurely closing the enclosing fold.

using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace TherionProc.Editor;

internal static class TherionFoldingStrategy
{
    private static readonly HashSet<string> BlockStarts = new(StringComparer.OrdinalIgnoreCase)
    {
        "survey", "centreline", "centerline", "scrap", "map", "line", "area", "group", "layout", "lookup",
        "surface", "scan", "comment",
    };

    public static List<NewFolding> CreateFoldings(TextDocument document)
    {
        var result = new List<NewFolding>();
        var open = new Stack<(int Offset, string Name, string Keyword)>();

        foreach (var line in document.Lines)
        {
            var text = document.GetText(line);
            var first = FirstWord(text);
            if (first.Length == 0) continue;

            if (BlockStarts.Contains(first))
            {
                open.Push((line.Offset, Collapse(text), Normalize(first)));
            }
            else if (open.Count > 0 && IsEndKeyword(first, out var closes) &&
                     string.Equals(open.Peek().Keyword, closes, StringComparison.OrdinalIgnoreCase))
            {
                var start = open.Pop();
                int end = line.Offset + line.Length;
                if (end > start.Offset)
                    result.Add(new NewFolding(start.Offset, end) { Name = start.Name });
            }
        }

        // FoldingManager requires foldings ordered by start offset.
        result.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return result;
    }

    /// <summary>
    /// 1-based line numbers that fall <em>inside</em> a <c>layout … endlayout</c> or
    /// <c>lookup … endlookup</c> block (the non-Therion body, excluding the opener and
    /// closer lines). Used by the colorizer to leave that code unhighlighted.
    /// </summary>
    public static HashSet<int> LayoutBodyLines(TextDocument document)
    {
        var result = new HashSet<int>();
        int openLine = -1;
        string? closer = null;
        foreach (var line in document.Lines)
        {
            var first = FirstWord(document.GetText(line));
            if (first.Length == 0) continue;

            if (openLine < 0)
            {
                if (string.Equals(first, "layout", StringComparison.OrdinalIgnoreCase)) { openLine = line.LineNumber; closer = "endlayout"; }
                else if (string.Equals(first, "lookup", StringComparison.OrdinalIgnoreCase)) { openLine = line.LineNumber; closer = "endlookup"; }
            }
            else if (string.Equals(first, closer, StringComparison.OrdinalIgnoreCase))
            {
                for (int n = openLine + 1; n < line.LineNumber; n++) result.Add(n);
                openLine = -1;
                closer = null;
            }
        }
        return result;
    }

    /// <summary>True for an <c>end…</c> token; <paramref name="closes"/> is the block keyword it closes.</summary>
    private static bool IsEndKeyword(string word, out string closes)
    {
        closes = string.Empty;
        if (word.Length <= 3 || !word.StartsWith("end", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = word.Substring(3);
        int n = 0;
        while (n < rest.Length && char.IsLetter(rest[n])) n++; // strip trailing punctuation (e.g. 'enddef;')
        closes = Normalize(rest.Substring(0, n));
        return closes.Length > 0;
    }

    // centerline/centreline are interchangeable; normalize so a block opened with
    // one alias can be closed by the other.
    private static string Normalize(string keyword) =>
        string.Equals(keyword, "centerline", StringComparison.OrdinalIgnoreCase)
            ? "centreline"
            : keyword.ToLowerInvariant();

    private static string FirstWord(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text.Substring(start, i - start);
    }

    private static string Collapse(string lineText)
    {
        var t = lineText.Trim();
        return t.Length <= 60 ? t : t[..57] + "…";
    }
}
