// Implementation Plan §7.3 — AvaloniaEdit-backed Therion editor.
// The colorizer pulls from the host-agnostic TokenClassifier so the editor
// shares a single source of truth with the parser.

using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Therion.Syntax;

namespace TherionProc.Editor;

/// <summary>
/// AvaloniaEdit colorizer driven by <see cref="TokenClassifier"/>.
/// </summary>
public sealed class TherionColorizer : DocumentColorizingTransformer
{
    private static readonly Dictionary<TokenClassification, IBrush> Brushes = new()
    {
        [TokenClassification.Keyword]     = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
        [TokenClassification.Option]      = new SolidColorBrush(Color.FromRgb(220, 220, 170)),
        [TokenClassification.Number]      = new SolidColorBrush(Color.FromRgb(181, 206, 168)),
        [TokenClassification.String]      = new SolidColorBrush(Color.FromRgb(206, 145, 120)),
        [TokenClassification.Comment]     = new SolidColorBrush(Color.FromRgb(106, 153, 85)),
        [TokenClassification.Punctuation] = new SolidColorBrush(Color.FromRgb(212, 212, 212)),
    };

    private static readonly TherionTokenizer Tokenizer = new();

    protected override void ColorizeLine(DocumentLine line)
    {
        var doc = CurrentContext.Document;
        var lineText = doc.GetText(line);
        if (string.IsNullOrEmpty(lineText)) return;

        // Tokenize just this line for incremental redraw — line-local lexing is
        // safe for Therion (no multi-line constructs except quoted strings, which
        // a future revision can promote to a multi-line state machine).
        var tokens = Tokenizer.Tokenize("<inline>", lineText);
        var classified = TokenClassifier.Classify(tokens);

        foreach (var span in classified)
        {
            if (!Brushes.TryGetValue(span.Classification, out var brush)) continue;
            int start = line.Offset + span.Span.StartOffset;
            int end = start + span.Span.Length;
            if (end > line.EndOffset) end = line.EndOffset;
            if (start >= end) continue;

            ChangeLinePart(start, end, el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
