// Renders the relational-map edges (lines + arrowheads) beneath the node layer and
// provides hover/click on a line: hovering shows the link description, clicking navigates
// to where the link is written in source. Custom-drawn so edges track node dragging live.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public sealed class RelationalEdgesControl : Control
{
    private RelationalMapViewModel? _vm;
    private Action<Therion.Core.SourceSpan>? _navigate;
    private RelationalEdge? _hovered;

    private static readonly ImmutablePen NormalPen =
        new(new ImmutableSolidColorBrush(Color.FromArgb(0xB0, 0x78, 0x78, 0x78)), 1.4);
    private static readonly ImmutablePen HoverPen =
        new(new ImmutableSolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)), 2.6);
    private static readonly IBrush ArrowBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0x60, 0x60, 0x60));
    private static readonly IBrush ArrowHoverBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));

    public RelationalEdgesControl()
    {
        // The control fills the diagram panel; it must be hit-testable on its drawn lines only,
        // but Avalonia hit-tests the whole bounds — we refine to "near a line" in pointer logic.
        Cursor = Cursor.Default;
    }

    public void Configure(RelationalMapViewModel vm, Action<Therion.Core.SourceSpan> navigate)
    {
        if (_vm is not null) _vm.GraphChanged -= OnGraphChanged;
        _vm = vm;
        _navigate = navigate;
        _vm.GraphChanged += OnGraphChanged;
        InvalidateVisual();
    }

    private void OnGraphChanged(object? sender, EventArgs e) { _hovered = null; InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        // A transparent fill over the full bounds makes the control hit-testable everywhere
        // (a bare Control isn't), so we can detect hover/click near any line.
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_vm is null) return;

        foreach (var edge in _vm.Edges)
        {
            bool hot = ReferenceEquals(edge, _hovered);
            var p1 = new Point(edge.From.CenterX, edge.From.CenterY);
            var p2 = new Point(edge.To.CenterX, edge.To.CenterY);
            var a = ClipToRect(p1, p2, edge.From);
            var b = ClipToRect(p2, p1, edge.To);
            ctx.DrawLine(hot ? HoverPen : NormalPen, a, b);
            DrawArrow(ctx, a, b, hot);
        }
    }

    private static void DrawArrow(DrawingContext ctx, Point from, Point to, bool hot)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var ux = dx / len; var uy = dy / len;
        const double size = 9;
        var basePt = new Point(to.X - ux * size, to.Y - uy * size);
        var left = new Point(basePt.X - uy * (size * 0.5), basePt.Y + ux * (size * 0.5));
        var right = new Point(basePt.X + uy * (size * 0.5), basePt.Y - ux * (size * 0.5));

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(to, true);
            g.LineTo(left);
            g.LineTo(right);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(hot ? ArrowHoverBrush : ArrowBrush, null, geo);
    }

    // Intersection of the segment center→other with the rectangle's border, so lines start/end
    // at the node edge rather than its centre.
    private static Point ClipToRect(Point center, Point toward, RelationalNode node)
    {
        double hw = node.Width / 2, hh = node.Height / 2;
        double dx = toward.X - center.X, dy = toward.Y - center.Y;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) return center;
        double scaleX = dx != 0 ? hw / Math.Abs(dx) : double.PositiveInfinity;
        double scaleY = dy != 0 ? hh / Math.Abs(dy) : double.PositiveInfinity;
        double s = Math.Min(scaleX, scaleY);
        return new Point(center.X + dx * s, center.Y + dy * s);
    }

    // ---- hover / click ------------------------------------------------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null) return;
        var pos = e.GetPosition(this);
        var hit = NearestEdge(pos, out _);
        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered = hit;
            InvalidateVisual();
        }
        if (hit is not null)
        {
            ToolTip.SetTip(this, hit.LinkLabel);
            ToolTip.SetIsOpen(this, true);
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
            Cursor = Cursor.Default;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hovered is not null) { _hovered = null; InvalidateVisual(); }
        ToolTip.SetIsOpen(this, false);
        Cursor = Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null) return;
        // Navigate only on a double-click on the relation line — go to where the relation is
        // defined in source. A single click just selects/keeps the hover highlight.
        if (e.ClickCount < 2) return;
        var pos = e.GetPosition(this);
        var hit = NearestEdge(pos, out _);
        if (hit?.LinkSpan is { } span)
        {
            _navigate?.Invoke(span);
            e.Handled = true;
        }
    }

    private RelationalEdge? NearestEdge(Point p, out double bestDist)
    {
        bestDist = double.MaxValue;
        RelationalEdge? best = null;
        if (_vm is null) return null;
        const double threshold = 6.0;
        foreach (var edge in _vm.Edges)
        {
            var a = ClipToRect(new Point(edge.From.CenterX, edge.From.CenterY),
                               new Point(edge.To.CenterX, edge.To.CenterY), edge.From);
            var b = ClipToRect(new Point(edge.To.CenterX, edge.To.CenterY),
                               new Point(edge.From.CenterX, edge.From.CenterY), edge.To);
            double d = DistanceToSegment(p, a, b);
            if (d < bestDist) { bestDist = d; best = edge; }
        }
        return bestDist <= threshold ? best : null;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double wx = p.X - a.X, wy = p.Y - a.Y;
        double c1 = vx * wx + vy * wy;
        if (c1 <= 0) return Dist(p, a);
        double c2 = vx * vx + vy * vy;
        if (c2 <= c1) return Dist(p, b);
        double t = c1 / c2;
        return Dist(p, new Point(a.X + t * vx, a.Y + t * vy));
    }

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
