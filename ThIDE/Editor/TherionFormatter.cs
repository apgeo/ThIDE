// conservative "Format Document": TherionReindenter's line-by-line result applied to the document
// in place (bottom-up) so the caret is preserved. Purely structural — it never reorders or drops
// content, so it can't corrupt a file the way a full AST round-trip might.

using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using Therion.Workspace.Formatting;

namespace ThIDE.Editor;

internal static class TherionFormatter
{
    public static void Reindent(TextDocument doc, string indentUnit)
    {
        if (doc is null || doc.LineCount == 0) return;

        var lines = new List<string>(doc.LineCount);
        foreach (var line in doc.Lines) lines.Add(doc.GetText(line));
        var desired = TherionReindenter.DesiredLines(lines, indentUnit);

        // Bottom-up keeps offsets valid as earlier lines change length.
        doc.BeginUpdate();
        try
        {
            for (int i = lines.Count; i >= 1; i--)
            {
                if (desired[i - 1] is not { } want) continue;
                var line = doc.GetLineByNumber(i);
                doc.Replace(line.Offset, line.Length, want);
            }
        }
        finally { doc.EndUpdate(); }
    }
}
