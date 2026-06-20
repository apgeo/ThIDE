// Implementation Plan §4.1 (Lexer). Hand-rolled tokenizer for the Therion text
// formats (.th / .th2 / .thconfig). Superpower 3.0.0 no longer ships
// TokenizerBuilder<T>; we keep the door open to using TokenListParser<TKind,T>
// in the higher-level parser layer (M2+) by emitting a token list shape that
// can be wrapped if needed.
//
// Therion source-of-truth references:
//   - Comments / line continuation: therion/src/thinput.cxx
//   - Identifier / number / string rules: therion/src/thparse.cxx
// thbook v6.4.0 §2 "General syntax".

using System.Collections.Immutable;
using System.Text;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>A single lexical token with its absolute <see cref="SourceSpan"/> and the verbatim text.</summary>
public readonly record struct TherionToken(TherionTokenKind Kind, SourceSpan Span, string Text)
{
    public override string ToString() => $"{Kind}('{Text}') @ {Span}";
}

/// <summary>
/// Hand-rolled tokenizer for the Therion text formats. Never throws — invalid
/// characters are reported through the caller's diagnostic channel by inspecting
/// the resulting tokens.
/// </summary>
public sealed class TherionTokenizer
{
    /// <summary>Tokenize <paramref name="text"/> into a flat token list.</summary>
    public ImmutableArray<TherionToken> Tokenize(string filePath, string text)
    {
        var builder = ImmutableArray.CreateBuilder<TherionToken>();

        int pos = 0;
        int line = 1;
        int col = 1;

        while (pos < text.Length)
        {
            char c = text[pos];

            // -- Newline (\r\n, \n, \r) ---------------------------------------
            if (c is '\r' or '\n')
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                if (c == '\r' && pos + 1 < text.Length && text[pos + 1] == '\n')
                    pos += 2;
                else
                    pos += 1;

                builder.Add(new TherionToken(
                    TherionTokenKind.NewLine,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    text.Substring(startPos, pos - startPos)));

                line++;
                col = 1;
                continue;
            }

            // -- Line continuation: backslash + newline -----------------------
            if (c == '\\' && pos + 1 < text.Length && (text[pos + 1] == '\r' || text[pos + 1] == '\n'))
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                pos++;
                col++;
                if (text[pos] == '\r' && pos + 1 < text.Length && text[pos + 1] == '\n')
                    pos += 2;
                else
                    pos += 1;

                builder.Add(new TherionToken(
                    TherionTokenKind.LineContinuation,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    text.Substring(startPos, pos - startPos)));

                line++;
                col = 1;
                continue;
            }

            // -- Whitespace (spaces / tabs) -----------------------------------
            if (c == ' ' || c == '\t')
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t'))
                {
                    pos++;
                    col++;
                }
                builder.Add(new TherionToken(
                    TherionTokenKind.Whitespace,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    text.Substring(startPos, pos - startPos)));
                continue;
            }

            // -- Line comment: # to end-of-line --------------------------------
            if (c == '#')
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                while (pos < text.Length && text[pos] != '\r' && text[pos] != '\n')
                {
                    pos++;
                    col++;
                }
                builder.Add(new TherionToken(
                    TherionTokenKind.LineComment,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    text.Substring(startPos, pos - startPos)));
                continue;
            }

            // -- String literal: double-quoted, allows internal newlines -------
            if (c == '"')
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                pos++;
                col++;
                while (pos < text.Length && text[pos] != '"')
                {
                    if (text[pos] == '\n') { line++; col = 1; }
                    else col++;
                    pos++;
                }
                if (pos < text.Length) { pos++; col++; } // closing quote
                builder.Add(new TherionToken(
                    TherionTokenKind.String,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    text.Substring(startPos, pos - startPos)));
                continue;
            }

            // -- Number: optional sign + digits + optional fraction + exponent.
            // Sign is only consumed when followed by a digit so that bare '-' /
            // '+' on options (e.g. "-foo") falls through to the identifier branch.
            if (IsNumberStart(text, pos))
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                if (text[pos] is '+' or '-') { pos++; col++; }
                while (pos < text.Length && char.IsDigit(text[pos])) { pos++; col++; }
                if (pos < text.Length && text[pos] == '.')
                {
                    pos++; col++;
                    while (pos < text.Length && char.IsDigit(text[pos])) { pos++; col++; }
                }
                if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E'))
                {
                    pos++; col++;
                    if (pos < text.Length && (text[pos] == '+' || text[pos] == '-')) { pos++; col++; }
                    while (pos < text.Length && char.IsDigit(text[pos])) { pos++; col++; }
                }
                builder.Add(new TherionToken(
                    TherionTokenKind.Number,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    text.Substring(startPos, pos - startPos)));
                continue;
            }

            // -- Punctuation ----------------------------------------------------
            if (IsPunctuation(c))
            {
                builder.Add(new TherionToken(
                    TherionTokenKind.Punctuation,
                    MakeSpan(filePath, line, col, line, col + 1, pos, 1),
                    c.ToString()));
                pos++;
                col++;
                continue;
            }

            // -- Identifier / bareword: greedy until whitespace, EOL or
            //    structural punctuation. Therion identifiers are unusually
            //    permissive (may contain '.', '@', etc.).
            {
                int startPos = pos;
                int startLine = line;
                int startCol = col;
                while (pos < text.Length && !IsIdentifierBreak(text[pos]))
                {
                    pos++;
                    col++;
                }
                int len = pos - startPos;
                if (len == 0)
                {
                    // Defensive: skip one char to guarantee progress, recording it as punctuation.
                    builder.Add(new TherionToken(
                        TherionTokenKind.Punctuation,
                        MakeSpan(filePath, startLine, startCol, line, col + 1, startPos, 1),
                        text[startPos].ToString()));
                    pos++;
                    col++;
                    continue;
                }
                builder.Add(new TherionToken(
                    TherionTokenKind.Identifier,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, len),
                    text.Substring(startPos, len)));
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsNumberStart(string text, int pos)
    {
        char c = text[pos];
        if (char.IsDigit(c)) return true;
        if (c is '+' or '-')
        {
            if (pos + 1 < text.Length && char.IsDigit(text[pos + 1])) return true;
            if (pos + 2 < text.Length && text[pos + 1] == '.' && char.IsDigit(text[pos + 2])) return true;
        }
        if (c == '.' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])) return true;
        return false;
    }

    private static bool IsPunctuation(char c) =>
        c is '=' or ',' or '[' or ']' or '{' or '}' or '(' or ')' or ':' or ';';

    private static bool IsIdentifierBreak(char c) =>
        c is ' ' or '\t' or '\r' or '\n' or '#' or '"' || IsPunctuation(c);

    private static SourceSpan MakeSpan(
        string filePath, int startLine, int startCol, int endLine, int endCol,
        int startOffset, int length) =>
        new(filePath,
            new SourceLocation(startLine, startCol),
            new SourceLocation(endLine, endCol),
            startOffset,
            length);
}
