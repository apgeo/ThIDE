// VIS-02 — custom-drawn centreline sketch. Auto-fits the world segments into the control, with
// drag-to-pan, wheel-to-zoom, and click-a-leg to raise SegmentActivated (the VM navigates to it).
//
// Debug overlays (to diagnose superimposed / garbled previews): optional per-station labels
// (bare or survey-qualified) and leg colouring by survey / file / connected-component, with a
// matching legend. Colours come from the deterministic SketchColors palette.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Therion.Core;
using Therion.Semantics;
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
    private static readonly IBrush LabelText =
        new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly IBrush LabelHalo =
        new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LegendBack =
        new ImmutableSolidColorBrush(Color.FromArgb(0xE0, 0x2B, 0x2B, 0x2B));
    private static readonly IBrush LegendText =
        new ImmutableSolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

    // Beyond this many distinct stations, labels would be unreadable and slow — suppress them.
    private const int MaxLabelStations = 1500;

    public static readonly StyledProperty<IReadOnlyList<SketchSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<SketchSegment>?>(nameof(Segments));

    public IReadOnlyList<SketchSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    // LEAD-02: lead markers overlaid on the sketch.
    public static readonly StyledProperty<IReadOnlyList<LeadMarker>?> LeadMarkersProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<LeadMarker>?>(nameof(LeadMarkers));

    public IReadOnlyList<LeadMarker>? LeadMarkers
    {
        get => GetValue(LeadMarkersProperty);
        set => SetValue(LeadMarkersProperty, value);
    }

    // Clickable equate-junction markers (where surveys are stitched together).
    public static readonly StyledProperty<IReadOnlyList<EquateMarker>?> EquateMarkersProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<EquateMarker>?>(nameof(EquateMarkers));

    public IReadOnlyList<EquateMarker>? EquateMarkers
    {
        get => GetValue(EquateMarkersProperty);
        set => SetValue(EquateMarkersProperty, value);
    }

    /// <summary>Show the equate-junction markers (on by default).</summary>
    public static readonly StyledProperty<bool> ShowJunctionsProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(ShowJunctions), true);

    public bool ShowJunctions
    {
        get => GetValue(ShowJunctionsProperty);
        set => SetValue(ShowJunctionsProperty, value);
    }

    /// <summary>Draw a small name label at each station.</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(ShowLabels));

    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>Qualify station labels as <c>survey.station</c> instead of the bare station name.</summary>
    public static readonly StyledProperty<bool> ShowSurveyNamesProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(ShowSurveyNames));

    public bool ShowSurveyNames
    {
        get => GetValue(ShowSurveyNamesProperty);
        set => SetValue(ShowSurveyNamesProperty, value);
    }

    /// <summary>Leg colouring: <c>none</c> | <c>survey</c> | <c>file</c> | <c>component</c>.</summary>
    public static readonly StyledProperty<string?> ColorModeProperty =
        AvaloniaProperty.Register<LivePreviewControl, string?>(nameof(ColorMode), "none");

    public string? ColorMode
    {
        get => GetValue(ColorModeProperty);
        set => SetValue(ColorModeProperty, value);
    }

    private static readonly IPen MarkerOutline = new Pen(new ImmutableSolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), 1);
    private static IBrush MarkerBrush(LeadKind kind) => kind switch
    {
        LeadKind.ContinuationFlag => new ImmutableSolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)), // orange
        LeadKind.CommentMarker    => new ImmutableSolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), // green
        LeadKind.Th2Point         => new ImmutableSolidColorBrush(Color.FromRgb(0x6A, 0x1B, 0x9A)), // purple
        _                         => new ImmutableSolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)), // dead-end red
    };

    // Equate junctions: a bigger amber "target" so they stand out from station/lead dots.
    private static readonly IPen JunctionOutline = new Pen(new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), 1.4);
    private static readonly IBrush JunctionFill = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // amber
    private static readonly IBrush JunctionCore = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private const double JunctionRadius = 6.0;

    /// <summary>Raised when the user clicks (without dragging) near a leg.</summary>
    public event EventHandler<SourceSpan>? SegmentActivated;

    private double _zoom = 1.0;
    private double _panX, _panY;
    private double _eff, _cx, _cy; // last render transform (world→screen), for hit-testing
    private Point _lastPointer;
    private bool _panning, _dragged;
    private (double MinX, double MinY, double MaxX, double MaxY)? _lastBounds; // for "same scene" detection (#1)

    static LivePreviewControl()
    {
        AffectsRender<LivePreviewControl>(
            SegmentsProperty, LeadMarkersProperty, EquateMarkersProperty, ShowJunctionsProperty,
            ShowLabelsProperty, ShowSurveyNamesProperty, ColorModeProperty);
    }

    public LivePreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SegmentsProperty) return;

        // Only auto-fit (reset zoom/pan) for a genuinely new scene. Clicking a station/junction
        // triggers a refresh with identical geometry, so the view must stay put (#1).
        if (ComputeBounds(Segments) is { } b)
        {
            if (!(_lastBounds is { } prev && BoundsClose(b, prev))) { _panX = _panY = 0; _zoom = 1.0; }
            _lastBounds = b;
        }
        else _lastBounds = null;
        InvalidateVisual();
    }

    private static (double MinX, double MinY, double MaxX, double MaxY)? ComputeBounds(IReadOnlyList<SketchSegment>? segs)
    {
        if (segs is null || segs.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in segs)
        {
            minX = Math.Min(minX, Math.Min(s.X1, s.X2)); maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
            minY = Math.Min(minY, Math.Min(s.Y1, s.Y2)); maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
        }
        return (minX, minY, maxX, maxY);
    }

    // "Same scene": world bounds match within ~2% of the larger extent (a click/no-op refresh).
    private static bool BoundsClose(
        (double MinX, double MinY, double MaxX, double MaxY) a, (double MinX, double MinY, double MaxX, double MaxY) b)
    {
        double tol = 1e-6 + 0.02 * Math.Max(Math.Abs(a.MaxX - a.MinX), Math.Abs(a.MaxY - a.MinY));
        return Math.Abs(a.MinX - b.MinX) <= tol && Math.Abs(a.MinY - b.MinY) <= tol
            && Math.Abs(a.MaxX - b.MaxX) <= tol && Math.Abs(a.MaxY - b.MaxY) <= tol;
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

        var mode = ColorMode ?? "none";
        bool coloured = mode is "survey" or "file" or "component";
        var penCache = coloured ? new Dictionary<uint, IPen>() : null;

        foreach (var s in segs)
        {
            var p1 = ToScreen(s.X1, s.Y1, size);
            var p2 = ToScreen(s.X2, s.Y2, size);
            ctx.DrawLine(coloured ? PenFor(s, mode, penCache!) : LegPen, p1, p2);
        }
        // Station dots only when the layout isn't too dense.
        if (segs.Count <= 1500)
            foreach (var s in segs)
            {
                ctx.DrawEllipse(StationBrush, null, ToScreen(s.X2, s.Y2, size), 1.6, 1.6);
                ctx.DrawEllipse(StationBrush, null, ToScreen(s.X1, s.Y1, size), 1.6, 1.6);
            }

        if (ShowLabels) DrawStationLabels(ctx, segs, size);

        // LEAD-02: lead markers on top, coloured by kind.
        if (LeadMarkers is { } leads)
            foreach (var m in leads)
                ctx.DrawEllipse(MarkerBrush(m.Kind), MarkerOutline, ToScreen(m.X, m.Y, size), 4.5, 4.5);

        // Equate junctions on top of everything: a bigger amber target with a dark core.
        if (ShowJunctions && EquateMarkers is { } junctions)
            foreach (var j in junctions)
            {
                var p = ToScreen(j.X, j.Y, size);
                ctx.DrawEllipse(JunctionFill, JunctionOutline, p, JunctionRadius, JunctionRadius);
                ctx.DrawEllipse(JunctionCore, null, p, 1.8, 1.8);
            }

        if (coloured) DrawLegend(ctx, segs, mode);
    }

    private static IPen PenFor(SketchSegment s, string mode, Dictionary<uint, IPen> cache)
    {
        var color = SketchColors.ForKey(ColorKey(s, mode));
        uint argb = color.ToUInt32();
        if (!cache.TryGetValue(argb, out var pen))
            cache[argb] = pen = new ImmutablePen(new ImmutableSolidColorBrush(color), 1.3);
        return pen;
    }

    private static string ColorKey(SketchSegment s, string mode) => mode switch
    {
        "survey"    => s.Survey,
        "file"      => s.File,
        "component" => "component " + s.Component.ToString(CultureInfo.InvariantCulture),
        _           => string.Empty,
    };

    // De-duplicate stations (an endpoint is shared by many legs) and label each once.
    private void DrawStationLabels(DrawingContext ctx, IReadOnlyList<SketchSegment> segs, Size size)
    {
        var seen = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        foreach (var s in segs)
        {
            seen.TryAdd(s.FromName, (s.X1, s.Y1));
            seen.TryAdd(s.ToName, (s.X2, s.Y2));
            if (seen.Count > MaxLabelStations) return;   // too dense to be useful
        }
        bool qualify = ShowSurveyNames;
        foreach (var (name, w) in seen)
        {
            var p = ToScreen(w.X, w.Y, size);
            var text = Label(qualify ? name : ShortName(name), 10, LabelText);
            var origin = new Point(p.X + 3, p.Y - text.Height / 2);
            ctx.FillRectangle(LabelHalo, new Rect(origin.X - 1, origin.Y, text.Width + 2, text.Height));
            ctx.DrawText(text, origin);
        }
    }

    // Top-left legend mapping each colour key to a swatch + label for the active colour mode.
    private void DrawLegend(DrawingContext ctx, IReadOnlyList<SketchSegment> segs, string mode)
    {
        var keys = new List<string>();
        var have = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in segs)
        {
            var key = ColorKey(s, mode);
            if (have.Add(key)) keys.Add(key);
            if (keys.Count > 24) break;
        }
        keys.Sort(StringComparer.OrdinalIgnoreCase);

        const double pad = 6, sw = 12, gap = 6, row = 16;
        var rows = new List<(string Label, Color Color)>(keys.Count);
        double textW = 0;
        foreach (var key in keys)
        {
            var label = LegendLabel(key, mode);
            rows.Add((label, SketchColors.ForKey(key)));
            textW = Math.Max(textW, Label(label, 11, LegendText).Width);
        }
        if (rows.Count == 0) return;

        double boxW = pad + sw + gap + textW + pad;
        double boxH = pad + rows.Count * row + pad;
        ctx.FillRectangle(LegendBack, new Rect(8, 8, boxW, boxH), 4);
        double y = 8 + pad;
        foreach (var (label, color) in rows)
        {
            ctx.FillRectangle(new ImmutableSolidColorBrush(color), new Rect(8 + pad, y + 2, sw, sw), 2);
            ctx.DrawText(Label(label, 11, LegendText), new Point(8 + pad + sw + gap, y));
            y += row;
        }
    }

    private static string LegendLabel(string key, string mode) => mode switch
    {
        "file"   => string.IsNullOrEmpty(key) ? "(no file)" : Path.GetFileName(key),
        "survey" => string.IsNullOrEmpty(key) ? "(root)" : key,
        _        => key,
    };

    private static string ShortName(string qualified)
    {
        int dot = qualified.LastIndexOf('.');
        return dot >= 0 && dot < qualified.Length - 1 ? qualified[(dot + 1)..] : qualified;
    }

    private static FormattedText Label(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

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
        double step = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        double newZoom = Math.Clamp(_zoom * step, 0.05, 50);
        double factor = newZoom / _zoom;     // actual applied factor (after clamping at the limits)
        if (factor != 1.0)
        {
            // Anchor the zoom on the world point under the cursor (so it stays put), instead of
            // always zooming about the centre (#2). Derived from screen = (w-c)*eff + size/2 + pan.
            var p = e.GetPosition(this);
            _panX = (p.X - Bounds.Width / 2) * (1 - factor) + _panX * factor;
            _panY = (p.Y - Bounds.Height / 2) * (1 - factor) + _panY * factor;
            _zoom = newZoom;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    // Pick the nearest leg (in screen space) to the click and raise it if close enough.
    private void ActivateAt(Point click)
    {
        var segs = Segments;
        var size = Bounds.Size;

        // Equate junctions are the biggest, top-most targets: a click near one jumps to the equate.
        if (ShowJunctions && EquateMarkers is { } junctions)
        {
            double bestJ = JunctionRadius + 3;
            SourceSpan? jSpan = null;
            foreach (var j in junctions)
            {
                var d = Distance(click, ToScreen(j.X, j.Y, size));
                if (d < bestJ) { bestJ = d; jSpan = j.Span; }
            }
            if (jSpan is { } js) { SegmentActivated?.Invoke(this, js); return; }
        }

        // LEAD-02: a click near a lead marker navigates to that lead first (markers sit on top).
        if (LeadMarkers is { } leads)
        {
            double bestMarker = 9.0;
            SourceSpan? markerSpan = null;
            foreach (var m in leads)
            {
                var d = Distance(click, ToScreen(m.X, m.Y, size));
                if (d < bestMarker) { bestMarker = d; markerSpan = m.Span; }
            }
            if (markerSpan is { } ms) { SegmentActivated?.Invoke(this, ms); return; }
        }

        if (segs is null) return;
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

/// <summary>
/// Deterministic colour palette for the live-preview debug overlays. The same key always maps to
/// the same colour (a stable FNV-1a hash → palette index), so colours stay consistent across
/// re-renders and don't depend on iteration order. Pure — unit-tested.
/// </summary>
public static class SketchColors
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x15, 0x65, 0xC0), // blue
        Color.FromRgb(0xE6, 0x51, 0x00), // orange
        Color.FromRgb(0x2E, 0x7D, 0x32), // green
        Color.FromRgb(0x6A, 0x1B, 0x9A), // purple
        Color.FromRgb(0xC6, 0x28, 0x28), // red
        Color.FromRgb(0x00, 0x83, 0x8F), // teal
        Color.FromRgb(0xF9, 0xA8, 0x25), // amber
        Color.FromRgb(0xAD, 0x14, 0x57), // pink
        Color.FromRgb(0x55, 0x8B, 0x2F), // olive
        Color.FromRgb(0x42, 0x77, 0xC4), // light blue
        Color.FromRgb(0x8D, 0x6E, 0x63), // brown
        Color.FromRgb(0x00, 0x69, 0x5C), // dark teal
        Color.FromRgb(0x9E, 0x9D, 0x24), // lime
        Color.FromRgb(0x5E, 0x35, 0xB1), // deep purple
        Color.FromRgb(0xD8, 0x43, 0x15), // deep orange
        Color.FromRgb(0x37, 0x47, 0x4F), // blue grey
    };

    public static int Count => Palette.Length;

    /// <summary>Stable palette slot for a key (process-independent, unlike string.GetHashCode).</summary>
    public static int PaletteIndex(string? key)
    {
        uint h = 2166136261u;
        if (!string.IsNullOrEmpty(key))
            foreach (char c in key) { h ^= c; h *= 16777619u; }
        return (int)(h % (uint)Palette.Length);
    }

    public static Color ForKey(string? key) => Palette[PaletteIndex(key)];
}
