// Implementation Plan §4.1 (Lexer), §4.2 (Parser).
// Token kinds shared by the .th / .th2 / .thconfig lexer (Superpower).
// The .xvi format uses a separate, simpler tokenizer (§3.1, §4.1).

namespace Therion.Syntax;

/// <summary>Lexical categories produced by <see cref="TherionTokenizer"/>.</summary>
public enum TherionTokenKind
{
    /// <summary>Synthetic; never emitted by the lexer.</summary>
    None = 0,

    /// <summary>Identifier or unknown bareword (commands, station names, options).</summary>
    Identifier,

    /// <summary>Numeric literal (integer or floating point).</summary>
    Number,

    /// <summary>Double-quoted string literal.</summary>
    String,

    /// <summary>Line comment starting with <c>#</c>.</summary>
    LineComment,

    /// <summary>End-of-line marker (significant in Therion syntax).</summary>
    NewLine,

    /// <summary>One or more spaces / tabs (significant for column tracking).</summary>
    Whitespace,

    /// <summary>Backslash at end of line — line continuation marker.</summary>
    LineContinuation,

    /// <summary>Punctuation such as <c>-</c>, <c>/</c>, <c>=</c>, <c>,</c>.</summary>
    Punctuation,
}
