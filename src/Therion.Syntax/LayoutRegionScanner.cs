// LANG-02 (embedded code) — per-line region classification for a Therion document's layout blocks.
//
// The editor colorizer needs to know, line by line, which language a line is written in so it can
// pick the right lexer: a Therion `layout` option line, embedded MetaPost (`code metapost`), or
// embedded TeX (`code tex-map`/`tex-atlas`). This pure scanner is the single source of truth for
// that mapping — it mirrors LayoutBodyParser's GREEDY `code` → `endcode` rule (a `code metapost`
// with no `endcode` runs to the next `endcode`, pulling intervening option lines into the code
// block, exactly as Therion does — see tests/Corpus/Synthetic/project/Vladusca.thconfig).
//
// It is deliberately text/line based (no token or AST dependency) so the editor can run it
// synchronously on every debounced change, and it lives in Therion.Syntax (not the editor) so the
// rule is tested once and shared.

using System;
using System.Collections.Generic;

namespace Therion.Syntax;

/// <summary>Which language a layout-related line is written in (for highlighting).</summary>
public enum EmbeddedRegion
{
    /// <summary>Suppress highlighting (non-Therion opaque body, e.g. a <c>lookup</c> table).</summary>
    None,
    /// <summary>A Therion <c>layout</c> body option line (highlight option keys as keywords).</summary>
    LayoutOption,
    /// <summary>Embedded MetaPost (inside <c>code metapost … endcode</c>).</summary>
    MetaPost,
    /// <summary>Embedded TeX (inside <c>code tex-map/tex-atlas … endcode</c>).</summary>
    Tex,
}

/// <summary>Maps 1-based line numbers to their <see cref="EmbeddedRegion"/> for a Therion document.</summary>
public static class LayoutRegionScanner
{
    private enum State { Normal, InLayout, InLayoutCode, InLookup }

    /// <summary>
    /// Classify each line of <paramref name="lines"/>. Only lines needing special handling are
    /// present in the result; an absent line is an ordinary Therion line (use the global classifier).
    /// The <c>layout</c>/<c>endlayout</c> and <c>lookup</c>/<c>endlookup</c> opener/closer lines are
    /// intentionally left absent so they highlight as normal block keywords.
    /// </summary>
    public static IReadOnlyDictionary<int, EmbeddedRegion> Scan(IReadOnlyList<string> lines)
    {
        var map = new Dictionary<int, EmbeddedRegion>();
        var state = State.Normal;
        var codeRegion = EmbeddedRegion.MetaPost;

        for (int idx = 0; idx < lines.Count; idx++)
        {
            int lineNo = idx + 1;
            var (first, second) = FirstTwoWords(lines[idx]);
            if (first.Length == 0) continue; // blank/whitespace — nothing to highlight either way

            switch (state)
            {
                case State.Normal:
                    if (Eq(first, "layout")) state = State.InLayout;          // opener: leave absent
                    else if (Eq(first, "lookup")) state = State.InLookup;     // opener: leave absent
                    break;

                case State.InLookup:
                    if (Eq(first, "endlookup")) state = State.Normal;         // closer: leave absent
                    else map[lineNo] = EmbeddedRegion.None;                   // opaque body: no highlight
                    break;

                case State.InLayout:
                    if (Eq(first, "endlayout")) state = State.Normal;         // closer: leave absent
                    else if (Eq(first, "code"))
                    {
                        map[lineNo] = EmbeddedRegion.LayoutOption;            // the `code <lang>` fence
                        codeRegion = TargetRegion(second);
                        state = State.InLayoutCode;
                    }
                    else map[lineNo] = EmbeddedRegion.LayoutOption;
                    break;

                case State.InLayoutCode:
                    if (Eq(first, "endcode"))
                    {
                        map[lineNo] = EmbeddedRegion.LayoutOption;            // the `endcode` fence
                        state = State.InLayout;
                    }
                    else if (Eq(first, "endlayout"))
                    {
                        state = State.Normal;                                // unterminated code: close layout
                    }
                    else map[lineNo] = codeRegion;                           // greedy code body
                    break;
            }
        }

        return map;
    }

    /// <summary>The embedded language a <c>code &lt;target&gt;</c> line introduces.</summary>
    private static EmbeddedRegion TargetRegion(string target) =>
        target.StartsWith("tex", StringComparison.OrdinalIgnoreCase)
            ? EmbeddedRegion.Tex
            : EmbeddedRegion.MetaPost; // metapost / postprocess / unspecified

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The first two whitespace-delimited words of a line (either may be empty).</summary>
    private static (string First, string Second) FirstTwoWords(string text)
    {
        int i = 0, n = text.Length;
        while (i < n && char.IsWhiteSpace(text[i])) i++;
        int s1 = i;
        while (i < n && !char.IsWhiteSpace(text[i])) i++;
        string first = text.Substring(s1, i - s1);
        while (i < n && char.IsWhiteSpace(text[i])) i++;
        int s2 = i;
        while (i < n && !char.IsWhiteSpace(text[i])) i++;
        string second = text.Substring(s2, i - s2);
        return (first, second);
    }
}
