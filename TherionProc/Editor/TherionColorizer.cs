// Implementation Plan §7.3 — AvaloniaEdit-backed Therion editor.
// The colorizer pulls from the host-agnostic TokenClassifier so the editor
// shares a single source of truth with the parser. It keeps a light/dark
// palette (so tokens stay legible on either theme) and caches per-line
// classification so scrolling/redraw doesn't re-tokenize unchanged lines.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Therion.Syntax;

namespace TherionProc.Editor;

public sealed class TherionColorizer : DocumentColorizingTransformer
{
    public enum Variant { Dark, Light }

    /// <summary>
    /// Global toggle (#1): when true, declared/referenced identifiers (survey/scrap/map names,
    /// station refs) are drawn in a distinct blue with a light-bold weight; when false they
    /// render as plain text exactly as before. Flip to revert to the legacy appearance.
    /// </summary>
    public const bool HighlightIdentifiers = true;

    private static readonly FontWeight IdentifierWeight = FontWeight.Medium; // "light bold"

    // Tuned for a dark background (VS-dark-like).
    private static readonly Dictionary<TokenClassification, IBrush> DarkPalette = new()
    {
        [TokenClassification.Keyword]     = Brush(86, 156, 214),
        [TokenClassification.Option]      = Brush(220, 220, 170),
        // Measurement values (numbers) in teal so they stand apart from green comments (#8).
        [TokenClassification.Number]      = Brush(78, 201, 176),
        [TokenClassification.String]      = Brush(206, 145, 120),
        [TokenClassification.Comment]     = Brush(106, 153, 85),
        [TokenClassification.Punctuation] = Brush(212, 212, 212),
        // Identifiers in a brighter cyan-blue, distinct from the keyword blue (#1).
        [TokenClassification.Identifier]  = Brush(120, 190, 255),
    };

    // Tuned for a light background (VS-light-like) — the dark palette's pale
    // yellow/green/near-white were nearly invisible on white.
    private static readonly Dictionary<TokenClassification, IBrush> LightPalette = new()
    {
        [TokenClassification.Keyword]     = Brush(0, 0, 200),
        [TokenClassification.Option]      = Brush(150, 110, 0),
        // Measurement values (numbers) in teal so they stand apart from green comments (#8).
        [TokenClassification.Number]      = Brush(0, 128, 140),
        [TokenClassification.String]      = Brush(163, 21, 21),
        [TokenClassification.Comment]     = Brush(0, 128, 0),
        [TokenClassification.Punctuation] = Brush(90, 90, 90),
        // Identifiers in a deeper azure, distinct from the keyword blue (#1).
        [TokenClassification.Identifier]  = Brush(36, 114, 200),
    };

    private static readonly TherionTokenizer Tokenizer = new();

    // User custom syntax colors (#2): when set, these brushes override the theme palette for
    // the listed classifications across all editors. Raised through ColorsChanged so open
    // editors redraw on change.
    private static Dictionary<TokenClassification, IBrush>? _customColors;
    public static event EventHandler? ColorsChanged;
    public static void SetCustomColors(Dictionary<TokenClassification, IBrush>? overrides)
    {
        _customColors = overrides is { Count: > 0 } ? overrides : null;
        ColorsChanged?.Invoke(null, EventArgs.Empty);
    }

    private Dictionary<TokenClassification, IBrush> _palette = LightPalette;
    // Cache keyed by (region, line text): the same text can classify differently in a Therion line,
    // a layout option line, or an embedded MetaPost/TeX line, so the region must be part of the key.
    private readonly Dictionary<(int Region, string Text), ImmutableArray<ClassifiedSpan>> _cache = new();
    private IReadOnlyDictionary<int, EmbeddedRegion>? _regions;

    public void SetVariant(Variant variant) =>
        _palette = variant == Variant.Dark ? DarkPalette : LightPalette;

    /// <summary>Resolves a token's brush: user override first, then the theme palette.</summary>
    private bool TryBrush(TokenClassification c, out IBrush brush)
    {
        if (_customColors is { } o && o.TryGetValue(c, out var custom)) { brush = custom; return true; }
        return _palette.TryGetValue(c, out brush!);
    }

    /// <summary>
    /// Per-line embedded-language regions, 1-based. Lines mapped to
    /// <see cref="EmbeddedRegion.MetaPost"/> / <see cref="EmbeddedRegion.Tex"/> are highlighted with
    /// the embedded-language lexers; <see cref="EmbeddedRegion.LayoutOption"/> lines use the
    /// layout-aware classifier; <see cref="EmbeddedRegion.None"/> lines are left unhighlighted
    /// (opaque bodies such as <c>lookup</c>). Lines absent from the map are ordinary Therion lines.
    /// </summary>
    public void SetLineRegions(IReadOnlyDictionary<int, EmbeddedRegion>? regions) =>
        _regions = regions is { Count: > 0 } ? regions : null;

    // skip syntax highlighting on pathologically long lines (e.g. a single huge
    // surface/data row). Tokenizing + per-token ChangeLinePart on a multi-thousand-char line
    // stalls scrolling, and caching the whole line as a dictionary key wastes memory. Such a
    // line renders as plain text instead.
    private const int MaxHighlightLineLength = 2000;

    protected override void ColorizeLine(DocumentLine line)
    {
        // Resolve the line's embedded-language region. Absent ⇒ ordinary Therion line.
        EmbeddedRegion? region = null;
        if (_regions is not null && _regions.TryGetValue(line.LineNumber, out var r)) region = r;
        if (region == EmbeddedRegion.None) return; // opaque body (e.g. lookup) — leave unhighlighted

        var doc = CurrentContext.Document;
        if (line.Length > MaxHighlightLineLength) return;
        var lineText = doc.GetText(line);
        if (string.IsNullOrEmpty(lineText)) return;

        var key = (region.HasValue ? (int)region.Value : -1, lineText);
        if (!_cache.TryGetValue(key, out var classified))
        {
            // Line-local lexing — safe for Therion (no multi-line constructs except rare quoted
            // strings) and for the embedded MetaPost/TeX (their comments + strings are single-line).
            classified = ClassifyForRegion(region, lineText);
            if (_cache.Count > 4000) _cache.Clear(); // bound memory on huge files
            _cache[key] = classified;
        }

        foreach (var span in classified)
        {
            // Identifier highlighting is opt-in via the global toggle (#1); when off, identifier
            // tokens have no palette entry and render as plain text.
            bool isIdentifier = span.Classification == TokenClassification.Identifier;
            if (isIdentifier && !HighlightIdentifiers) continue;
            if (!TryBrush(span.Classification, out var brush)) continue;
            int start = line.Offset + span.Span.StartOffset;
            int end = start + span.Span.Length;
            if (end > line.EndOffset) end = line.EndOffset;
            if (start >= end) continue;

            ChangeLinePart(start, end, el =>
            {
                el.TextRunProperties.SetForegroundBrush(brush);
                if (isIdentifier)
                {
                    var tf = el.TextRunProperties.Typeface;
                    el.TextRunProperties.SetTypeface(new Typeface(
                        tf.FontFamily, tf.Style, IdentifierWeight, tf.Stretch));
                }
            });
        }
    }

    /// <summary>Classify one line with the lexer/classifier appropriate to its region.</summary>
    private static ImmutableArray<ClassifiedSpan> ClassifyForRegion(EmbeddedRegion? region, string lineText) =>
        region switch
        {
            EmbeddedRegion.MetaPost     => MetaPostLexer.Classify(lineText),
            EmbeddedRegion.Tex          => TexLexer.Classify(lineText),
            EmbeddedRegion.LayoutOption => TokenClassifier.ClassifyLayoutLine(Tokenizer.Tokenize("<inline>", lineText)),
            _                           => TokenClassifier.Classify(Tokenizer.Tokenize("<inline>", lineText)),
        };

    private static IBrush Brush(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromRgb(r, g, b)).ToImmutable();
}
