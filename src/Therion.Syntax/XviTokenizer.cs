// Lightweight scanner for the Tcl-flavoured `.xvi` format. It does not need a full Tcl
// interpreter: every meaningful line is a `set <var> {<body>}` statement whose body is a
// brace-balanced blob that may span many lines. This splits the source into those statements
// (recording leading comments and any unterminated brace) so XviParser can interpret each one.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>One <c>set &lt;name&gt; {…}</c> statement: the variable name and its brace body.</summary>
public readonly record struct XviSetStatement(
    SourceSpan Span, string Name, string Body, SourceSpan BodySpan, bool Terminated);

/// <summary>Splits an <c>.xvi</c> source into <see cref="XviSetStatement"/>s + leading comments.</summary>
public static class XviTokenizer
{
    /// <summary>
    /// Scans <paramref name="text"/> into the list of <c>set</c> statements. Anything that is not a
    /// blank line, a <c>#</c> comment, or a <c>set</c> statement is returned in
    /// <paramref name="strayLines"/> (keyword + span) so the parser can flag it.
    /// </summary>
    public static (ImmutableArray<XviSetStatement> Statements,
                   ImmutableArray<TrivialComment> LeadingComments,
                   ImmutableArray<(SourceSpan Span, string Text)> StrayLines)
        Tokenize(string filePath, string text)
    {
        var statements = ImmutableArray.CreateBuilder<XviSetStatement>();
        var leading = ImmutableArray.CreateBuilder<TrivialComment>();
        var stray = ImmutableArray.CreateBuilder<(SourceSpan, string)>();
        bool seenContent = false;

        int pos = 0, line = 1;
        while (pos < text.Length)
        {
            char c = text[pos];

            // Track line numbers; skip blank space between statements.
            if (c == '\n') { line++; pos++; continue; }
            if (c is ' ' or '\t' or '\r') { pos++; continue; }

            if (c == '#') // whole-line comment
            {
                int s = pos;
                while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r') pos++;
                var cspan = MakeSpan(filePath, line, s, pos);
                if (!seenContent) leading.Add(new TrivialComment(cspan, text[s..pos]));
                continue;
            }

            // A statement starts here. Read the first word (the command, expected: "set").
            int wordStart = pos, startLine = line;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos])) pos++;
            string word = text[wordStart..pos];

            if (!string.Equals(word, "set", System.StringComparison.Ordinal))
            {
                seenContent = true;
                // Stray (non-`set`) statement: record it and skip to end of line.
                while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r') pos++;
                stray.Add((MakeSpan(filePath, startLine, wordStart, pos), word));
                continue;
            }

            seenContent = true;
            SkipInlineSpace(text, ref pos);
            // Variable name.
            int nameStart = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '{') pos++;
            string name = text[nameStart..pos];
            SkipInlineSpace(text, ref pos);

            // The value: either a brace-balanced { … } blob or a single bare token.
            if (pos < text.Length && text[pos] == '{')
            {
                int bodyStart = pos + 1;
                int depth = 0;
                int p = pos;
                int braceLine = line;
                bool terminated = false;
                for (; p < text.Length; p++)
                {
                    char ch = text[p];
                    if (ch == '\n') line++;
                    if (ch == '{') depth++;
                    else if (ch == '}')
                    {
                        depth--;
                        if (depth == 0) { terminated = true; break; }
                    }
                }
                int bodyEnd = terminated ? p : text.Length;        // exclusive of closing brace
                int stmtEnd = terminated ? p + 1 : text.Length;
                var body = text[bodyStart..bodyEnd];
                statements.Add(new XviSetStatement(
                    MakeSpan(filePath, startLine, wordStart, stmtEnd),
                    name, body,
                    MakeSpan(filePath, braceLine, bodyStart, bodyEnd),
                    terminated));
                pos = stmtEnd;
            }
            else
            {
                // Bare value: `set name value` to end of line.
                int valStart = pos;
                while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r') pos++;
                statements.Add(new XviSetStatement(
                    MakeSpan(filePath, startLine, wordStart, pos),
                    name, text[valStart..pos],
                    MakeSpan(filePath, startLine, valStart, pos), true));
            }
        }

        return (statements.ToImmutable(), leading.ToImmutable(), stray.ToImmutable());
    }

    private static void SkipInlineSpace(string text, ref int pos)
    {
        while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t')) pos++;
    }

    private static SourceSpan MakeSpan(string filePath, int startLine, int startOffset, int endOffset) =>
        new(filePath,
            new SourceLocation(startLine, 1),
            new SourceLocation(startLine, 1 + (endOffset - startOffset)),
            startOffset, endOffset - startOffset);
}
