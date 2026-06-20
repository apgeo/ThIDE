// Implementation Plan §4.1 — dedicated lightweight tokenizer for .xvi.
// XVI is a fixed-structure key-value text format (no nested blocks, no strings
// with embedded spaces), so we use a simple line-and-token scanner rather than
// the main TherionTokenizer.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>One logical line of an <c>.xvi</c> file: keyword + numeric/string arguments.</summary>
public readonly record struct XviLine(SourceSpan Span, string Keyword, ImmutableArray<string> Args);

/// <summary>Lightweight tokenizer that splits an <c>.xvi</c> source into <see cref="XviLine"/>s.</summary>
public static class XviTokenizer
{
    public static (ImmutableArray<XviLine> Lines, ImmutableArray<TrivialComment> LeadingComments)
        Tokenize(string filePath, string text)
    {
        var lines = ImmutableArray.CreateBuilder<XviLine>();
        var leadingComments = ImmutableArray.CreateBuilder<TrivialComment>();
        bool seenContent = false;

        int pos = 0;
        int line = 1;
        while (pos < text.Length)
        {
            int lineStart = pos;
            int lineCol = 1;
            // skip to EOL
            int eol = pos;
            while (eol < text.Length && text[eol] != '\n' && text[eol] != '\r') eol++;
            string raw = text.Substring(pos, eol - pos);
            string trimmed = raw.TrimStart();
            int colOffset = raw.Length - trimmed.Length;
            int startCol = lineCol + colOffset;

            if (trimmed.Length == 0)
            {
                // empty line
            }
            else if (trimmed[0] == '#')
            {
                var span = new SourceSpan(filePath,
                    new SourceLocation(line, startCol),
                    new SourceLocation(line, startCol + trimmed.Length),
                    lineStart + colOffset, trimmed.Length);
                var comment = new TrivialComment(span, trimmed);
                if (!seenContent) leadingComments.Add(comment);
                // else: trailing/middle comments currently discarded (M4 scope)
            }
            else
            {
                seenContent = true;
                var parts = trimmed.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var span = new SourceSpan(filePath,
                        new SourceLocation(line, startCol),
                        new SourceLocation(line, startCol + trimmed.Length),
                        lineStart + colOffset, trimmed.Length);
                    var args = ImmutableArray.CreateBuilder<string>(parts.Length - 1);
                    for (int i = 1; i < parts.Length; i++) args.Add(parts[i]);
                    lines.Add(new XviLine(span, parts[0], args.ToImmutable()));
                }
            }

            // advance past EOL
            pos = eol;
            if (pos < text.Length)
            {
                if (text[pos] == '\r' && pos + 1 < text.Length && text[pos + 1] == '\n') pos += 2;
                else pos += 1;
                line++;
            }
        }

        return (lines.ToImmutable(), leadingComments.ToImmutable());
    }
}
