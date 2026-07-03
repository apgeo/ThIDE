// Pure, testable core of the opt-in "rename in comments too" pass (P2): finds whole-word
// occurrences of a name inside `#` comment tokens, so the app layer only reads/writes files.

using System;
using System.Collections.Generic;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Locates name occurrences that live inside <c>#</c> comments (which symbol rename skips).</summary>
public static class CommentOccurrences
{
    /// <summary>
    /// Whole-word occurrences of <paramref name="name"/> inside the <c>#</c> comment tokens of
    /// <paramref name="text"/>, excluding any offset in <paramref name="exclude"/> (spans already
    /// handled by the symbol rename). Only comment text is considered — never code.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Find(
        string text, string name, ISet<int>? exclude = null)
    {
        var hits = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(text)) return hits;

        foreach (var t in new TherionTokenizer().Tokenize("<mem>", text))
        {
            if (t.Kind != TherionTokenKind.LineComment) continue;
            int commentEnd = t.Span.StartOffset + t.Span.Length;
            int idx = t.Span.StartOffset;
            while ((idx = text.IndexOf(name, idx, StringComparison.Ordinal)) >= 0 && idx < commentEnd)
            {
                int start = idx;
                idx += name.Length;
                bool boundedL = start == 0 || !IsRefChar(text[start - 1]);
                bool boundedR = start + name.Length >= text.Length || !IsRefChar(text[start + name.Length]);
                if (boundedL && boundedR && (exclude is null || !exclude.Contains(start)))
                    hits.Add((start, name.Length));
            }
        }
        return hits;
    }

    private static bool IsRefChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '@' or ':';
}
