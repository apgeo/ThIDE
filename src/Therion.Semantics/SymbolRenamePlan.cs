// I3/P3 — pure, testable core of "true rename": turns a resolved symbol identity into the exact
// per-file text edits, driven entirely by the semantic occurrence index (scope-correct, @-aware,
// comment-free, cross-file). The app layer only reads/writes files + shows the preview.
// See .claude/symbol-occurrence-index-design.md and true-rename-symbol-plan.md.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Therion.Semantics;

/// <summary>The spans in one file that a rename should rewrite (each holds the symbol's name token).</summary>
public readonly record struct RenameFileEdits(
    string FilePath,
    string FileText,
    IReadOnlyList<(int Start, int Length)> Spans);

/// <summary>Computes the token-level edits for renaming a symbol (station or survey).</summary>
public static class SymbolRenamePlan
{
    /// <summary>
    /// Edits for renaming <paramref name="symbol"/> (whose current name token is
    /// <paramref name="expectedName"/>). <paramref name="readText"/> supplies each file's current
    /// text (null ⇒ skip). Only spans that still slice to <paramref name="expectedName"/> are returned,
    /// so a stale model can never corrupt unrelated text. Empty when the symbol has no occurrences.
    /// </summary>
    public static IReadOnlyList<RenameFileEdits> Compute(
        WorkspaceSemanticModel workspace,
        SymbolId symbol,
        string expectedName,
        Func<string, string?> readText)
    {
        var occ = workspace.FindOccurrences(symbol);
        if (occ.IsEmpty) return Array.Empty<RenameFileEdits>();

        var result = new List<RenameFileEdits>();
        foreach (var group in occ.GroupBy(o => o.Span.FilePath))
        {
            var text = readText(group.Key);
            if (text is null) continue;

            var spans = group
                .Select(o => (Start: o.Span.StartOffset, Length: o.Span.Length))
                .Where(h => h.Start >= 0 && h.Start + h.Length <= text.Length &&
                            string.Equals(text.Substring(h.Start, h.Length), expectedName, StringComparison.Ordinal))
                .Distinct()
                .OrderBy(h => h.Start)
                .Select(h => (h.Start, h.Length))
                .ToList();
            if (spans.Count > 0) result.Add(new RenameFileEdits(group.Key, text, spans));
        }
        return result;
    }
}
