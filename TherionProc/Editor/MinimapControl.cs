// a lightweight document minimap. Renders one thin bar per source line (width ∝ line
// length, colour by content: comment / block header / normal), a translucent viewport box for the
// visible region, and click/drag to scroll. Deliberately simple (no scaled text glyphs) so it stays
// cheap even on large files (lines are sampled above a threshold).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;

namespace TherionProc.Editor;

internal sealed class MinimapControl : Control
{
    private static readonly IBrush BgBrush       = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));
    private static readonly IBrush NormalBrush   = new SolidColorBrush(Color.FromArgb(0x80, 0x88, 0x88, 0x88));
    private static readonly IBrush CommentBrush  = new SolidColorBrush(Color.FromArgb(0x80, 0x4C, 0xAF, 0x50));
    private static readonly IBrush HeaderBrush   = new SolidColorBrush(Color.FromArgb(0xC0, 0x33, 0x99, 0xFF));
    private static readonly IBrush ViewportBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80));
    private static readonly IPen   ViewportPen   = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)), 1);

    private readonly TextEditor _editor;

    public MinimapControl(TextEditor editor)
    {
        _editor = editor;
        Width = 84;
        ClipToBounds = true;
        _editor.TextChanged += (_, _) => InvalidateVisual();
        _editor.TextArea.TextView.VisualLinesChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var doc = _editor.Document;
        double w = Bounds.Width, h = Bounds.Height;
        if (doc is null || w <= 0 || h <= 0) return;
        int lineCount = doc.LineCount;
        if (lineCount == 0) return;

        dc.FillRectangle(BgBrush, new Rect(0, 0, w, h));

        double scale = h / lineCount;          // vertical px per source line
        double rowH = Math.Clamp(scale, 1.0, 3.0);
        double maxBarW = Math.Max(2, w - 6);
        int step = Math.Max(1, (int)Math.Ceiling(lineCount / 4000.0)); // sample huge files

        for (int ln = 1; ln <= lineCount; ln += step)
        {
            var line = doc.GetLineByNumber(ln);
            if (line.Length == 0) continue;
            var text = doc.GetText(line);
            var trimmed = text.TrimStart();
            var brush =
                trimmed.StartsWith('#') ? CommentBrush :
                (TherionBlocks.OpenerType(TherionBlocks.FirstWord(text)) is not null ||
                 TherionBlocks.CloserType(TherionBlocks.FirstWord(text)) is not null) ? HeaderBrush :
                NormalBrush;
            double y = (ln - 1) * scale;
            double barW = Math.Min(maxBarW, 2 + line.Length);
            dc.FillRectangle(brush, new Rect(3, y, barW, rowH));
        }

        // Viewport box for the visible region. VisualLines throws VisualLinesInvalidException if
        // accessed while invalid (e.g. during a compositor paint triggered by the completion popup,
        // before the TextView has re-laid out). Skip the box this frame; VisualLinesChanged
        // re-invalidates us once the lines are valid again.
        var view = _editor.TextArea.TextView;
        if (view is { VisualLinesValid: true } && view.VisualLines.Count > 0)
        {
            int top = view.VisualLines[0].FirstDocumentLine.LineNumber;
            int bot = view.VisualLines[^1].LastDocumentLine.LineNumber;
            double y1 = (top - 1) * scale;
            double height = Math.Max(6, (bot - top + 1) * scale);
            dc.FillRectangle(ViewportBrush, new Rect(0, y1, w, height));
            dc.DrawRectangle(null, ViewportPen, new Rect(0.5, y1 + 0.5, w - 1, height - 1));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ScrollToY(e.GetPosition(this).Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            ScrollToY(e.GetPosition(this).Y);
    }

    private void ScrollToY(double y)
    {
        var doc = _editor.Document;
        double h = Bounds.Height;
        if (doc is null || h <= 0) return;
        int line = (int)Math.Clamp(y / h * doc.LineCount + 1, 1, doc.LineCount);
        _editor.ScrollToLine(line);
        InvalidateVisual();
    }
}
