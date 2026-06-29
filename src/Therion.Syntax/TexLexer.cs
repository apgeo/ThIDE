// LANG-02 (embedded code) — highlighting-grade TeX lexer.
//
// Therion `layout` bodies embed TeX inside `code tex-map … endcode` and `code tex-atlas … endcode`
// (thbook §"layout"). These are TeX/LaTeX, NOT MetaPost — a different language — so they get their
// own lexer. As with MetaPost there is no reusable .NET TeX parser worth a dependency, and we only
// need highlighting-grade tokenizing: control sequences, grouping, math, comments. Emits the shared
// ClassifiedSpan stream (TokenClassification → palette), line-local like the Therion colorizer.

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Line-local TeX tokenizer for syntax highlighting embedded layout code.</summary>
public static class TexLexer
{
    private const string EmbeddedFile = "<tex>";

    /// <summary>Classify a single line of TeX into highlight spans (offsets are line-local).</summary>
    public static ImmutableArray<ClassifiedSpan> Classify(string line)
    {
        var b = ImmutableArray.CreateBuilder<ClassifiedSpan>();
        int i = 0, n = line.Length;
        while (i < n)
        {
            char c = line[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Control sequence: `\` + letters (control word) or `\` + one symbol (e.g. `\%`, `\\`).
            // Handled before `%` so an escaped percent is not mistaken for a comment.
            if (c == '\\')
            {
                int start = i++;
                if (i < n && char.IsLetter(line[i]))
                    while (i < n && char.IsLetter(line[i])) i++;
                else if (i < n) i++; // control symbol
                b.Add(Span(start, i - start, TokenClassification.Keyword));
                continue;
            }

            // `%` line comment — runs to end of line.
            if (c == '%') { b.Add(Span(i, n - i, TokenClassification.Comment)); break; }

            // Grouping / math / specials: { } [ ] $ & # ^ _ ~
            if (IsTexPunct(c))
            {
                int start = i;
                while (i < n && IsTexPunct(line[i])) i++;
                b.Add(Span(start, i - start, TokenClassification.Punctuation));
                continue;
            }

            // Numeric literal.
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < n && (char.IsDigit(line[i]) || line[i] == '.')) i++;
                b.Add(Span(start, i - start, TokenClassification.Number));
                continue;
            }

            // Plain text (letters and any other character).
            {
                int start = i;
                if (char.IsLetter(c)) while (i < n && char.IsLetter(line[i])) i++;
                else i++;
                b.Add(Span(start, i - start, TokenClassification.Text));
            }
        }
        return b.ToImmutable();
    }

    private static bool IsTexPunct(char ch) =>
        ch is '{' or '}' or '[' or ']' or '$' or '&' or '#' or '^' or '_' or '~';

    private static ClassifiedSpan Span(int start, int length, TokenClassification c) =>
        new(new SourceSpan(EmbeddedFile,
            new SourceLocation(1, start + 1), new SourceLocation(1, start + length + 1),
            start, length), c);
}
