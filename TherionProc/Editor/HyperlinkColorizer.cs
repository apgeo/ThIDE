// Renders a single "active" document range as a clickable hyperlink (blue +
// underline). The editor updates the range as the mouse hovers a file path on a
// path-bearing command line, giving the usual code-editor "follow link" affordance.

using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace TherionProc.Editor;

internal sealed class HyperlinkColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush LinkBrush =
        new SolidColorBrush(Color.FromRgb(0, 102, 204)).ToImmutable();

    private int _start = -1;
    private int _length;

    /// <summary>Sets the highlighted range; returns true if it actually changed.</summary>
    public bool SetLink(int start, int length)
    {
        if (_start == start && _length == length) return false;
        _start = start;
        _length = length;
        return true;
    }

    /// <summary>Clears the highlight; returns true if there was one.</summary>
    public bool Clear()
    {
        if (_start < 0 && _length == 0) return false;
        _start = -1;
        _length = 0;
        return true;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_start < 0 || _length <= 0) return;
        int s = Math.Max(_start, line.Offset);
        int e = Math.Min(_start + _length, line.EndOffset);
        if (s >= e) return;

        ChangeLinePart(s, e, el =>
        {
            el.TextRunProperties.SetForegroundBrush(LinkBrush);
            el.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
        });
    }
}
