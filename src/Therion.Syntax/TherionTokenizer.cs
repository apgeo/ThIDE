// Implementation Plan �4.1 (Lexer). Hand-rolled tokenizer for the Therion text
// formats (.th / .th2 / .thconfig). Superpower 3.0.0 no longer ships
// TokenizerBuilder<T>; we keep the door open to using TokenListParser<TKind,T>
// in the higher-level parser layer (M2+) by emitting a token list shape that
// can be wrapped if needed.
//
// Therion source-of-truth references:
//   - Comments / line continuation: therion/src/thinput.cxx
//   - Identifier / number / string rules: therion/src/thparse.cxx
// thbook v6.4.0 �2 "General syntax".

using System.Collections.Immutable;
using System.Text;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// A single lexical token with its absolute <see cref="SourceSpan"/> and its text.
/// <para>
/// <b>Text contract:</b> significant tokens (identifiers, numbers, strings, punctuation, comments)
/// carry their <em>verbatim</em> source slice. <em>Trivia</em> tokens — <see cref="TherionTokenKind
/// .Whitespace"/>, <see cref="TherionTokenKind.NewLine"/> and <see cref="TherionTokenKind
/// .LineContinuation"/> — carry <see cref="string.Empty"/>: their content is uniquely determined by
/// the <see cref="Span"/> (and recoverable from the source), and materializing it would allocate a
/// string per token for text no consumer reads. Use the <see cref="Span"/> if you need trivia text.
/// </para>
/// </summary>
public readonly record struct TherionToken(TherionTokenKind Kind, SourceSpan Span, string Text)
{
    public override string ToString() => $"{Kind}('{Text}') @ {Span}";
}

/// <summary>
/// Hand-rolled tokenizer for the Therion text formats. Never throws � invalid
/// characters are reported through the caller's diagnostic channel by inspecting
/// the resulting tokens.
/// </summary>
public sealed class TherionTokenizer
{
    /// <summary>Tokenize <paramref name="text"/> into a flat token list.</summary>
    public ImmutableArray<TherionToken> Tokenize(string filePath, string text)
    {
        // Pre-size to a rough token estimate (~1 token / 4 chars) to avoid repeated doubling+copy of
        // the growing builder. Over-estimates are trimmed by ToImmutable; under-estimates just grow.
        var builder = ImmutableArray.CreateBuilder<TherionToken>(Math.Max(16, text.Length / 4));

        int pos = 0;
        int line = 1;
        int col = 1;
        // Index in `builder` where the current physical line's tokens begin — used to detect a
        // `surface` header line so its (potentially huge DEM) body can be collapsed opaquely.
        int lineStartIdx = 0;

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

                // Trivia (whitespace / newline / continuation) carries empty Text — see the contract
                // note on TherionToken. Its content is fully recoverable from the Span; skipping the
                // Substring removes the bulk of the tokenizer's per-token allocations.
                builder.Add(new TherionToken(
                    TherionTokenKind.NewLine,
                    MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                    string.Empty));

                bool wasSurfaceHeader = LineFirstSignificantIs(builder, lineStartIdx, "surface");
                line++;
                col = 1;
                lineStartIdx = builder.Count;
                // A `surface … endsurface` body is opaque to every consumer (DEM grids can be tens
                // of MB). Collapse it to one token so we don't allocate millions of number tokens.
                if (wasSurfaceHeader)
                    TryCollapseSurfaceBody(text, filePath, builder, ref pos, ref line, ref col, ref lineStartIdx);
                continue;
            }

            // -- Line continuation: backslash + (optional trailing ws) + newline.
            // Therion tolerates whitespace between the '\' and the line break, so a line ending
            // in "\   " still continues. (A '\' followed by anything else — e.g. a Windows path
            // separator "rez\grind" — is NOT a continuation and falls through to identifier.)
            if (c == '\\')
            {
                int look = pos + 1;
                while (look < text.Length && (text[look] == ' ' || text[look] == '\t')) look++;
                if (look < text.Length && (text[look] == '\r' || text[look] == '\n'))
                {
                    int startPos = pos;
                    int startLine = line;
                    int startCol = col;
                    col += look - pos;
                    pos = look;
                    if (text[pos] == '\r' && pos + 1 < text.Length && text[pos + 1] == '\n')
                        pos += 2;
                    else
                        pos += 1;

                    builder.Add(new TherionToken(
                        TherionTokenKind.LineContinuation,
                        MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                        string.Empty));   // trivia carries empty Text (recoverable from Span)

                    line++;
                    col = 1;
                    continue;
                }
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
                    string.Empty));   // trivia carries empty Text (recoverable from Span)
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
                // A "number" glued directly to more identifier characters (no separator) is really a
                // bareword / station name — e.g. "0@entrance" (cross-reference), "2046-81_ponor",
                // "12abc". Keep consuming it as a single identifier so digit-leading names and
                // point@survey references don't get split (which broke equate resolution).
                if (pos < text.Length && !IsIdentifierBreak(text[pos]))
                {
                    while (pos < text.Length && !IsIdentifierBreak(text[pos])) { pos++; col++; }
                    builder.Add(new TherionToken(
                        TherionTokenKind.Identifier,
                        MakeSpan(filePath, startLine, startCol, line, col, startPos, pos - startPos),
                        text.Substring(startPos, pos - startPos)));
                    continue;
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

    // ---- opaque `surface … endsurface` body collapse --------------------

    /// <summary>True if the first significant token of <c>builder[startIdx..]</c> equals <paramref name="keyword"/>.</summary>
    private static bool LineFirstSignificantIs(
        ImmutableArray<TherionToken>.Builder builder, int startIdx, string keyword)
    {
        for (int i = startIdx; i < builder.Count; i++)
        {
            var k = builder[i].Kind;
            if (k is TherionTokenKind.Whitespace or TherionTokenKind.LineContinuation
                or TherionTokenKind.LineComment or TherionTokenKind.NewLine) continue;
            return string.Equals(builder[i].Text, keyword, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// When positioned at the first body line of a <c>surface</c> block, scans to the matching
    /// <c>endsurface</c> and emits the whole body as one opaque token (advancing past it). Does
    /// nothing if there is no matching <c>endsurface</c> or the body is empty — so a stray
    /// <c>surface</c> elsewhere can never swallow the rest of the file.
    /// </summary>
    private static void TryCollapseSurfaceBody(
        string text, string filePath, ImmutableArray<TherionToken>.Builder builder,
        ref int pos, ref int line, ref int col, ref int lineStartIdx)
    {
        int bodyStart = pos;
        if (!FindEndsurfaceLine(text, bodyStart, out int endLineStart)) return;
        int bodyEnd = NewlineStart(text, endLineStart);   // exclude the newline before endsurface
        if (bodyEnd <= bodyStart) return;                 // empty body — let normal lexing handle it

        int newlines = 0;
        for (int i = bodyStart; i < bodyEnd; i++) if (text[i] == '\n') newlines++;

        builder.Add(new TherionToken(
            TherionTokenKind.Identifier,
            new SourceSpan(filePath,
                new SourceLocation(line, 1), new SourceLocation(line + newlines, 1),
                bodyStart, bodyEnd - bodyStart),
            text.Substring(bodyStart, bodyEnd - bodyStart)));

        line += newlines;
        col = 1;
        pos = bodyEnd;
        lineStartIdx = builder.Count;
    }

    /// <summary>Finds the start offset of the next line whose first word is <c>endsurface</c>.</summary>
    private static bool FindEndsurfaceLine(string text, int from, out int endLineStart)
    {
        int lineStart = from;
        while (lineStart < text.Length)
        {
            int j = lineStart;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
            if (IsKeywordAt(text, j, "endsurface")) { endLineStart = lineStart; return true; }
            int k = lineStart;
            while (k < text.Length && text[k] != '\n' && text[k] != '\r') k++;
            if (k >= text.Length) break;
            k += text[k] == '\r' && k + 1 < text.Length && text[k + 1] == '\n' ? 2 : 1;
            lineStart = k;
        }
        endLineStart = -1;
        return false;
    }

    /// <summary>True if the lowercase <paramref name="keyword"/> sits at <paramref name="j"/> as a whole word.</summary>
    private static bool IsKeywordAt(string text, int j, string keyword)
    {
        if (j + keyword.Length > text.Length) return false;
        for (int i = 0; i < keyword.Length; i++)
            if (char.ToLowerInvariant(text[j + i]) != keyword[i]) return false;
        int after = j + keyword.Length;
        if (after >= text.Length) return true;
        char c = text[after];
        return c is ' ' or '\t' or '\r' or '\n';
    }

    /// <summary>The start offset of the newline sequence that terminates the line before <paramref name="lineStart"/>.</summary>
    private static int NewlineStart(string text, int lineStart)
    {
        if (lineStart <= 0) return 0;
        int p = lineStart - 1;
        if (text[p] == '\n') return p >= 1 && text[p - 1] == '\r' ? p - 1 : p;
        if (text[p] == '\r') return p;
        return lineStart;
    }
}
