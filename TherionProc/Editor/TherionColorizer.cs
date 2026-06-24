// Implementation Plan §7.3 — AvaloniaEdit-backed Therion editor.
// The colorizer pulls from the host-agnostic TokenClassifier so the editor
// shares a single source of truth with the parser. It keeps a light/dark
// palette (so tokens stay legible on either theme) and caches per-line
// classification so scrolling/redraw doesn't re-tokenize unchanged lines.

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

    private Dictionary<TokenClassification, IBrush> _palette = LightPalette;
    private readonly Dictionary<string, ImmutableArray<ClassifiedSpan>> _cache = new();
    private HashSet<int>? _skipLines;

    public void SetVariant(Variant variant) =>
        _palette = variant == Variant.Dark ? DarkPalette : LightPalette;

    /// <summary>
    /// 1-based line numbers to leave unhighlighted (e.g. metapost/tex code inside a
    /// .thconfig <c>layout … endlayout</c> block, which isn't Therion syntax).
    /// </summary>
    public void SetSkipLines(HashSet<int>? lines) =>
        _skipLines = lines is { Count: > 0 } ? lines : null;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_skipLines is not null && _skipLines.Contains(line.LineNumber)) return;

        var doc = CurrentContext.Document;
        var lineText = doc.GetText(line);
        if (string.IsNullOrEmpty(lineText)) return;

        if (!_cache.TryGetValue(lineText, out var classified))
        {
            // Line-local lexing — safe for Therion (no multi-line constructs except
            // rare quoted strings, which a future incremental highlighter can promote).
            var tokens = Tokenizer.Tokenize("<inline>", lineText);
            classified = TokenClassifier.Classify(tokens);
            if (_cache.Count > 4000) _cache.Clear(); // bound memory on huge files
            _cache[lineText] = classified;
        }

        foreach (var span in classified)
        {
            // Identifier highlighting is opt-in via the global toggle (#1); when off, identifier
            // tokens have no palette entry and render as plain text.
            bool isIdentifier = span.Classification == TokenClassification.Identifier;
            if (isIdentifier && !HighlightIdentifiers) continue;
            if (!_palette.TryGetValue(span.Classification, out var brush)) continue;
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

    private static IBrush Brush(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromRgb(r, g, b)).ToImmutable();
}
