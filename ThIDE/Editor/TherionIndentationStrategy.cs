// Auto-indent for Therion: a new line inherits the previous line's indent, and
// gains one level after a block-opening command (survey/centreline/scrap/…).
// Therion ignores whitespace, but consistent indentation makes nesting readable.

using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;

namespace ThIDE.Editor;

internal sealed class TherionIndentationStrategy : IIndentationStrategy
{
    private const string IndentUnit = "  ";

    private static readonly HashSet<string> BlockStarts = new(StringComparer.OrdinalIgnoreCase)
    {
        "survey", "centreline", "centerline", "scrap", "map", "line", "area", "group",
    };

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        if (document is null || line is null) return;

        var previous = line.PreviousLine;
        while (previous is not null && IsBlank(document.GetText(previous)))
            previous = previous.PreviousLine;
        if (previous is null) return;

        var prevText = document.GetText(previous);
        var indent = LeadingWhitespace(prevText);
        if (BlockStarts.Contains(FirstWord(prevText)))
            indent += IndentUnit;

        var lineText = document.GetText(line);
        int existing = LeadingWhitespace(lineText).Length;
        if (indent != lineText.Substring(0, existing))
            document.Replace(line.Offset, existing, indent);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        for (int i = beginLine; i <= endLine && i <= document.LineCount; i++)
            IndentLine(document, document.GetLineByNumber(i));
    }

    private static bool IsBlank(string s) => s.Trim().Length == 0;

    private static string LeadingWhitespace(string text)
    {
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return text.Substring(0, i);
    }

    private static string FirstWord(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text.Substring(start, i - start);
    }
}
