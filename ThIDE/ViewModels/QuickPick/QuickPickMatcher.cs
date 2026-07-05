// Shared scoring for the quick-pick palettes (Ctrl+P files, Ctrl+Shift+P commands/symbols).
// Supports several search modes at once, ranked best-first:
//   • exact / prefix of the name              (strongest)
//   • contiguous substring of the name        (with word-start + position bonuses)
//   • fuzzy subsequence of the name           (chars in order, gaps allowed — VS-Code style)
//   • contiguous / fuzzy match in the path     (weakest)
// Space-separated tokens are AND-ed: every token must match somewhere (name preferred over path),
// so "su ma" finds "survey_main", and "svmn" fuzzily finds "survey_main" too.

using System;

namespace ThIDE.ViewModels.QuickPick;

public static class QuickPickMatcher
{
    /// <summary>
    /// Scores <paramref name="nameLower"/>/<paramref name="pathLower"/> against an already-trimmed,
    /// lower-cased <paramref name="query"/>. Returns null when it doesn't match (a token matched
    /// nowhere); higher is better. An empty query scores 0 (everything matches, original order kept).
    /// </summary>
    public static int? Score(string query, string nameLower, string pathLower)
    {
        if (query.Length == 0) return 0;

        int total = 0;
        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int? best = ScoreToken(token, nameLower, pathLower);
            if (best is null) return null;     // this token matched neither name nor path
            total += best.Value;
        }

        // Whole-query bonuses on the name.
        if (string.Equals(nameLower, query, StringComparison.Ordinal)) total += 2000;
        else if (nameLower.StartsWith(query, StringComparison.Ordinal)) total += 600;
        return total;
    }

    private static int? ScoreToken(string token, string nameLower, string pathLower)
    {
        int idx = nameLower.IndexOf(token, StringComparison.Ordinal);
        if (idx >= 0)
            return 1000 - Math.Min(idx, 200) * 2 + (idx == 0 ? 300 : 0) + WordStartBonus(nameLower, idx);

        int fuzzy = Subsequence(nameLower, token);
        if (fuzzy >= 0) return 420 - Math.Min(fuzzy, 300);

        int pidx = pathLower.IndexOf(token, StringComparison.Ordinal);
        if (pidx >= 0) return 150 - Math.Min(pidx, 100);

        int pfuzzy = Subsequence(pathLower, token);
        if (pfuzzy >= 0) return 60 - Math.Min(pfuzzy, 50);

        return null;
    }

    // If `pattern` is a subsequence of `text`, returns a compactness penalty (lower = tighter,
    // earlier match); otherwise -1.
    private static int Subsequence(string text, string pattern)
    {
        if (pattern.Length == 0) return 0;
        int ti = 0, pi = 0, first = -1, last = 0;
        while (ti < text.Length && pi < pattern.Length)
        {
            if (text[ti] == pattern[pi]) { if (first < 0) first = ti; last = ti; pi++; }
            ti++;
        }
        if (pi < pattern.Length) return -1;
        return (last - first) + first; // span tightness + how late it started
    }

    private static int WordStartBonus(string text, int idx) =>
        idx == 0 || !char.IsLetterOrDigit(text[idx - 1]) ? 80 : 0;
}
