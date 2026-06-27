// EDIT-04 — conservative "Format Document". Re-indents each line to its block-nesting depth
// (survey/centreline/scrap/…) and trims trailing whitespace, editing the document in place
// (bottom-up) so the caret is preserved. Lines inside opaque layout/lookup bodies (metapost/tex)
// are left verbatim. Purely structural — it never reorders or drops content, so it can't corrupt
// a file the way a full AST round-trip might.

using System;
using System.Collections.Generic;
using System.Text;
using AvaloniaEdit.Document;

namespace TherionProc.Editor;

internal static class TherionFormatter
{
    public static void Reindent(TextDocument doc, string indentUnit)
    {
        if (doc is null || doc.LineCount == 0) return;

        var opaque = TherionFoldingStrategy.LayoutBodyLines(doc); // metapost/tex bodies to leave alone
        int n = doc.LineCount;
        var depth = new int[n + 1];

        // Pass 1 (top-down): the indent depth each line should sit at.
        var stack = new Stack<string>();
        for (int i = 1; i <= n; i++)
        {
            var text = doc.GetText(doc.GetLineByNumber(i));
            var fw = TherionBlocks.FirstWord(text);
            if (TherionBlocks.CloserType(fw) is { } ctype)
            {
                while (stack.Count > 0 && stack.Peek() != ctype) stack.Pop();
                if (stack.Count > 0) stack.Pop();
            }
            depth[i] = stack.Count;
            if (TherionBlocks.IsBlockOpenerLine(text, out var otype)) stack.Push(otype);
        }

        // Pass 2 (bottom-up): rewrite leading whitespace + trim trailing. Bottom-up keeps offsets
        // valid as earlier lines change length.
        doc.BeginUpdate();
        try
        {
            for (int i = n; i >= 1; i--)
            {
                if (opaque.Contains(i)) continue;
                var line = doc.GetLineByNumber(i);
                var text = doc.GetText(line);
                var body = text.Trim();
                var desired = body.Length == 0 ? string.Empty : Repeat(indentUnit, depth[i]) + body;
                if (!string.Equals(text, desired, StringComparison.Ordinal))
                    doc.Replace(line.Offset, line.Length, desired);
            }
        }
        finally { doc.EndUpdate(); }
    }

    private static string Repeat(string unit, int count)
    {
        if (count <= 0) return string.Empty;
        var sb = new StringBuilder(unit.Length * count);
        for (int i = 0; i < count; i++) sb.Append(unit);
        return sb.ToString();
    }
}
