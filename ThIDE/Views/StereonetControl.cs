// custom-drawn stereonet (Wulff / Schmidt) diagram for the Structural Geology module.
//
// Renders a pre-projected immutable StereonetPlotModel (built by StructuralGeologyViewModel from
// the fitted planes): the reference graticule, the primitive circle with N/E/S/W rim marks, each
// visible plane's great circle (+ optional pole), and optional raw-measurement points. Clicking an
// arc or pole raises PlaneActivated so the VM can select the matching plane row (same sync path as
// the 3D plot's pick). A corner readout shows the trend/plunge under the cursor (Stereonet-11
// style), inverted through the model's projection.
//
// All drawing happens on the UI thread; brushes/pens still use the Immutable variants per the
// project convention.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Therion.Structural;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public sealed class StereonetControl : Control
{
    private const double RimMargin = 22;       // room for the rim labels
    private const double HitTolerance = 6.0;   // px: click/hover distance to an arc or mark

    public static readonly StyledProperty<StereonetPlotModel?> ModelProperty =
        AvaloniaProperty.Register<StereonetControl, StereonetPlotModel?>(nameof(Model));

    public StereonetPlotModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>Raised with the plane name when its great circle or pole is clicked.</summary>
    public event EventHandler<string>? PlaneActivated;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private string? _readout;   // trend/plunge under the cursor
    private string? _hoverName;

    static StereonetControl() => AffectsRender<StereonetControl>(ModelProperty);

    public StereonetControl() => ClipToBounds = true;

    // ---- geometry --------------------------------------------------------------------------------

    private (Point Center, double R) Frame()
    {
        double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - RimMargin;
        return (new Point(Bounds.Width / 2, Bounds.Height / 2), r);
    }

    private static Point Map(StereonetPoint p, Point c, double r) => new(c.X + p.X * r, c.Y - p.Y * r);

    // ---- rendering -------------------------------------------------------------------------------

    public override void Render(DrawingContext ctx)
    {
        var m = Model;
        bool white = m?.WhiteBackground ?? true;

        var bg = white ? new ImmutableSolidColorBrush(Colors.White)
                       : new ImmutableSolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var ink = white ? Color.FromRgb(0x33, 0x33, 0x33) : Color.FromRgb(0xCC, 0xCC, 0xCC);
        var faint = white ? Color.FromArgb(0x2E, 0x00, 0x00, 0x00) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);

        ctx.FillRectangle(bg, new Rect(Bounds.Size));

        var (c, r) = Frame();
        if (r < 24) return;

        var gridPen = new ImmutablePen(new ImmutableSolidColorBrush(faint), 0.8);
        var rimPen = new ImmutablePen(new ImmutableSolidColorBrush(ink), 1.4);
        var inkBrush = new ImmutableSolidColorBrush(ink);

        // Graticule (under everything else).
        if (m?.Graticule is { } grid)
        {
            foreach (var line in grid.GreatCircles) DrawPolyline(ctx, line, gridPen, c, r);
            foreach (var line in grid.SmallCircles) DrawPolyline(ctx, line, gridPen, c, r);
        }

        // Primitive circle + centre cross + rim ticks/labels.
        ctx.DrawEllipse(null, rimPen, c, r, r);
        ctx.DrawLine(rimPen, new Point(c.X - 5, c.Y), new Point(c.X + 5, c.Y));
        ctx.DrawLine(rimPen, new Point(c.X, c.Y - 5), new Point(c.X, c.Y + 5));
        DrawRim(ctx, c, r, rimPen, inkBrush);

        if (m is null || m.Arcs.IsDefaultOrEmpty)
        {
            var hint = Label(ThIDE.Resources.Tr.Get("Struct_NetEmpty"), 12, inkBrush);
            ctx.DrawText(hint, new Point(c.X - hint.Width / 2, c.Y + r + 4));
        }

        if (m is not null)
        {
            var haloPen = new ImmutablePen(
                new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xD6, 0x00)), 5.5);
            var goldPen = new ImmutablePen(
                new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x00)), 1.8);

            // Great circles (selected: gold halo underlay + thicker stroke, matching the 3D plot).
            foreach (var arc in m.Arcs)
            {
                if (arc.Selected) DrawPolyline(ctx, arc.Points, haloPen, c, r);
                var pen = new ImmutablePen(new ImmutableSolidColorBrush(arc.Color), arc.Selected ? 2.6 : 1.7);
                DrawPolyline(ctx, arc.Points, pen, c, r);
            }

            foreach (var mark in m.Measurements)
            {
                var brush = new ImmutableSolidColorBrush(mark.Color, 0.65);
                ctx.DrawEllipse(brush, null, Map(mark.Point, c, r), 2.2, 2.2);
            }

            foreach (var mark in m.Poles)
            {
                var p = Map(mark.Point, c, r);
                double rad = mark.Selected ? 5.0 : 3.6;
                ctx.DrawEllipse(new ImmutableSolidColorBrush(mark.Color), null, p, rad, rad);
                if (mark.Selected) ctx.DrawEllipse(null, goldPen, p, rad + 2.2, rad + 2.2);
            }

            // Plane-name labels at the deepest arc point, nudged toward the centre-facing side.
            if (m.ShowLabels)
                foreach (var arc in m.Arcs)
                {
                    if (arc.Points.IsDefaultOrEmpty) continue;
                    var mid = Map(arc.Points[arc.Points.Length / 2], c, r);
                    var text = Label(arc.Label.Length > 0 ? $"{arc.Name} {arc.Label}" : arc.Name,
                        11, new ImmutableSolidColorBrush(arc.Color));
                    ctx.DrawText(text, new Point(mid.X + 4, mid.Y - text.Height - 2));
                }
        }

        // Corner readouts: projection name (left top) + cursor trend/plunge (left bottom).
        var subtle = new ImmutableSolidColorBrush(ink, 0.75);
        if (m is not null)
        {
            var projText = Label(ThIDE.Resources.Tr.Get(
                m.Projection == StereonetProjection.EqualAngle ? "Struct_ProjWulff" : "Struct_ProjSchmidt"), 11, subtle);
            ctx.DrawText(projText, new Point(6, 4));
        }
        if (_readout is { } ro)
            ctx.DrawText(Label(ro, 11, subtle), new Point(6, Bounds.Height - 18));
    }

    private static void DrawPolyline(DrawingContext ctx, ImmutableArray<StereonetPoint> pts,
        IPen pen, Point c, double r)
    {
        if (pts.IsDefaultOrEmpty || pts.Length < 2) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(Map(pts[0], c, r), false);
            for (int i = 1; i < pts.Length; i++) g.LineTo(Map(pts[i], c, r));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    private void DrawRim(DrawingContext ctx, Point c, double r, IPen pen, IBrush ink)
    {
        // 10° ticks; longer at the cardinals, with N/E/S/W letters just outside the circle.
        string[] cardinal = { "N", "E", "S", "W" };
        for (int deg = 0; deg < 360; deg += 10)
        {
            double a = deg * Math.PI / 180;
            double sin = Math.Sin(a), cos = Math.Cos(a);
            bool major = deg % 90 == 0;
            double len = major ? 7 : 4;
            ctx.DrawLine(pen,
                new Point(c.X + sin * r, c.Y - cos * r),
                new Point(c.X + sin * (r + len), c.Y - cos * (r + len)));
            if (major)
            {
                var t = Label(cardinal[deg / 90], 12, ink);
                ctx.DrawText(t, new Point(c.X + sin * (r + 13) - t.Width / 2, c.Y - cos * (r + 13) - t.Height / 2));
            }
        }
    }

    private static FormattedText Label(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    // ---- interaction -----------------------------------------------------------------------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var (c, r) = Frame();
        if (r < 24) return;
        var p = e.GetPosition(this);
        double x = (p.X - c.X) / r, y = (c.Y - p.Y) / r;

        string? readout = null;
        var proj = Model?.Projection ?? StereonetProjection.EqualAngle;
        if (Stereonet.TryInverse(x, y, proj, out var trend, out var plunge))
            readout = string.Format(CultureInfo.CurrentCulture, "{0:000}°/{1:00}°", trend, plunge);

        var hover = HitTest(p, c, r);
        if (readout != _readout || hover != _hoverName)
        {
            _readout = readout;
            _hoverName = hover;
            Cursor = hover is null ? Cursor.Default : HandCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_readout is null && _hoverName is null) return;
        _readout = null;
        _hoverName = null;
        Cursor = Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var (c, r) = Frame();
        if (r < 24) return;
        if (HitTest(e.GetPosition(this), c, r) is { } name)
            PlaneActivated?.Invoke(this, name);
    }

    /// <summary>Nearest plane under the pointer: poles first, then great-circle polylines.</summary>
    private string? HitTest(Point p, Point c, double r)
    {
        var m = Model;
        if (m is null) return null;

        string? best = null;
        double bestDist = HitTolerance;

        foreach (var mark in m.Poles)
        {
            double d = Distance(p, Map(mark.Point, c, r));
            if (d < bestDist) { bestDist = d; best = mark.Name; }
        }
        if (best is not null) return best;

        foreach (var arc in m.Arcs)
        {
            if (arc.Points.IsDefaultOrEmpty) continue;
            var prev = Map(arc.Points[0], c, r);
            for (int i = 1; i < arc.Points.Length; i++)
            {
                var cur = Map(arc.Points[i], c, r);
                double d = SegmentDistance(p, prev, cur);
                if (d < bestDist) { bestDist = d; best = arc.Name; }
                prev = cur;
            }
        }
        return best;
    }

    // ---- image export ------------------------------------------------------------------------

    /// <summary>Renders the net at 2× into a PNG picked by the user (shared by the wizard tab and
    /// the popped-out panel; best-effort like the 3D plot's export).</summary>
    public async Task ExportPngAsync()
    {
        try
        {
            if (Bounds.Width < 10 || Bounds.Height < 10) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = ThIDE.Resources.Tr.Get("Pick_ExportImage"),
                SuggestedFileName = "stereonet.png",
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_PngImage")) { Patterns = new[] { "*.png" } },
                },
            });
            if (file is null) return;

            const double scale = 2.0;
            var px = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            using var rtb = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
            rtb.Render(this);
            if (file.TryGetLocalPath() is { } path) { await using var fs = File.Create(path); rtb.Save(fs); }
            else { await using var s = await file.OpenWriteAsync(); rtb.Save(s); }
        }
        catch { /* best-effort export */ }
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double SegmentDistance(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double len2 = vx * vx + vy * vy;
        if (len2 < 1e-12) return Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2, 0, 1);
        return Distance(p, new Point(a.X + t * vx, a.Y + t * vy));
    }
}
