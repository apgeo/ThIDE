// A thin gutter beside the editor that paints a tick for every diagnostic,
// positioned proportionally to its line within the whole document (VS-style
// "overview ruler"). Clicking anywhere scrolls the editor to that line, so the
// ruler doubles as a quick way to jump between errors/warnings on long files.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using Therion.Core;

namespace ThIDE.Editor;

internal sealed class DiagnosticOverviewRuler : Control
{
    private readonly TextEditor _editor;
    private IReadOnlyList<Diagnostic> _diagnostics = Array.Empty<Diagnostic>();
    private string? _filePath;

    public DiagnosticOverviewRuler(TextEditor editor)
    {
        _editor = editor;
        Width = 14;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void SetDiagnostics(IReadOnlyList<Diagnostic>? diagnostics)
    {
        _diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        InvalidateVisual();
    }

    public void SetFilePath(string? path)
    {
        _filePath = path;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var doc = _editor.Document;
        if (doc is null || doc.LineCount == 0 || Bounds.Height <= 0) return;

        double h = Bounds.Height;
        double w = Bounds.Width;
        int total = doc.LineCount;

        foreach (var d in _diagnostics)
        {
            if (d.Span.IsEmpty || !MatchesCurrentFile(d.Span)) continue;
            int line = d.Span.Start.Line;
            if (line < 1 || line > total) continue;
            double y = (line - 0.5) / total * h;
            context.FillRectangle(ColorFor(d.Severity), new Rect(2, y - 1.5, Math.Max(1, w - 4), 3));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var doc = _editor.Document;
        if (doc is null || doc.LineCount == 0 || Bounds.Height <= 0) return;

        double y = e.GetPosition(this).Y;
        int line = (int)Math.Clamp(y / Bounds.Height * doc.LineCount + 1, 1, doc.LineCount);
        _editor.ScrollToLine(line);
        _editor.Focus();
        e.Handled = true;
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
}
