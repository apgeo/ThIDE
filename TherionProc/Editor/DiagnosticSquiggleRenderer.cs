// Implementation Plan §7.3 / D5 — diagnostic squiggle adornments in the editor.
// IBackgroundRenderer that paints rustc-style wavy underlines for each diagnostic
// whose SourceSpan falls inside the bound document.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Therion.Core;

namespace TherionProc.Editor;

internal sealed class DiagnosticSquiggleRenderer : IBackgroundRenderer
{
    private readonly TextView _view;
    private IReadOnlyList<Diagnostic> _diagnostics = Array.Empty<Diagnostic>();
    private string? _filePath;

    public DiagnosticSquiggleRenderer(TextView view) => _view = view;

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetFilePath(string? path)
    {
        _filePath = path;
        _view.InvalidateLayer(Layer);
    }

    public void SetDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        _diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        _view.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_diagnostics.Count == 0 || textView.Document is null) return;

        foreach (var d in _diagnostics)
        {
            if (d.Span.IsEmpty) continue;
            if (!MatchesCurrentFile(d.Span)) continue;

            var line = d.Span.Start.Line;
            var col = Math.Max(1, d.Span.Start.Column);
            if (line < 1 || line > textView.Document.LineCount) continue;

            int startOffset, endOffset;
            try
            {
                startOffset = textView.Document.GetOffset(line, col);
                var length = Math.Max(1, d.Span.Length);
                endOffset = Math.Min(startOffset + length, textView.Document.TextLength);
            }
            catch { continue; }
            if (endOffset <= startOffset) continue;

            var segment = new TextSegmentLike(startOffset, endOffset - startOffset);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawWavyLine(drawingContext, rect, ColorFor(d.Severity));
            }
        }
    }

    private bool MatchesCurrentFile(SourceSpan span)
    {
        if (string.IsNullOrEmpty(_filePath)) return true; // single-file mode
        return string.Equals(span.FilePath, _filePath, StringComparison.OrdinalIgnoreCase);
    }

    private static IBrush ColorFor(DiagnosticSeverity sev) => sev switch
    {
        DiagnosticSeverity.Error   => Brushes.Red,
        DiagnosticSeverity.Warning => Brushes.DarkOrange,
        DiagnosticSeverity.Info    => Brushes.SteelBlue,
        _                          => Brushes.Gray,
    };

    private static void DrawWavyLine(DrawingContext dc, Rect rect, IBrush brush)
    {
        const double amplitude = 1.2;
        const double waveLen = 4.0;
        double y = rect.Bottom - 0.5;
        var pen = new Pen(brush, 1.0);

        double x = rect.Left;
        var prev = new Point(x, y);
        bool up = true;
        while (x < rect.Right)
        {
            x = Math.Min(rect.Right, x + waveLen / 2);
            var next = new Point(x, up ? y - amplitude : y + amplitude);
            dc.DrawLine(pen, prev, next);
            prev = next;
            up = !up;
        }
    }

    // Minimal ISegment implementation so we don't allocate AvaloniaEdit's TextSegment per draw.
    private sealed class TextSegmentLike : AvaloniaEdit.Document.ISegment
    {
        public TextSegmentLike(int offset, int length) { Offset = offset; Length = length; }
        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;
    }
}
