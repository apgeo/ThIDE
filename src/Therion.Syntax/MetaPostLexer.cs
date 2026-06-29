// LANG-02 (embedded code) — highlighting-grade MetaPost lexer.
//
// Therion `layout` bodies embed MetaPost inside `code metapost … endcode` (thbook §"layout",
// Therion source thmpost.cxx). There is no mature reusable .NET MetaPost parser — the reference
// implementation is C/web2c — and we only need *highlighting-grade* tokenizing here, not a full
// grammar. So this is a small line-local lexer that emits the same ClassifiedSpan stream the
// Therion colorizer already renders (reusing TokenClassification → palette), matching how the
// editor tokenizes one line at a time (TherionColorizer.ColorizeLine).
//
// NOTE: line-local, like the Therion path. MetaPost comments (`%`) and string literals (`"…"`) are
// single-line, so this is faithful; a multi-line string spanning `code` lines (vanishingly rare)
// would mis-highlight until a future incremental highlighter promotes it.

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Line-local MetaPost tokenizer for syntax highlighting embedded layout code.</summary>
public static class MetaPostLexer
{
    /// <summary>
    /// MetaPost primitives / common macros + Therion drawing macros. Case-sensitive (MetaPost is).
    /// This drives the <see cref="TokenClassification.Keyword"/> colour; it need not be exhaustive —
    /// unknown identifiers simply render as plain text.
    /// </summary>
    private static readonly ImmutableHashSet<string> Keywords =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            // definitions / grouping
            "def", "vardef", "primarydef", "secondarydef", "tertiarydef", "enddef",
            "begingroup", "endgroup", "expr", "suffix", "text", "primary", "secondary", "tertiary",
            "save", "interim", "newinternal", "let", "of", "endfig", "beginfig", "end",
            // control flow
            "if", "elseif", "else", "fi", "for", "endfor", "forever", "forsuffixes",
            "exitif", "exitunless", "upto", "downto", "step", "until", "within",
            // drawing
            "draw", "drawarrow", "drawdblarrow", "drawdot", "fill", "filldraw", "undraw",
            "unfill", "unfilldraw", "clip", "addto", "also", "contour", "doublepath",
            "withcolor", "withrgbcolor", "withcmykcolor", "withgreyscale", "withpen",
            "withprescript", "withpostscript", "dashed", "pickup", "drawoptions",
            // types
            "pen", "picture", "path", "pair", "color", "cmykcolor", "rgbcolor", "numeric",
            "string", "boolean", "transform",
            // diagnostics / io
            "message", "errmessage", "errhelp", "show", "showvariable", "special", "input",
            "scantokens", "readfrom", "write",
            // path/geometry ops + constants
            "cycle", "makepen", "makepath", "reverse", "subpath", "point", "postcontrol",
            "precontrol", "direction", "directiontime", "intersectionpoint", "intersectiontimes",
            "buildcycle", "unitvector", "dir", "angle", "length", "sqrt", "sind", "cosd",
            "mlog", "mexp", "floor", "ceiling", "round", "abs", "max", "min", "true", "false",
            "nullpicture", "nullpen", "origin", "up", "down", "left", "right",
            "halfcircle", "quartercircle", "fullcircle", "unitsquare", "identity",
            "rotated", "scaled", "shifted", "slanted", "zscaled", "xscaled", "yscaled",
            "reflectedabout", "rotatedaround", "transformed", "whatever", "dashpattern",
            "evenly", "withdots", "on", "off",
            // Therion-specific MetaPost macros (thmpost.cxx + thsymbolset)
            "thdraw", "thfill", "thclip", "thwithcolor", "thpoint", "thline", "tharea",
            "T", "fonts_setup", "initsymbol", "p_", "l_", "a_", "s_");

    private const string EmbeddedFile = "<metapost>";

    /// <summary>Classify a single line of MetaPost into highlight spans (offsets are line-local).</summary>
    public static ImmutableArray<ClassifiedSpan> Classify(string line)
    {
        var b = ImmutableArray.CreateBuilder<ClassifiedSpan>();
        int i = 0, n = line.Length;
        while (i < n)
        {
            char c = line[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            // `%` line comment — runs to end of line.
            if (c == '%') { b.Add(Span(i, n - i, TokenClassification.Comment)); break; }

            // "…" string literal (single-line).
            if (c == '"')
            {
                int start = i++;
                while (i < n && line[i] != '"') i++;
                if (i < n) i++; // include closing quote
                b.Add(Span(start, i - start, TokenClassification.String));
                continue;
            }

            // Numeric literal (MetaPost numbers are decimal, no exponent).
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(line[i + 1])))
            {
                int start = i;
                while (i < n && (char.IsDigit(line[i]) || line[i] == '.')) i++;
                b.Add(Span(start, i - start, TokenClassification.Number));
                continue;
            }

            // Identifier / keyword (letters, digits, underscore).
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                var word = line.Substring(start, i - start);
                b.Add(Span(start, i - start,
                    Keywords.Contains(word) ? TokenClassification.Keyword : TokenClassification.Text));
                continue;
            }

            // Run of punctuation/operators (`:=`, `..`, `--`, `(`, `,`, `;`, `#`, …).
            {
                int start = i;
                while (i < n && IsOperatorChar(line[i])) i++;
                if (i == start) i++; // safety: always make progress
                b.Add(Span(start, i - start, TokenClassification.Punctuation));
            }
        }
        return b.ToImmutable();
    }

    private static bool IsOperatorChar(char ch) =>
        !char.IsLetterOrDigit(ch) && ch != '_' && !char.IsWhiteSpace(ch) && ch != '"' && ch != '%';

    private static ClassifiedSpan Span(int start, int length, TokenClassification c) =>
        new(new SourceSpan(EmbeddedFile,
            new SourceLocation(1, start + 1), new SourceLocation(1, start + length + 1),
            start, length), c);
}
