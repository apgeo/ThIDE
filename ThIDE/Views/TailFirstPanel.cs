// A horizontal row that, when it is too narrow, gives up its leading children instead of its trailing
// ones: the status-bar path keeps the file name and its nearest folders whole and shows "…" where the
// root-side folders were. A plain StackPanel clipped on the right and cut the file name short.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ThIDE.Views;

public sealed class TailFirstPanel : Panel
{
    // The leading "…" is a visual child of its own, after the item containers; it is not one of Children,
    // so the ItemsControl that owns those never sees it.
    private readonly Control _ellipsis;
    private int _firstShown;

    public TailFirstPanel() : this(new TextBlock
    {
        Text = "…", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
    }) { }

    /// <summary>With a stand-in for the "…" (tests measure without a font manager).</summary>
    internal TailFirstPanel(Control ellipsis)
    {
        _ellipsis = ellipsis;
        ClipToBounds = true;
        VisualChildren.Add(_ellipsis);
    }

    /// <summary>
    /// The index of the first child that fits when <paramref name="widths"/> are laid out right-aligned in
    /// <paramref name="available"/>, leaving room for a leading ellipsis of <paramref name="ellipsisWidth"/>
    /// whenever any child is dropped. The last child is always kept, even when it alone is too wide.
    /// </summary>
    internal static int FirstShown(IReadOnlyList<double> widths, double available, double ellipsisWidth)
    {
        double total = 0;
        foreach (var w in widths) total += w;
        if (total <= available) return 0;

        int first = widths.Count;
        double used = ellipsisWidth;
        while (first > 0 && used + widths[first - 1] <= available) used += widths[--first];
        return Math.Min(first, Math.Max(widths.Count - 1, 0));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        var widths = new double[Children.Count];
        double height = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(unbounded);
            widths[i] = child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        _ellipsis.Measure(unbounded);
        double ellipsisWidth = _ellipsis.DesiredSize.Width;

        _firstShown = FirstShown(widths, availableSize.Width, ellipsisWidth);
        double width = _firstShown > 0 ? ellipsisWidth : 0;
        for (int i = _firstShown; i < widths.Length; i++) width += widths[i];
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Dropped children (and the ellipsis when nothing is dropped) are parked left of the panel, where
        // the clip hides them and hit testing cannot reach them; the shown ones follow the ellipsis.
        double ellipsisWidth = _ellipsis.DesiredSize.Width;
        double parked = 0;
        for (int i = 0; i < _firstShown; i++) parked += Children[i].DesiredSize.Width;

        double px = -parked - ellipsisWidth, x = 0;
        if (_firstShown > 0) { _ellipsis.Arrange(new Rect(0, 0, ellipsisWidth, finalSize.Height)); x = ellipsisWidth; }
        else _ellipsis.Arrange(new Rect(-ellipsisWidth, 0, ellipsisWidth, finalSize.Height));

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            double w = child.DesiredSize.Width;
            if (i < _firstShown) { child.Arrange(new Rect(px, 0, w, finalSize.Height)); px += w; }
            else { child.Arrange(new Rect(x, 0, w, finalSize.Height)); x += w; }
        }
        return finalSize;
    }
}
