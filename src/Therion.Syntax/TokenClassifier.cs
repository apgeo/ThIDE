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
    /// <summary>A declared/referenced identifier name (survey/scrap/map name, station ref, ...).</summary>
    Identifier,
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
        "extend", "units", "calibrate", "declination", "grade", "endgrade", "infer", "mark",
        "flags", "sd", "explo-date", "explo-team", "instrument", "import",
        "grid-angle", "break", "walls", "vthreshold", "station-names",
        "surface", "endsurface", "scan", "endscan",
        // .th2
        "scrap", "endscrap", "point", "line", "endline", "area", "endarea",
        "encoding", "sketch", "map", "endmap", "join", "layer", "break", "preview",
        "comment", "endcomment",
        // .thconfig
        "source", "layout", "endlayout", "lookup", "endlookup", "export", "select", "cs", "system-charset",
        "language", "lang", "translate", "revise", "group", "endgroup",
    };

    /// <summary>The known Therion command/block keywords (used for editor autocomplete).</summary>
    public static IReadOnlyCollection<string> Keywords => KnownKeywords;

    // Keywords whose following bare-word arguments on the same line are identifier names or
    // station references (so the editor can highlight them distinctly from keywords, #1).
    private static readonly HashSet<string> NameIntroducers = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "survey", "scrap", "map", "station", "fix", "equate", "join", "select",
    };

    /// <summary>Classify the given token stream.</summary>
    public static ImmutableArray<ClassifiedSpan> Classify(ImmutableArray<TherionToken> tokens)
    {
        var result = ImmutableArray.CreateBuilder<ClassifiedSpan>(tokens.Length);
        // Once a name-introducer keyword is seen, the remaining bare words on that line are
        // treated as identifiers. Reset at each line boundary.
        bool namesFollow = false;
        // Inside a "map ... endmap" block every bare word on its body lines is a scrap/map
        // reference (the members being composed) — those are identifiers too, but they are NOT
        // preceded by an introducer keyword on their own line, so they need block-level state
        // that survives line boundaries (until "endmap"). Same for "endsurvey"-style refs (#10).
        bool inMapBlock = false;
        foreach (var t in tokens)
        {
            TokenClassification c;
            switch (t.Kind)
            {
                case TherionTokenKind.LineComment:      c = TokenClassification.Comment; break;
                case TherionTokenKind.String:           c = TokenClassification.String; break;
                case TherionTokenKind.Number:           c = TokenClassification.Number; break;
                case TherionTokenKind.Punctuation:      c = TokenClassification.Punctuation; break;
                case TherionTokenKind.Whitespace:
                case TherionTokenKind.LineContinuation: c = TokenClassification.Whitespace; break;
                case TherionTokenKind.NewLine:
                    namesFollow = false; c = TokenClassification.Whitespace; break;
                case TherionTokenKind.Identifier:
                    c = ClassifyIdentifier(t.Text, ref namesFollow, ref inMapBlock); break;
                default:                                c = TokenClassification.Text; break;
            }
            result.Add(new ClassifiedSpan(t.Span, c));
        }
        return result.MoveToImmutable();
    }

    /// <summary>
    /// Classify a single <em>layout body</em> line. Used only for lines a
    /// <see cref="LayoutRegionScanner"/> marked <see cref="EmbeddedRegion.LayoutOption"/>: the leading
    /// bare word is the option key (highlighted as a keyword when it is a known layout option, e.g.
    /// <c>scale</c>, <c>legend</c>, <c>symbol-hide</c>), and the rest classify by token kind. This is
    /// kept separate from <see cref="Classify"/> so the global keyword set is not polluted with option
    /// names like <c>scale</c>/<c>color</c>/<c>units</c> that mean other things outside a layout.
    /// </summary>
    public static ImmutableArray<ClassifiedSpan> ClassifyLayoutLine(ImmutableArray<TherionToken> tokens)
    {
        var result = ImmutableArray.CreateBuilder<ClassifiedSpan>(tokens.Length);
        bool keySeen = false;
        foreach (var t in tokens)
        {
            TokenClassification c;
            switch (t.Kind)
            {
                case TherionTokenKind.LineComment:      c = TokenClassification.Comment; break;
                case TherionTokenKind.String:           c = TokenClassification.String; break;
                case TherionTokenKind.Number:           c = TokenClassification.Number; break;
                case TherionTokenKind.Punctuation:      c = TokenClassification.Punctuation; break;
                case TherionTokenKind.Whitespace:
                case TherionTokenKind.LineContinuation:
                case TherionTokenKind.NewLine:          c = TokenClassification.Whitespace; break;
                case TherionTokenKind.Identifier:
                    if (t.Text.Length > 0 && t.Text[0] == '-') c = TokenClassification.Option; // -flag
                    else if (!keySeen)
                    {
                        keySeen = true;
                        c = LayoutKeywords.IsKnown(t.Text)
                            ? TokenClassification.Keyword
                            : TokenClassification.Text;
                    }
                    else c = TokenClassification.Text;
                    break;
                default:                                c = TokenClassification.Text; break;
            }
            result.Add(new ClassifiedSpan(t.Span, c));
        }
        return result.MoveToImmutable();
    }

    private static TokenClassification ClassifyIdentifier(string text, ref bool namesFollow, ref bool inMapBlock)
    {
        if (text.Length > 0 && text[0] == '-') return TokenClassification.Option; // option flag
        if (KnownKeywords.Contains(text))
        {
            // Track map-block entry/exit so the member references on the body lines highlight.
            if (text.Equals("map", System.StringComparison.OrdinalIgnoreCase)) inMapBlock = true;
            else if (text.Equals("endmap", System.StringComparison.OrdinalIgnoreCase)) inMapBlock = false;
            namesFollow = NameIntroducers.Contains(text);
            return TokenClassification.Keyword;
        }
        // A bare word following a name-introducer keyword — or any bare word on a map body line —
        // is an identifier/station/scrap reference.
        return (namesFollow || inMapBlock) ? TokenClassification.Identifier : TokenClassification.Text;
    }
}
