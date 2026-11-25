// A strip of time: every child lies along its width from one share of it to another (0 is the
// left end, 1 the right), at full height. The Blender panel shows the captions of a clip on one,
// each where the seconds it is shown for fall. Where a child goes is said on the child itself, or
// on the item container an ItemsControl makes for it, with the attached properties Start and End;
// the view model works the shares out, this only places them.

using System;
using Avalonia;
using Avalonia.Controls;

namespace ThIDE.Views.Docking;

public sealed class CaptionStripPanel : Panel
{
    /// <summary>The narrowest a child is drawn, so a caption of a fraction of a second is seen.</summary>
    public const double MinChildWidth = 3;

    public static readonly AttachedProperty<double> StartProperty =
        AvaloniaProperty.RegisterAttached<CaptionStripPanel, Control, double>("Start");

    public static readonly AttachedProperty<double> EndProperty =
        AvaloniaProperty.RegisterAttached<CaptionStripPanel, Control, double>("End", 1.0);

    static CaptionStripPanel() => AffectsParentArrange<CaptionStripPanel>(StartProperty, EndProperty);

    public static double GetStart(Control control) => control.GetValue(StartProperty);
    public static void SetStart(Control control, double value) => control.SetValue(StartProperty, value);
    public static double GetEnd(Control control) => control.GetValue(EndProperty);
    public static void SetEnd(Control control, double value) => control.SetValue(EndProperty, value);

    /// <summary>Where a child that runs from <paramref name="start"/> to <paramref name="end"/>
    /// lies on a strip <paramref name="width"/> wide: its left edge and its width. Shares
    /// outside 0–1, and ones that are no numbers, are brought inside; a child is never narrower
    /// than <see cref="MinChildWidth"/> and never leaves the strip.</summary>
    internal static (double X, double Width) Span(double start, double end, double width)
    {
        if (!(width > 0)) return (0, 0);
        double from = double.IsFinite(start) ? Math.Clamp(start, 0, 1) : 0;
        double to = double.IsFinite(end) ? Math.Clamp(end, from, 1) : from;
        double w = Math.Min(Math.Max((to - from) * width, MinChildWidth), width);
        double x = Math.Min(from * width, width - w);
        return (x, w);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        // As wide as it is given; it asks for no width of its own.
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var (x, width) = Span(GetStart(child), GetEnd(child), finalSize.Width);
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
        }
        return finalSize;
    }
}
