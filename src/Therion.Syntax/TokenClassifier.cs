// Implementation Plan �7.3 � host-agnostic syntax classification.
// AvaloniaEdit, Roslyn-style classifiers, or a plain HTML renderer all consume
// the same TokenClassification stream. Drives a single source of truth (the lexer).

using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Semantic classification of a token for editor display.</summary>
public enum TokenClassification
{
    /// <summary>Default text (identifier we didn't classify further).</summary>
    Text,
    /// <summary>A known top-level / block keyword (<c>survey</c>, <c>endsurvey</c>, ...).</summary>
    Keyword,
    /// <summary>A flag option starting with <c>-</c> (e.g., <c>-title</c>).</summary>
    Option,
    /// <summary>A numeric literal.</summary>
    Number,
    /// <summary>A quoted string literal.</summary>
    String,
    /// <summary>A line comment (<c># ...</c>).</summary>
    Comment,
    /// <summary>Structural punctuation.</summary>
    Punctuation,
    /// <summary>Whitespace / line-continuation / newline.</summary>
    Whitespace,
}

/// <summary>A classified slice of source text.</summary>
public readonly record struct ClassifiedSpan(SourceSpan Span, TokenClassification Classification);

/// <summary>
/// Maps the lexer's <see cref="TherionToken"/> stream to a stream of
/// <see cref="ClassifiedSpan"/>s suitable for any editor host.
/// </summary>
public static class TokenClassifier
{
    private static readonly HashSet<string> KnownKeywords = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // .th
        "survey", "endsurvey", "centreline", "centerline", "endcentreline", "endcenterline",
        "data", "fix", "equate", "input", "load", "team", "date", "station",
        "extend", "units", "calibrate", "declination", "grade", "infer", "mark",
        "flags", "sd", "explo-date", "explo-team", "instrument",
        // .th2
        "scrap", "endscrap", "point", "line", "endline", "area", "endarea",
        "encoding", "sketch", "map", "endmap", "join", "layer",
        // .thconfig
        "source", "layout", "export", "select", "cs", "system-charset",
        "language", "lang", "translate", "revise", "group", "endgroup",
    };

    /// <summary>The known Therion command/block keywords (used for editor autocomplete).</summary>
    public static IReadOnlyCollection<string> Keywords => KnownKeywords;

    /// <summary>Classify the given token stream.</summary>
    public static ImmutableArray<ClassifiedSpan> Classify(ImmutableArray<TherionToken> tokens)
    {
        var result = ImmutableArray.CreateBuilder<ClassifiedSpan>(tokens.Length);
        foreach (var t in tokens)
        {
            var c = t.Kind switch
            {
                TherionTokenKind.LineComment       => TokenClassification.Comment,
                TherionTokenKind.String            => TokenClassification.String,
                TherionTokenKind.Number            => TokenClassification.Number,
                TherionTokenKind.Punctuation       => TokenClassification.Punctuation,
                TherionTokenKind.Whitespace        => TokenClassification.Whitespace,
                TherionTokenKind.NewLine           => TokenClassification.Whitespace,
                TherionTokenKind.LineContinuation  => TokenClassification.Whitespace,
                TherionTokenKind.Identifier        => ClassifyIdentifier(t.Text),
                _                                  => TokenClassification.Text,
            };
            result.Add(new ClassifiedSpan(t.Span, c));
        }
        return result.MoveToImmutable();
    }

    private static TokenClassification ClassifyIdentifier(string text)
    {
        if (text.Length > 0 && text[0] == '-') return TokenClassification.Option;
        return KnownKeywords.Contains(text) ? TokenClassification.Keyword : TokenClassification.Text;
    }
}
