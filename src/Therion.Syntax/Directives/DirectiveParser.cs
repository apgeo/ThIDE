// Parses a single source line into a TherionDirective, if it carries a `#@…` comment.
// Grammar (per request):
//   #@<type> <arg1> <arg2> … <argN>
// - The type follows `#@` immediately and is case-insensitive.
// - Arguments are separated by runs of blanks and/or a single comma (with optional blanks
//   around it). A comma may delimit an EMPTY slot → that argument is undefined; a run of
//   blanks never produces an empty argument; a trailing comma does not add an argument.
// - An argument written as `_` or `undefined` (case-insensitive) is undefined.
// - An argument enclosed in `'…'` or `"…"` is a single argument equal to the quoted text
//   (verbatim — a quoted `undefined` is a defined value).
//
// The parser is allocation-light: it early-outs on lines that do not contain a `#@` comment.

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Directives;

/// <summary>Parses one line into a <see cref="TherionDirective"/> (or nothing).</summary>
public static class DirectiveParser
{
    /// <summary>The two characters that open a directive comment.</summary>
    public const string Prefix = "#@";

    /// <summary>
    /// Attempts to parse a directive from <paramref name="lineText"/>. Returns false when the
    /// line has no directive comment. <paramref name="lineStartOffset"/> is the absolute
    /// character offset of the line's first character (for span/offset computation);
    /// <paramref name="lineNumber"/> is 1-based.
    /// </summary>
    public static bool TryParse(
        string lineText, string filePath, int lineNumber, int lineStartOffset,
        out TherionDirective directive)
    {
        directive = null!;
        int hash = FindCommentHash(lineText);
        if (hash < 0 || hash + 1 >= lineText.Length || lineText[hash + 1] != '@') return false;

        // Directive type: skip '#@' and any blanks, then read up to a blank/comma.
        int i = hash + 2;
        while (i < lineText.Length && IsBlank(lineText[i])) i++;
        int typeStart = i;
        while (i < lineText.Length && !IsBlank(lineText[i]) && lineText[i] != ',') i++;
        if (i == typeStart) return false; // "#@" with no type
        string rawType = lineText.Substring(typeStart, i - typeStart);

        var args = ParseArgs(lineText, i, filePath, lineNumber, lineStartOffset);

        int len = lineText.Length - hash;
        var span = new SourceSpan(filePath,
            new SourceLocation(lineNumber, hash + 1),
            new SourceLocation(lineNumber, lineText.Length + 1),
            lineStartOffset + hash, len);

        directive = new TherionDirective(rawType.ToLowerInvariant(), rawType, args, span, lineNumber);
        return true;
    }

    /// <summary>
    /// The index of the <c>#</c> that starts the comment, or -1. A <c>#</c> inside a Therion
    /// double-quoted string is not a comment (mirrors the tokenizer); single quotes are not
    /// Therion string delimiters, so they don't hide a comment.
    /// </summary>
    private static int FindCommentHash(string s)
    {
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') inString = !inString;
            else if (c == '#' && !inString) return i;
        }
        return -1;
    }

    private static ImmutableArray<DirectiveArg> ParseArgs(
        string s, int start, string filePath, int lineNumber, int lineStartOffset)
    {
        var args = ImmutableArray.CreateBuilder<DirectiveArg>();
        int i = start;
        int n = s.Length;

        while (i < n)
        {
            while (i < n && IsBlank(s[i])) i++;
            if (i >= n) break;

            if (s[i] == ',')
            {
                // A comma with no preceding atom (since the last cell terminator) → empty cell.
                args.Add(DirectiveArg.Undefined(PointSpan(filePath, lineNumber, lineStartOffset, i)));
                i++;
                continue;
            }

            int argStart = i;
            string raw;
            bool quoted = false;
            if (s[i] == '"' || s[i] == '\'')
            {
                char quote = s[i];
                i++;
                int contentStart = i;
                while (i < n && s[i] != quote) i++;
                raw = s.Substring(contentStart, i - contentStart);
                if (i < n) i++; // consume closing quote
                quoted = true;
            }
            else
            {
                int wordStart = i;
                while (i < n && !IsBlank(s[i]) && s[i] != ',') i++;
                raw = s.Substring(wordStart, i - wordStart);
            }

            // Columns are 1-based indexes within the line; offsets are absolute in the document.
            var span = new SourceSpan(filePath,
                new SourceLocation(lineNumber, argStart + 1),
                new SourceLocation(lineNumber, i + 1),
                lineStartOffset + argStart, i - argStart);

            bool undefined = !quoted &&
                (raw == "_" || raw.Equals("undefined", StringComparison.OrdinalIgnoreCase));
            args.Add(undefined ? DirectiveArg.Undefined(span) : DirectiveArg.Defined(raw, span));

            // Consume this cell's terminator: trailing blanks then at most one comma.
            while (i < n && IsBlank(s[i])) i++;
            if (i < n && s[i] == ',') i++;
        }

        return args.ToImmutable();
    }

    private static SourceSpan PointSpan(string filePath, int lineNumber, int lineStartOffset, int indexInLine) =>
        new(filePath,
            new SourceLocation(lineNumber, indexInLine + 1),
            new SourceLocation(lineNumber, indexInLine + 1),
            lineStartOffset + indexInLine, 0);

    private static bool IsBlank(char c) => c == ' ' || c == '\t';
}
