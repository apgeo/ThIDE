// custom-drawn centreline sketch. Auto-fits the world segments into the control, with
// drag-to-pan, wheel-to-zoom, and click-a-leg/point to raise SegmentActivated (the VM navigates).
//
// Beyond the legs it draws splays (faded wall lines or just their edge points), highlights the
// station / junction / entrance / fix under the cursor with a ring + info tooltip + corner readout,
// and — driven by HighlightGroup/ClearHighlight from the legend overlay — emphasises a survey / file /
// component (thicker legs + recoloured splays, drawn from the FULL scene so even a hidden group shows
// while hovered) and frames it with a rectangle, animating a zoom-out (no pan) if it isn't on screen.
// A north arrow is drawn in the top-right corner while in plan view.
//
// Provenance colouring (survey / file / component) uses the deterministic SketchColors palette shared
// with the legend swatches; when splays are shown they take the group colour and the legs are drawn a
// touch darker and thicker so the centreline stays legible over them.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
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

    // Splays (wall shots): faded so they read as secondary detail behind the centreline.
    private static readonly ImmutablePen SplayPen =
        new(new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0x42, 0x77, 0xC4)), 1.0);
    private static readonly IBrush SplayPointBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0x42, 0x77, 0xC4));

    // Hover: a bright ring around the point + a dark info tooltip / corner readout.
    private static readonly IPen HoverRing =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x6D, 0x00)), 2.0);
    private static readonly IBrush InfoBack =
        new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0x21, 0x21, 0x21));
    private static readonly IBrush InfoText =
        new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

    // Group highlight: a dashed bright rectangle with a faint fill, and the emphasised splay colour.
    private static readonly IBrush HighlightFill =
        new ImmutableSolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x6D, 0x00));
    private static readonly IPen HighlightPen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x6D, 0x00)), 1.6,
            new ImmutableDashStyle(new double[] { 4, 3 }, 0));
    private static readonly IPen HighlightSplayPen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x6D, 0x00)), 1.2);
    private static readonly IBrush HighlightSplayPointBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x6D, 0x00));

    private static readonly IBrush NorthBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0xDD, 0x21, 0x21, 0x21));
    private static readonly IPen NorthPen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xDD, 0x21, 0x21, 0x21)), 1.6);

    // Beyond this many distinct stations, labels would be unreadable and slow — suppress them.
    private const int MaxLabelStations = 1500;
    // Above this many splays, skip per-move hover hit-testing on them (still drawn).
    private const int MaxSplayHover = 4000;

    public static readonly StyledProperty<IReadOnlyList<SketchSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<SketchSegment>?>(nameof(Segments));

    public IReadOnlyList<SketchSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    // The full (unfiltered) scene — consulted to emphasise a hovered group even when it's hidden.
    public static readonly StyledProperty<IReadOnlyList<SketchSegment>?> AllSegmentsProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<SketchSegment>?>(nameof(AllSegments));

    public IReadOnlyList<SketchSegment>? AllSegments
    {
        get => GetValue(AllSegmentsProperty);
        set => SetValue(AllSegmentsProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<SplaySegment>?> AllSplaysProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<SplaySegment>?>(nameof(AllSplays));

    public IReadOnlyList<SplaySegment>? AllSplays
    {
        get => GetValue(AllSplaysProperty);
        set => SetValue(AllSplaysProperty, value);
    }

    // Splays (wall shots) — drawn as faded lines or edge points, depending on SplaysAsLines.
    public static readonly StyledProperty<IReadOnlyList<SplaySegment>?> SplaysProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<SplaySegment>?>(nameof(Splays));

    public IReadOnlyList<SplaySegment>? Splays
    {
        get => GetValue(SplaysProperty);
        set => SetValue(SplaysProperty, value);
    }

    /// <summary>Draw splays as faded lines (true) or just their far edge points (false).</summary>
    public static readonly StyledProperty<bool> SplaysAsLinesProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(SplaysAsLines), true);

    public bool SplaysAsLines
    {
        get => GetValue(SplaysAsLinesProperty);
        set => SetValue(SplaysAsLinesProperty, value);
    }

    // Hoverable / clickable station / entrance / fix points.
    public static readonly StyledProperty<IReadOnlyList<StationPoint>?> StationPointsProperty =
        AvaloniaProperty.Register<LivePreviewControl, IReadOnlyList<StationPoint>?>(nameof(StationPoints));

    public IReadOnlyList<StationPoint>? StationPoints
    {
        get => GetValue(StationPointsProperty);
        set => SetValue(StationPointsProperty, value);
    }

    // lead markers overlaid on the sketch.
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

    /// <summary>Show splays (off by default).</summary>
    public static readonly StyledProperty<bool> ShowSplaysProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(ShowSplays));

    public bool ShowSplays
    {
        get => GetValue(ShowSplaysProperty);
        set => SetValue(ShowSplaysProperty, value);
    }

    /// <summary>Draw the small dot at each station (on by default).</summary>
    public static readonly StyledProperty<bool> ShowStationSymbolsProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(ShowStationSymbols), true);

    public bool ShowStationSymbols
    {
        get => GetValue(ShowStationSymbolsProperty);
        set => SetValue(ShowStationSymbolsProperty, value);
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

    /// <summary>Draw a north arrow in the top-right corner (plan view only).</summary>
    public static readonly StyledProperty<bool> ShowNorthArrowProperty =
        AvaloniaProperty.Register<LivePreviewControl, bool>(nameof(ShowNorthArrow));

    public bool ShowNorthArrow
    {
        get => GetValue(ShowNorthArrowProperty);
        set => SetValue(ShowNorthArrowProperty, value);
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

    /// <summary>Raised when the user clicks (without dragging) near a leg / point.</summary>
    public event EventHandler<SourceSpan>? SegmentActivated;

    private double _zoom = 1.0;
    private double _panX, _panY;
    private double _eff, _cx, _cy; // last render transform (world→screen), for hit-testing
    private Point _lastPointer;
    private bool _panning, _dragged;
    private (double MinX, double MinY, double MaxX, double MaxY)? _lastBounds; // for "same scene" detection (#1)

    // Hover state (the point under the cursor) + the pointer position the tooltip follows.
    // Near = the compact label drawn at the cursor (station name only); Detail = the multi-line block
    // drawn in the bottom-left corner.
    private (Point Screen, string Near, string Detail, double Radius)? _hover;
    private Point _hoverPointer;

    // Group highlight (from the legend overlay): which group, its world rect, and its info line.
    private string? _highlightKey;
    private string? _highlightDim;
    private string? _highlightInfo;
    private Rect? _highlightWorld;

    // Smooth (eased) zoom animation toward a target — used by the group "reveal" zoom-out.
    private double _zoomTarget;
    private DispatcherTimer? _zoomTimer;

    static LivePreviewControl()
    {
        AffectsRender<LivePreviewControl>(
            SegmentsProperty, SplaysProperty, SplaysAsLinesProperty, StationPointsProperty,
            LeadMarkersProperty, EquateMarkersProperty, ShowJunctionsProperty, ShowSplaysProperty,
            ShowStationSymbolsProperty, ShowLabelsProperty, ShowSurveyNamesProperty,
            ShowNorthArrowProperty, ColorModeProperty);
    }

    public LivePreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>
    /// Frame a survey/file/component and emphasise it. The map is NOT panned; if the group isn't on
    /// screen the zoom is animated outward (and left there) until it is. Repeated calls for the same
    /// group are ignored so hovering doesn't re-trigger the animation.
    /// </summary>
    public void HighlightGroup(string key, string dimension, string? info,
        double minX, double minY, double maxX, double maxY)
    {
        if (_highlightKey == key && _highlightDim == dimension) return;
        _highlightKey = key;
        _highlightDim = dimension;
        _highlightInfo = info ?? string.Empty;
        _highlightWorld = (maxX >= minX && maxY >= minY)
            ? new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY))
            : (Rect?)null;

        if (_highlightWorld is not null && _eff > 0 && _zoom > 0)
        {
            double zmax = MaxZoomToShow(minX, minY, maxX, maxY);
            if (zmax > 0 && _zoom > zmax) AnimateZoomTo(zmax * 0.92);   // zoom out only; leave it there
        }
        InvalidateVisual();
    }

    /// <summary>Clears the group highlight (keeps the current zoom — it's "left zoomed out").</summary>
    public void ClearHighlight()
    {
        if (_highlightKey is null && _highlightWorld is null) return;
        _highlightKey = _highlightDim = _highlightInfo = null;
        _highlightWorld = null;
        InvalidateVisual();
    }

    // Largest zoom that still keeps the world rect inside the viewport at the CURRENT pan (no panning).
    private double MaxZoomToShow(double minX, double minY, double maxX, double maxY)
    {
        double fit = _eff / _zoom;
        if (fit <= 0) return -1;
        double cxs = Bounds.Width / 2 + _panX, cys = Bounds.Height / 2 + _panY;
        double w = Bounds.Width, h = Bounds.Height, m = 16;
        double zmax = double.MaxValue;
        void Cx(double wx) { double a = (wx - _cx) * fit; if (a > 1e-9) zmax = Math.Min(zmax, (w - m - cxs) / a); else if (a < -1e-9) zmax = Math.Min(zmax, (m - cxs) / a); }
        void Cy(double wy) { double a = (wy - _cy) * fit; if (a > 1e-9) zmax = Math.Min(zmax, (h - m - cys) / a); else if (a < -1e-9) zmax = Math.Min(zmax, (m - cys) / a); }
        Cx(minX); Cx(maxX); Cy(minY); Cy(maxY);
        return zmax;
    }

    private void AnimateZoomTo(double target)
    {
        _zoomTarget = Math.Clamp(target, 0.05, 50);
        if (Math.Abs(_zoomTarget - _zoom) < 1e-4) { _zoom = _zoomTarget; InvalidateVisual(); return; }
        _zoomTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnZoomTick);
        if (!_zoomTimer.IsEnabled) _zoomTimer.Start();
    }

    private void OnZoomTick(object? sender, EventArgs e)
    {
        _zoom += (_zoomTarget - _zoom) * 0.22;   // exponential ease
        if (Math.Abs(_zoomTarget - _zoom) < 0.002) { _zoom = _zoomTarget; _zoomTimer?.Stop(); }
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _zoomTimer?.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SegmentsProperty) return;

        // Only auto-fit (reset zoom/pan) for a genuinely new scene. Clicking a station/junction
        // triggers a refresh with identical geometry, so the view must stay put (#1).
        if (ComputeBounds(Segments) is { } b)
        {
            if (!(_lastBounds is { } prev && BoundsClose(b, prev)))
            {
                _panX = _panY = 0; _zoom = 1.0;
                _highlightKey = _highlightDim = _highlightInfo = null;   // a new scene invalidates any peek
                _highlightWorld = null;
                _zoomTimer?.Stop();
            }
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
        bool emphasizeLegs = coloured && ShowSplays;   // legs darker/thicker so they read over splays

        // Splays underneath the centreline so the legs stay legible.
        if (ShowSplays && Splays is { } splays && splays.Count > 0)
        {
            var splayStyle = coloured ? new Dictionary<uint, (IPen Pen, IBrush Brush)>() : null;
            foreach (var sp in splays)
            {
                IPen pen; IBrush brush;
                if (splayStyle is not null) { var st = SplayStyle(sp, mode, splayStyle); pen = st.Pen; brush = st.Brush; }
                else { pen = SplayPen; brush = SplayPointBrush; }
                if (SplaysAsLines) ctx.DrawLine(pen, ToScreen(sp.X1, sp.Y1, size), ToScreen(sp.X2, sp.Y2, size));
                else ctx.DrawEllipse(brush, null, ToScreen(sp.X2, sp.Y2, size), 1.8, 1.8);
            }
        }

        var penCache = coloured ? new Dictionary<uint, IPen>() : null;
        double legThick = emphasizeLegs ? 2.0 : 1.3;
        foreach (var s in segs)
        {
            var p1 = ToScreen(s.X1, s.Y1, size);
            var p2 = ToScreen(s.X2, s.Y2, size);
            ctx.DrawLine(coloured ? PenFor(s, mode, penCache!, legThick, emphasizeLegs) : LegPen, p1, p2);
        }
        // Station dots only when enabled and the layout isn't too dense.
        if (ShowStationSymbols && segs.Count <= 1500)
            foreach (var s in segs)
            {
                ctx.DrawEllipse(StationBrush, null, ToScreen(s.X2, s.Y2, size), 1.6, 1.6);
                ctx.DrawEllipse(StationBrush, null, ToScreen(s.X1, s.Y1, size), 1.6, 1.6);
            }

        if (ShowLabels) DrawStationLabels(ctx, segs, size);

        // lead markers on top, coloured by kind.
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

        // Hovered-group emphasis (drawn from the FULL scene, so a hidden group shows while hovered).
        if (_highlightKey is { } hkey && _highlightDim is { } hdim)
            DrawGroupEmphasis(ctx, size, hkey, hdim);

        // North arrow (plan view): a small N↑ in the top-right corner.
        if (ShowNorthArrow) DrawNorthArrow(ctx, size);

        // Info readouts, last so they sit above everything.
        if (!string.IsNullOrEmpty(_highlightInfo))
        {
            DrawCornerInfoBox(ctx, _highlightInfo!, size);
        }
        else if (_hover is { } h)
        {
            ctx.DrawEllipse(null, HoverRing, h.Screen, h.Radius, h.Radius);
            // Near the cursor: just the compact label (station name, no survey) — Task 6.1.
            DrawInfoBox(ctx, new Point(_hoverPointer.X + 14, _hoverPointer.Y + 14), h.Near, size, clampCorner: false);
            // Bottom-left: the full multi-line detail.
            DrawCornerInfoBox(ctx, h.Detail, size);
        }
    }

    private void DrawGroupEmphasis(DrawingContext ctx, Size size, string key, string dim)
    {
        // The group's splays, in a distinct highlight colour (only when splays are shown).
        if (ShowSplays && AllSplays is { } asp)
            foreach (var sp in asp)
            {
                if (!string.Equals(GroupKey(sp.Survey, sp.File, sp.Component, dim), key, StringComparison.Ordinal)) continue;
                if (SplaysAsLines) ctx.DrawLine(HighlightSplayPen, ToScreen(sp.X1, sp.Y1, size), ToScreen(sp.X2, sp.Y2, size));
                else ctx.DrawEllipse(HighlightSplayPointBrush, null, ToScreen(sp.X2, sp.Y2, size), 2.4, 2.4);
            }

        // The group's legs, thicker, in the group colour.
        if (AllSegments is { } aseg)
        {
            var gpen = new ImmutablePen(new ImmutableSolidColorBrush(SketchColors.ForKey(key)), 3.0);
            foreach (var s in aseg)
            {
                if (!string.Equals(GroupKey(s.Survey, s.File, s.Component, dim), key, StringComparison.Ordinal)) continue;
                var p1 = ToScreen(s.X1, s.Y1, size);
                var p2 = ToScreen(s.X2, s.Y2, size);
                ctx.DrawLine(gpen, p1, p2);
                if (ShowStationSymbols)
                {
                    ctx.DrawEllipse(StationBrush, null, p1, 2.0, 2.0);
                    ctx.DrawEllipse(StationBrush, null, p2, 2.0, 2.0);
                }
            }
        }

        // The framing rectangle.
        if (_highlightWorld is { } hw)
        {
            var a = ToScreen(hw.X, hw.Y, size);
            var b = ToScreen(hw.Right, hw.Bottom, size);
            double x0 = Math.Min(a.X, b.X) - 6, y0 = Math.Min(a.Y, b.Y) - 6;
            double x1 = Math.Max(a.X, b.X) + 6, y1 = Math.Max(a.Y, b.Y) + 6;
            ctx.DrawRectangle(HighlightFill, HighlightPen, new Rect(x0, y0, x1 - x0, y1 - y0), 3, 3);
        }
    }

    private void DrawNorthArrow(DrawingContext ctx, Size size)
    {
        double cx = size.Width - 24, top = 14, bottom = 40;
        ctx.DrawLine(NorthPen, new Point(cx, bottom), new Point(cx, top + 4));
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(cx, top), true);
            g.LineTo(new Point(cx - 4, top + 8));
            g.LineTo(new Point(cx + 4, top + 8));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(NorthBrush, null, head);
        var n = Label("N", 11, NorthBrush);
        ctx.DrawText(n, new Point(cx - n.Width / 2, bottom + 1));
    }

    // A rounded info box; either follows the cursor (clamped to stay on-screen) or sits in the corner.
    // The text is truncated to roughly the control width so a long comment can't run off the edge.
    private void DrawInfoBox(DrawingContext ctx, Point origin, string text, Size size, bool clampCorner)
    {
        int maxChars = Math.Max(20, (int)((size.Width - 20) / 6.6));
        if (text.Length > maxChars) text = text[..(maxChars - 1)] + "…";
        var ft = Label(text, 11.5, InfoText);
        double w = ft.Width + 12, h = ft.Height + 6;
        double x = origin.X, y = origin.Y;
        if (!clampCorner)
        {
            if (x + w > size.Width - 4) x = size.Width - 4 - w;
            if (y + h > size.Height - 4) y = origin.Y - h - 24;
            x = Math.Max(4, x); y = Math.Max(4, y);
        }
        ctx.FillRectangle(InfoBack, new Rect(x, y, w, h), 4);
        ctx.DrawText(ft, new Point(x + 6, y + 3));
    }

    // A multi-line info box pinned to the bottom-left corner, growing upward. Each '\n'-separated line
    // is drawn on its own row; the whole block is truncated per-line to roughly the control width so a
    // long comment can't run off the edge (Task 6.1).
    private void DrawCornerInfoBox(DrawingContext ctx, string text, Size size)
    {
        if (string.IsNullOrEmpty(text)) return;
        int maxChars = Math.Max(20, (int)((size.Width - 20) / 6.6));
        var rows = text.Split('\n');
        var fts = new List<FormattedText>(rows.Length);
        double w = 0;
        foreach (var raw in rows)
        {
            var line = raw.Length > maxChars ? raw[..(maxChars - 1)] + "…" : raw;
            var ft = Label(line, 11.5, InfoText);
            w = Math.Max(w, ft.Width);
            fts.Add(ft);
        }
        double lineH = fts.Count > 0 ? fts[0].Height : 14;
        double boxW = w + 12, boxH = lineH * fts.Count + 6;
        double x = 8, y = Math.Max(4, size.Height - 8 - boxH);
        ctx.FillRectangle(InfoBack, new Rect(x, y, boxW, boxH), 4);
        double ty = y + 3;
        foreach (var ft in fts) { ctx.DrawText(ft, new Point(x + 6, ty)); ty += lineH; }
    }

    // "a.0 = b.0" → "0 = 0" (station point-names only, no survey) for the compact near-cursor tooltip.
    private static string ShortJunction(string label)
    {
        var parts = label.Split(" = ", StringSplitOptions.None);
        for (int i = 0; i < parts.Length; i++)
        {
            var t = parts[i].Trim();
            int at = t.IndexOf('@'); if (at >= 0) t = t[..at];
            int dot = t.LastIndexOf('.'); if (dot >= 0 && dot < t.Length - 1) t = t[(dot + 1)..];
            parts[i] = t;
        }
        return string.Join(" = ", parts);
    }

    private static IPen PenFor(SketchSegment s, string mode, Dictionary<uint, IPen> cache, double thickness, bool darken)
    {
        var color = SketchColors.ForKey(ColorKey(s, mode));
        if (darken) color = Darken(color, 0.7);
        uint argb = color.ToUInt32();
        if (!cache.TryGetValue(argb, out var pen))
            cache[argb] = pen = new ImmutablePen(new ImmutableSolidColorBrush(color), thickness);
        return pen;
    }

    private static (IPen Pen, IBrush Brush) SplayStyle(SplaySegment s, string mode, Dictionary<uint, (IPen, IBrush)> cache)
    {
        var baseColor = SketchColors.ForKey(SplayColorKey(s, mode));
        var color = Color.FromArgb(0x80, baseColor.R, baseColor.G, baseColor.B);   // group colour, translucent
        uint argb = color.ToUInt32();
        if (!cache.TryGetValue(argb, out var st))
        {
            var brush = new ImmutableSolidColorBrush(color);
            cache[argb] = st = (new ImmutablePen(brush, 1.0), brush);
        }
        return st;
    }

    private static Color Darken(Color c, double f) =>
        Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static string ColorKey(SketchSegment s, string mode) => GroupKey(s.Survey, s.File, s.Component, mode);
    private static string SplayColorKey(SplaySegment s, string mode) => GroupKey(s.Survey, s.File, s.Component, mode);

    private static string GroupKey(string survey, string file, int component, string dim) => dim switch
    {
        "survey"    => survey,
        "file"      => file,
        "component" => "component " + component.ToString(CultureInfo.InvariantCulture),
        _           => survey,   // "none" buckets like survey for emphasis purposes
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
        if (_hover is not null) { _hover = null; InvalidateVisual(); }
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_panning)
        {
            var dx = p.X - _lastPointer.X; var dy = p.Y - _lastPointer.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 2) _dragged = true;
            _panX += dx; _panY += dy; _lastPointer = p;
            InvalidateVisual();
            return;
        }

        // Idle move: refresh the hovered point (and let the tooltip follow the cursor).
        _hoverPointer = p;
        var h = FindHover(p, Bounds.Size);
        bool had = _hover is not null;
        _hover = h;
        if (h is not null || had) InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is not null) { _hover = null; InvalidateVisual(); }
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
            _zoomTimer?.Stop();   // user takes over an in-flight reveal animation
            _panX = (p.X - Bounds.Width / 2) * (1 - factor) + _panX * factor;
            _panY = (p.Y - Bounds.Height / 2) * (1 - factor) + _panY * factor;
            _zoom = newZoom;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    // The hoverable point nearest the cursor (stations / entrances / fixes / junctions / leads /
    // splay ends), within a small pixel threshold; null when nothing is close.
    private (Point Screen, string Near, string Detail, double Radius)? FindHover(Point m, Size size)
    {
        double best = 12;
        (Point Screen, string Near, string Detail, double Radius)? hit = null;

        if (StationPoints is { } pts)
            foreach (var pt in pts)
            {
                var s = ToScreen(pt.X, pt.Y, size);
                double d = Distance(m, s);
                if (d < best)
                {
                    best = d;
                    var detail = string.IsNullOrEmpty(pt.Detail) ? pt.Info : pt.Detail;
                    hit = (s, ShortName(pt.Name), detail, HoverRadius(pt.Kind));   // near-cursor: station name only
                }
            }

        if (ShowJunctions && EquateMarkers is { } js)
            foreach (var j in js)
            {
                var s = ToScreen(j.X, j.Y, size);
                double d = Distance(m, s);
                if (d < best)
                {
                    best = d;
                    var detail = !string.IsNullOrEmpty(j.Detail) ? j.Detail
                               : string.IsNullOrEmpty(j.Info) ? "Junction · " + j.Label : j.Info;
                    hit = (s, ShortJunction(j.Label), detail, JunctionRadius + 2);
                }
            }

        if (LeadMarkers is { } leads)
            foreach (var l in leads)
            {
                var s = ToScreen(l.X, l.Y, size);
                double d = Distance(m, s);
                if (d < best) { best = d; hit = (s, l.Kind.ToString(), $"Lead · {l.Kind} · {l.Location}", 7); }
            }

        if (ShowSplays && Splays is { } splays && splays.Count <= MaxSplayHover)
            foreach (var sp in splays)
            {
                var s = ToScreen(sp.X2, sp.Y2, size);
                double d = Distance(m, s);
                if (d < best)
                {
                    best = d;
                    var detail = string.IsNullOrEmpty(sp.Info) ? "Splay · from " + sp.Station : sp.Info;
                    hit = (s, "from " + ShortName(sp.Station), detail, 5);
                }
            }

        return hit;
    }

    private static double HoverRadius(StationPointKind kind) => kind switch
    {
        StationPointKind.Fix => 8,
        StationPointKind.Entrance => 8,
        _ => 6,
    };

    // Pick the nearest target to the click and raise its source span. Points (the smaller, top-most
    // targets) win over lines; among lines, splays only when shown.
    private void ActivateAt(Point click)
    {
        var size = Bounds.Size;

        // Equate junctions are the biggest, top-most targets.
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

        // a click near a lead marker navigates to that lead (markers sit on top).
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

        // Stations / entrances / fixes → their declaration.
        if (StationPoints is { } pts)
        {
            double bestP = 8.0;
            SourceSpan? pSpan = null;
            foreach (var pt in pts)
            {
                var d = Distance(click, ToScreen(pt.X, pt.Y, size));
                if (d < bestP) { bestP = d; pSpan = pt.Span; }
            }
            if (pSpan is { } ps) { SegmentActivated?.Invoke(this, ps); return; }
        }

        // Splays (the line or its far point, per the draw mode) → their data row.
        if (ShowSplays && Splays is { } splays)
        {
            double bestS = 8.0;
            SourceSpan? sSpan = null;
            foreach (var sp in splays)
            {
                double d = SplaysAsLines
                    ? DistanceToSegment(click, ToScreen(sp.X1, sp.Y1, size), ToScreen(sp.X2, sp.Y2, size))
                    : Distance(click, ToScreen(sp.X2, sp.Y2, size));
                if (d < bestS) { bestS = d; sSpan = sp.Span; }
            }
            if (sSpan is { } ss) { SegmentActivated?.Invoke(this, ss); return; }
        }

        var segs = Segments;
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
/// Deterministic colour palette for the live-preview overlays. The same key always maps to the same
/// colour (a stable FNV-1a hash → palette index), so colours stay consistent across re-renders and
/// don't depend on iteration order. Pure — unit-tested.
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
