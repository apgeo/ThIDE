// VIS-02 — custom-drawn centreline sketch. Auto-fits the world segments into the control, with
// drag-to-pan, wheel-to-zoom, and click-a-leg to raise SegmentActivated (the VM navigates to it).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Therion.Core;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public sealed class LivePreviewControl : Control
{
    private static readonly ImmutablePen LegPen =
        new(new ImmutableSolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)), 1.3);
    private static readonly IBrush StationBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0x42, 0x42, 0x42));
    private static readonly IBrush Backdrop =
        new ImmutableSolidColorBrush(Color.FromArgb(0x12, 0x80, 0x80, 0x80));

    public static readonly StyledProperty<IReadOnlyList<SketchSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<SketchSegment>?>(nameof(Segments));

    public IReadOnlyList<SketchSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>Raised when the user clicks (without dragging) near a leg.</summary>
    public event EventHandler<SourceSpan>? SegmentActivated;

    private double _zoom = 1.0;
    private double _panX, _panY;
    private double _eff, _cx, _cy; // last render transform (world→screen), for hit-testing
    private Point _lastPointer;
    private bool _panning, _dragged;

    static LivePreviewControl()
    {
        AffectsRender<LivePreviewControl>(SegmentsProperty);
    }

    public LivePreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty) { _panX = _panY = 0; _zoom = 1.0; InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        // A faint fill gives the panel a backdrop and makes it hit-testable everywhere for panning.
        ctx.FillRectangle(Backdrop, new Rect(size));

        var segs = Segments;
        if (segs is null || segs.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in segs)
        {
            minX = Math.Min(minX, Math.Min(s.X1, s.X2)); maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
            minY = Math.Min(minY, Math.Min(s.Y1, s.Y2)); maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
        }
        double worldW = Math.Max(1e-6, maxX - minX), worldH = Math.Max(1e-6, maxY - minY);
        double fit = Math.Min((size.Width - 24) / worldW, (size.Height - 24) / worldH);
        if (fit <= 0 || double.IsInfinity(fit)) fit = 1;
        _eff = fit * _zoom;
        _cx = (minX + maxX) / 2; _cy = (minY + maxY) / 2;

        foreach (var s in segs)
        {
            var p1 = ToScreen(s.X1, s.Y1, size);
            var p2 = ToScreen(s.X2, s.Y2, size);
            ctx.DrawLine(LegPen, p1, p2);
        }
        // Station dots only when the layout isn't too dense.
        if (segs.Count <= 1500)
            foreach (var s in segs)
            {
                ctx.DrawEllipse(StationBrush, null, ToScreen(s.X2, s.Y2, size), 1.6, 1.6);
                ctx.DrawEllipse(StationBrush, null, ToScreen(s.X1, s.Y1, size), 1.6, 1.6);
            }
    }

    private Point ToScreen(double wx, double wy, Size size) =>
        new((wx - _cx) * _eff + size.Width / 2 + _panX, (wy - _cy) * _eff + size.Height / 2 + _panY);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _panning = true; _dragged = false;
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning) return;
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointer.X; var dy = p.Y - _lastPointer.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 2) _dragged = true;
        _panX += dx; _panY += dy; _lastPointer = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _panning = false;
        e.Pointer.Capture(null);
        if (!_dragged) ActivateAt(e.GetPosition(this));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        _zoom = Math.Clamp(_zoom * factor, 0.05, 50);
        InvalidateVisual();
        e.Handled = true;
    }

    // Pick the nearest leg (in screen space) to the click and raise it if close enough.
    private void ActivateAt(Point click)
    {
        var segs = Segments;
        if (segs is null) return;
        var size = Bounds.Size;
        double best = 8.0; // px threshold
        SourceSpan? bestSpan = null;
        foreach (var s in segs)
        {
            var d = DistanceToSegment(click, ToScreen(s.X1, s.Y1, size), ToScreen(s.X2, s.Y2, size));
            if (d < best) { best = d; bestSpan = s.Span; }
        }
        if (bestSpan is { } span) SegmentActivated?.Invoke(this, span);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-9) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private static double Distance(Point p, Point q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
}
