// live centreline preview. Plots the parsed centreline (from our own model, no Therion
// compile) as a quick plan or projected-profile sketch that refreshes as you edit. Positions are a
// relative spanning-tree layout from shot length/compass/clino; click a leg/point to jump to source.
//
// Surveys are stitched together through the project's `equate` graph: equated stations are merged
// (union-find) so a sub-survey is drawn in continuation of its parent instead of stacking on the
// origin (the cause of the old "superimposed tracks"). Each junction is shown as a clickable marker
// that jumps to the `equate` command. The preview is scoped to the files reachable from the active
// thconfig.
//
// Splays (wall shots) are computed separately from the centreline tree: each splay's far ("wall")
// point is its origin station plus the projected shot vector, so they can be drawn as faded lines or
// just edge points, and clicked to jump to source.
//
// Visibility is grouped by survey / file / component (following the colour mode) and exposed as a
// unified legend-with-checkboxes overlay; hovering a group highlights its extent in the control.
//
// Debug aids: each segment also carries its fully-qualified station names, owning survey, source
// file and connected-component index so the control can label stations and colour legs by
// survey / file / component. "Separate components" tiles any genuinely disconnected pieces (no
// equate/shot link) so they no longer overlap.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Semantics;
using ThIDE.Services;
using ThIDE.Views;

namespace ThIDE.ViewModels;

/// <summary>
/// A drawn leg in world coordinates (Y already flipped so north/up points up). Beyond the geometry
/// it carries debug provenance: the endpoints' fully-qualified names, the owning survey, the source
/// file, and the connected-component index it belongs to.
/// </summary>
public sealed record SketchSegment(
    double X1, double Y1, double X2, double Y2, SourceSpan Span,
    string FromName, string ToName, string Survey, string File, int Component);

/// <summary>
/// A splay (wall shot) plotted from its origin station (X1,Y1) to the measured far/"wall" point
/// (X2,Y2). Click→the splay's data row. Carries provenance for colouring/visibility filtering.
/// </summary>
public sealed record SplaySegment(
    double X1, double Y1, double X2, double Y2, SourceSpan Span,
    string Station, string Survey, string File, int Component)
{
    /// <summary>Pre-formatted hover line (origin station, measurements, comment, survey metadata).</summary>
    public string Info { get; init; } = string.Empty;
}

/// <summary>The kind of a hoverable centreline point (drives the highlight + info text).</summary>
public enum StationPointKind { Station, Entrance, Fix, Junction, Lead }

/// <summary>
/// A hoverable / clickable centreline point: a station, entrance, fix (and, computed in the control,
/// junctions and leads). Carries a pre-built <see cref="Info"/> line and the span to navigate to.
/// </summary>
public sealed record StationPoint(
    double X, double Y, string Name, StationPointKind Kind, SourceSpan Span,
    string Info, string Survey, string File, int Component)
{
    /// <summary>Multi-line detail (full name + survey, date, file, leading comments) for the corner box.</summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>a lead plotted at a station's world position, coloured by kind, click→source.</summary>
public sealed record LeadMarker(double X, double Y, string Location, LeadKind Kind, SourceSpan Span, int Component = 0);

/// <summary>An <c>equate</c> junction plotted at the merged station's position; click→the equate command.</summary>
public sealed record EquateMarker(double X, double Y, string Label, SourceSpan Span, int Component = 0)
{
    /// <summary>Pre-formatted hover line for the junction.</summary>
    public string Info { get; init; } = string.Empty;
    /// <summary>Multi-line detail (per-station name/date/file, the equate's source file, leading comments).</summary>
    public string Detail { get; init; } = string.Empty;
    /// <summary>The surveys this equate connects (so it can be hidden when all of them are hidden).</summary>
    public ImmutableArray<string> Surveys { get; init; } = ImmutableArray<string>.Empty;
    /// <summary>The file the equate command lives in (used for file-grouped visibility).</summary>
    public string File { get; init; } = string.Empty;
}

/// <summary>
/// A show/hide toggle for one visibility group (survey / file / connected-component, depending on the
/// active grouping). Carries a colour swatch (matching the leg colouring) and the group's world-space
/// extent so the control can draw a "where is it" rectangle when the row is hovered.
/// </summary>
public sealed partial class GroupVisibility : ObservableObject
{
    /// <summary>Stable signature used to remember the choice and to filter the scene.</summary>
    public string Key { get; }
    public string Label { get; }
    /// <summary>The grouping dimension this row belongs to (survey / file / component).</summary>
    public string Dimension { get; }
    /// <summary>Survey/file comment + title/date/team shown when the row is hovered (may be empty).</summary>
    public string Info { get; }
    /// <summary>Legend swatch (immutable so it's safe to build off the layout pass).</summary>
    public IBrush Swatch { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public bool HasBounds => MaxX >= MinX && MaxY >= MinY;
    [ObservableProperty] private bool _isVisible;
    private readonly Action<GroupVisibility>? _onChanged;

    public GroupVisibility() { Key = Label = Dimension = Info = string.Empty; Swatch = new ImmutableSolidColorBrush(Colors.Gray); } // design-time
    public GroupVisibility(string key, string label, string dimension, string info, IBrush swatch,
        double minX, double minY, double maxX, double maxY, bool visible, Action<GroupVisibility> onChanged)
    {
        Key = key; Label = label; Dimension = dimension; Info = info; Swatch = swatch;
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        _isVisible = visible; _onChanged = onChanged;
    }

    partial void OnIsVisibleChanged(bool value) => _onChanged?.Invoke(this);
}

/// <summary>
/// A resulted structural-geology plane pushed by the Structural Geology panel: identity, the
/// pre-formatted strike/dip label, the fit's (true-north) azimuths with the declination that was
/// applied to them, the station the plane is anchored at, and whether its grid row is selected.
/// </summary>
public sealed record StructuralPlaneOverlay(
    string Name, string Label, double StrikeDeg, double DipDeg, double DeclinationApplied,
    QualifiedName Anchor, bool IsSelected);

/// <summary>
/// A projected structural-plane line for the control: a thick clickable line through the plane's
/// anchor station, with the strike/dip label drawn at the (X2,Y2) end. Click→the Structural
/// Geology panel selects the matching plane row; IsSelected mirrors that grid's selection back.
/// </summary>
public sealed record PlaneOverlayLine(
    double X1, double Y1, double X2, double Y2, string Name, string Label, bool IsSelected);

/// <summary>Raw spanning-tree layout: a position per (equate-merged) station, its component, and the count.</summary>
public sealed record SketchLayout(
    Dictionary<string, (double E, double N, double Z)> Positions,
    Dictionary<string, int> Components,
    int ComponentCount);

public sealed partial class LivePreviewViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;
    private readonly IWorkspaceSession? _session;

    [ObservableProperty] private IReadOnlyList<SketchSegment> _segments = Array.Empty<SketchSegment>();
    [ObservableProperty] private IReadOnlyList<SplaySegment> _splays = Array.Empty<SplaySegment>();
    [ObservableProperty] private IReadOnlyList<StationPoint> _stationPoints = Array.Empty<StationPoint>();
    // The full (unfiltered) scene the control consults to draw a hovered group even when it's hidden.
    [ObservableProperty] private IReadOnlyList<SketchSegment> _fullSegments = Array.Empty<SketchSegment>();
    [ObservableProperty] private IReadOnlyList<SplaySegment> _fullSplays = Array.Empty<SplaySegment>();
    [ObservableProperty] private IReadOnlyList<LeadMarker> _leadMarkers = Array.Empty<LeadMarker>();
    [ObservableProperty] private IReadOnlyList<EquateMarker> _equateMarkers = Array.Empty<EquateMarker>();
    /// <summary>Structural-plane traces overlaid on the sketch (pushed by the Structural panel).</summary>
    [ObservableProperty] private IReadOnlyList<PlaneOverlayLine> _planeLines = Array.Empty<PlaneOverlayLine>();
    [ObservableProperty] private string _status = "No centreline yet.";

    // ---- view / projection ------------------------------------------------
    /// <summary>Active view: <c>plan</c>, or a projected profile (<c>north</c>/<c>east</c>/<c>south</c>/
    /// <c>west</c>/<c>custom</c>). Profiles differ only by the bearing the section is projected along.</summary>
    [ObservableProperty] private string _viewMode = "plan";
    /// <summary>The projection bearing (0–359°) used when <see cref="ViewMode"/> is <c>custom</c>.</summary>
    [ObservableProperty] private double _customAzimuth = 90;

    /// <summary>True in plan view; drives the north-arrow + Plan button.</summary>
    public bool IsPlan => ViewMode == "plan";
    /// <summary>True in any projected-profile view (mirrors !<see cref="IsPlan"/>); kept for the status line.</summary>
    public bool IsElevation => ViewMode != "plan";
    /// <summary>True only for the custom-bearing profile; reveals the angle slider.</summary>
    public bool IsCustomProfile => ViewMode == "custom";
    /// <summary>The effective projection bearing (degrees) for the active profile. Plan ignores it.</summary>
    public double ProfileAzimuth => ViewMode switch
    {
        "north" => 0, "east" => 90, "south" => 180, "west" => 270,
        "custom" => CustomAzimuth, _ => 90,
    };

    /// <summary>
    /// Caption shown in the drawing-area corner: "Plan view" in plan, else "Projected profile view
    /// (N/E/S/W)" for a cardinal profile, or "Projected profile view (toward &lt;az&gt;°)" for the
    /// custom-bearing profile. Localized and relocalizes live on a language switch.
    /// </summary>
    public string ViewLabel => ViewMode switch
    {
        "plan"   => ThIDE.Resources.Tr.Get("Live_ViewPlan"),
        "custom" => string.Format(ThIDE.Resources.Tr.Get("Live_ViewProfile"),
                        string.Format(ThIDE.Resources.Tr.Get("Live_ViewToward"),
                            System.Math.Round(ProfileAzimuth))),
        _        => string.Format(ThIDE.Resources.Tr.Get("Live_ViewProfile"), CardinalLetter(ViewMode)),
    };

    private static string CardinalLetter(string mode) => mode switch
    {
        "north" => "N", "east" => "E", "south" => "S", "west" => "W", _ => mode,
    };

    // ---- splays (wall shots) ----
    /// <summary>Show splays at all (off by default — they roughly triple the line count).</summary>
    [ObservableProperty] private bool _showSplays;
    /// <summary>Switch 1: draw splays as faded lines (true) or just their far edge points (false).</summary>
    [ObservableProperty] private bool _splaysAsLines = true;

    // ---- debug overlays (render-only; consumed by the control) ----
    /// <summary>Draw a small label at each station.</summary>
    [ObservableProperty] private bool _showStationLabels;
    /// <summary>Qualify station labels as <c>survey.station</c> instead of the bare station name.</summary>
    [ObservableProperty] private bool _showSurveyNames;
    /// <summary>Show the small dot at each station (on by default).</summary>
    [ObservableProperty] private bool _showStationSymbols = true;
    /// <summary>Show the clickable <c>equate</c> junction markers (on by default).</summary>
    [ObservableProperty] private bool _showJunctions = true;
    /// <summary>Show the interactive legend / visibility overlay (on by default).</summary>
    [ObservableProperty] private bool _showLegend = true;
    /// <summary>Leg colouring: <c>none</c> | <c>survey</c> | <c>file</c> | <c>component</c>.</summary>
    [ObservableProperty] private string _colorMode = "none";

    // ---- debug layout (affects geometry → triggers a rebuild) ----
    /// <summary>Tile each disconnected component in its own grid cell instead of stacking at the origin.</summary>
    [ObservableProperty] private bool _separateComponents;

    // ---- per-group visibility ----
    /// <summary>Show/hide toggle per visibility group (survey / file / component, per the colour mode).</summary>
    public ObservableCollection<GroupVisibility> Groups { get; } = new();
    public bool HasGroups => Groups.Count > 1;

    // The full computed scene; what we publish is filtered by the visible group set.
    private IReadOnlyList<SketchSegment> _allSegments = Array.Empty<SketchSegment>();
    private IReadOnlyList<SplaySegment> _allSplays = Array.Empty<SplaySegment>();
    private IReadOnlyList<StationPoint> _allStationPoints = Array.Empty<StationPoint>();
    private IReadOnlyList<LeadMarker> _allLeads = Array.Empty<LeadMarker>();
    private IReadOnlyList<EquateMarker> _allEquates = Array.Empty<EquateMarker>();
    private int _componentCountTotal;
    private string? _mainGroupKey;
    // Survey metadata (title/team/dates) keyed by full survey name, for the hover info lines.
    private Dictionary<string, SurveySymbol> _surveyByName = new(StringComparer.Ordinal);
    // Remembers each group's show/hide choice across rebuilds, keyed by "dimension\0signature".
    private readonly Dictionary<string, bool> _visibilityByKey = new(StringComparer.Ordinal);
    // Structural-geology overlay: the planes pushed by the Structural Geology panel, plus the layout
    // lookups (projected station positions + equate graph) kept from the last rebuild to anchor them.
    private IReadOnlyList<StructuralPlaneOverlay> _structuralPlanes = Array.Empty<StructuralPlaneOverlay>();
    private Dictionary<string, (double X, double Y)> _planePositions = new(StringComparer.Ordinal);
    private EquateGraph? _planeEquates;
    // Caches source lines per file (with last-write stamp) for survey/file leading-comment extraction.
    private readonly Dictionary<string, (DateTime Stamp, string[] Lines)> _fileLines = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressApply;

    public LivePreviewViewModel() { } // design-time
    public LivePreviewViewModel(IDocumentService documents, IWorkspaceSession? session = null)
    {
        _documents = documents;
        _session = session;
        // DocumentChanged alone is enough: SetActive always raises it right after
        // ActiveDocumentChanged, so subscribing to both rebuilt the preview twice per tab switch.
        _documents.DocumentChanged += (_, _) => OnUi(Rebuild);
        // ValidateOnType: the preview is built from the WORKSPACE model (ws.PerFile), not the active
        // doc's own reparse. A live edit only reaches that model when the session re-validates the
        // unsaved buffers (debounced) and raises BuffersRevalidated — DocumentChanged fires earlier,
        // off the reparse, and reads the stale pre-revalidation graph. Without this the preview only
        // caught up on save. Rebuild here so on-type edits refresh the sketch when the graph does.
        if (_session is not null)
            _session.BuffersRevalidated += (_, _) => OnUi(Rebuild);
        // Relocalize the drawing-area caption (ViewLabel) live when the UI language switches.
        ThIDE.Resources.LocProxy.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ViewLabel));
        Rebuild();
    }

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlan));
        OnPropertyChanged(nameof(IsElevation));
        OnPropertyChanged(nameof(IsCustomProfile));
        OnPropertyChanged(nameof(ProfileAzimuth));
        OnPropertyChanged(nameof(ViewLabel));
        Rebuild();
    }
    // Live-update the custom profile as the angle slider moves (no effect in the preset views).
    partial void OnCustomAzimuthChanged(double value)
    {
        if (ViewMode == "custom")
        {
            OnPropertyChanged(nameof(ProfileAzimuth));
            OnPropertyChanged(nameof(ViewLabel));
            Rebuild();
        }
    }
    partial void OnSeparateComponentsChanged(bool value) => Rebuild();
    partial void OnShowSplaysChanged(bool value) => ApplyVisibility();   // status line shows the splay count
    // Switching the colour mode re-buckets the visibility groups (survey ⇄ file ⇄ component).
    partial void OnColorModeChanged(string value)
    {
        if (_allSegments.Count == 0 && _allSplays.Count == 0 && _allStationPoints.Count == 0) return;
        BuildGroups();
        ApplyVisibility();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    /// <summary>Switches the view: <c>plan</c>, or a profile bearing (north/east/south/west/custom).</summary>
    [RelayCommand]
    private void SetViewMode(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        ViewMode = mode;
        // Re-raise so the radio-style buttons refresh their pushed state even when re-clicked.
        OnPropertyChanged(nameof(ViewMode));
    }

    /// <summary>Sets the leg-colouring mode (mirrors the 3D viewer's Color-by buttons).</summary>
    [RelayCommand]
    private void SetColorMode(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        ColorMode = mode;
        // Re-raise so the choice buttons refresh their pushed state even when re-clicked.
        OnPropertyChanged(nameof(ColorMode));
    }

    /// <summary>Show every group.</summary>
    [RelayCommand]
    private void ShowAllGroups() => Batch(() => { foreach (var g in Groups) g.IsVisible = true; });

    /// <summary>Hide every group.</summary>
    [RelayCommand]
    private void HideAllGroups() => Batch(() => { foreach (var g in Groups) g.IsVisible = false; });

    /// <summary>Invert each group's show/hide state.</summary>
    [RelayCommand]
    private void InvertGroups() => Batch(() => { foreach (var g in Groups) g.IsVisible = !g.IsVisible; });

    /// <summary>Show only the main (largest) group; hide the rest.</summary>
    [RelayCommand]
    private void ShowOnlyMain() => Batch(() => { foreach (var g in Groups) g.IsVisible = g.Key == _mainGroupKey; });

    private void Batch(Action act)
    {
        _suppressApply = true;
        try { act(); } finally { _suppressApply = false; }
        ApplyVisibility();
    }

    private void OnGroupVisibilityChanged(GroupVisibility gv)
    {
        _visibilityByKey[VisKey(gv.Key)] = gv.IsVisible;
        if (!_suppressApply) ApplyVisibility();
    }

    /// <summary>Navigates the editor to a leg / lead / junction / point source (called on click).</summary>
    public void Activate(SourceSpan span)
    {
        if (!span.IsEmpty && !string.IsNullOrEmpty(span.FilePath))
            _ = _documents?.NavigateToSpanAsync(span);
    }

    /// <summary>Raised with the plane's name when a structural-plane line is clicked (grid selects it).</summary>
    public event EventHandler<string>? StructuralPlaneActivated;

    /// <summary>Called by the view when a structural-plane overlay line is clicked.</summary>
    public void ActivatePlane(PlaneOverlayLine line) => StructuralPlaneActivated?.Invoke(this, line.Name);

    /// <summary>Replaces the structural-plane overlay set (pushed by the Structural Geology panel).</summary>
    public void SetStructuralPlanes(IReadOnlyList<StructuralPlaneOverlay> planes)
    {
        _structuralPlanes = planes;
        RebuildPlaneOverlay();
    }

    private void Rebuild()
    {
        var (shots, models) = Gather();
        _surveyByName = BuildSurveyLookup(models);
        if (shots.Count == 0)
        {
            _allSegments = Array.Empty<SketchSegment>();
            _allSplays = Array.Empty<SplaySegment>();
            _allStationPoints = Array.Empty<StationPoint>();
            _allLeads = Array.Empty<LeadMarker>();
            _allEquates = Array.Empty<EquateMarker>();
            _componentCountTotal = 0;
            _planePositions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
            _planeEquates = null;
            PlaneLines = Array.Empty<PlaneOverlayLine>();
            Groups.Clear();
            OnPropertyChanged(nameof(HasGroups));
            Segments = Array.Empty<SketchSegment>();
            Splays = Array.Empty<SplaySegment>();
            StationPoints = Array.Empty<StationPoint>();
            FullSegments = Array.Empty<SketchSegment>();
            FullSplays = Array.Empty<SplaySegment>();
            LeadMarkers = Array.Empty<LeadMarker>();
            EquateMarkers = Array.Empty<EquateMarker>();
            Status = "No centreline data to preview.";
            return;
        }

        // Merge equated stations (within- AND cross-file) so connected surveys lay out in continuation.
        var equates = BuildEquateGraph(models);
        string Rep(QualifiedName qn) => equates.Find(qn).ToString();

        var layout = ComputeLayout(shots, equates);

        // Anchor each component that carries a `fix` to its absolute coordinates (translation only —
        // compass already gives absolute orientation). This positions independently-fixed systems
        // correctly relative to each other instead of stacking unrelated pieces at the origin.
        var fixes = BuildFixes(models, equates);
        AnchorByFixes(layout.Positions, layout.Components, layout.ComponentCount, fixes);

        // Project every (merged) station into the current view's 2-D plane, then optionally spread
        // any genuinely disconnected components so they no longer overlap at the origin.
        var p2d = new Dictionary<string, (double X, double Y)>(layout.Positions.Count, StringComparer.Ordinal);
        foreach (var (key, p) in layout.Positions) p2d[key] = Project(p);
        if (SeparateComponents && layout.ComponentCount > 1)
            TileComponents(p2d, layout.Components, layout.ComponentCount);

        var segs = new List<SketchSegment>(shots.Count);
        foreach (var shot in shots)
        {
            if ((shot.Flags & ShotFlags.Splay) != 0) continue;
            var fromRep = Rep(shot.From);
            var toRep = Rep(shot.To);
            if (!p2d.TryGetValue(fromRep, out var a) || !p2d.TryGetValue(toRep, out var b))
                continue;
            var survey = SurveyOf(shot.From);
            var file = shot.Span.FilePath ?? string.Empty;
            var component = layout.Components.TryGetValue(fromRep, out var ci) ? ci : 0;
            // Labels show each leg's own station names, even though equated endpoints share a point.
            segs.Add(new SketchSegment(a.X, a.Y, b.X, b.Y, shot.Span,
                shot.From.ToString(), shot.To.ToString(), survey, file, component));
        }

        // Keep the full scene; publish only the pieces whose visibility toggle is on.
        _allSegments = segs;
        _allSplays = BuildSplays(shots, equates, p2d, layout.Components);
        _allStationPoints = BuildStationPoints(models, equates, p2d, layout.Components);
        _allLeads = BuildLeadMarkers(p2d, equates, layout.Components);
        _allEquates = BuildEquateMarkers(models, equates, p2d, layout.Components);  // junctions
        _componentCountTotal = layout.ComponentCount;
        _planePositions = p2d;
        _planeEquates = equates;
        FullSegments = _allSegments;
        FullSplays = _allSplays;

        BuildGroups();
        ApplyVisibility();
        RebuildPlaneOverlay();
    }

    // ---- structural-plane overlay ------------------------------------------

    /// <summary>
    /// Projects each pushed structural plane into the current view as a line through its anchor
    /// station: the strike line in plan, the apparent-dip trace in a profile. Line length ≈ the
    /// cave's largest extent (the scene-bounds diagonal — rough by design) so the plane reads
    /// across the whole sketch. The sketch's frame is magnetic north (raw compass), so any applied
    /// declination is removed from the plane azimuth before projecting; the label keeps the grid's
    /// (true-north) values.
    /// </summary>
    private void RebuildPlaneOverlay()
    {
        if (_structuralPlanes.Count == 0 || _allSegments.Count == 0 || _planePositions.Count == 0)
        {
            PlaneLines = Array.Empty<PlaneOverlayLine>();
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in _allSegments)
        {
            minX = Math.Min(minX, Math.Min(s.X1, s.X2)); maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
            minY = Math.Min(minY, Math.Min(s.Y1, s.Y2)); maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
        }
        double half = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY)) / 2;
        if (half < 1e-6) half = 5;

        var lines = new List<PlaneOverlayLine>(_structuralPlanes.Count);
        foreach (var p in _structuralPlanes)
        {
            var rep = _planeEquates is { } eq ? eq.Find(p.Anchor).ToString() : p.Anchor.ToString();
            if (!_planePositions.TryGetValue(rep, out var at) &&
                !_planePositions.TryGetValue(p.Anchor.ToString(), out at))
                continue;   // anchor station isn't in the drawn scene (e.g. thconfig-scoped out)

            var (dx, dy) = PlaneTraceDirection(p.StrikeDeg - p.DeclinationApplied, p.DipDeg, IsElevation, ProfileAzimuth);
            lines.Add(new PlaneOverlayLine(
                at.X - dx * half, at.Y - dy * half, at.X + dx * half, at.Y + dy * half,
                p.Name, p.Label, p.IsSelected));
        }
        PlaneLines = lines;
    }

    /// <summary>
    /// The unit 2-D direction of a plane's trace in the current view. In plan it is the strike
    /// line; in a profile it is the plane's intersection with the vertical section plane (the
    /// apparent-dip trace), so a plane dipping along the view bearing draws at its apparent dip
    /// and a vertical plane draws vertical. Built from the upward plane normal, so a 90° dip needs
    /// no special case; a horizontal plane (no trace) falls back to the strike azimuth. Pure.
    /// </summary>
    public static (double X, double Y) PlaneTraceDirection(
        double strikeDeg, double dipDeg, bool isElevation, double azimuthDeg = 90)
    {
        double dd = (strikeDeg + 90) * Math.PI / 180.0;   // dip direction (right-hand rule)
        double dip = dipDeg * Math.PI / 180.0;
        var n = (E: Math.Sin(dd) * Math.Sin(dip), N: Math.Cos(dd) * Math.Sin(dip), Z: Math.Cos(dip));
        // Normal of the viewing plane: vertical (plan) or horizontal across the section bearing.
        double a = azimuthDeg * Math.PI / 180.0;
        var m = isElevation ? (E: Math.Cos(a), N: -Math.Sin(a), Z: 0.0) : (E: 0.0, N: 0.0, Z: 1.0);
        // Trace = the intersection direction of the geological and viewing planes (n × m).
        var t = (E: n.N * m.Z - n.Z * m.N, N: n.Z * m.E - n.E * m.Z, Z: n.E * m.N - n.N * m.E);
        if (Math.Sqrt(t.E * t.E + t.N * t.N + t.Z * t.Z) < 1e-9)
        {
            double s = strikeDeg * Math.PI / 180.0;       // horizontal plane: use the strike azimuth
            t = (Math.Sin(s), Math.Cos(s), 0.0);
        }
        var d = ProjectVector(t, isElevation, azimuthDeg);
        double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        return len < 1e-9 ? (1, 0) : (d.X / len, d.Y / len);
    }

    // ---- per-group visibility ---------------------------------------------

    /// <summary>The grouping dimension follows the colour mode; "none" buckets by survey.</summary>
    private string GroupDimension => ColorMode is "file" or "component" ? ColorMode : "survey";

    private string VisKey(string groupKey) => GroupDimension + "\0" + groupKey;

    private string GroupKeyOf(string survey, string file, int component) => GroupDimension switch
    {
        "file"      => file,
        "component" => "component " + component.ToString(CultureInfo.InvariantCulture),
        _           => survey,
    };

    private string GroupKeyOf(SketchSegment s) => GroupKeyOf(s.Survey, s.File, s.Component);
    private string GroupKeyOf(SplaySegment s) => GroupKeyOf(s.Survey, s.File, s.Component);
    private string GroupKeyOf(StationPoint p) => GroupKeyOf(p.Survey, p.File, p.Component);

    // An equate stays visible while at least one of the groups it touches is visible (so a junction
    // between two hidden surveys/files disappears, but one onto a visible group keeps showing).
    private static bool EquateVisible(EquateMarker m, string dim, HashSet<string> visibleKeys, HashSet<int> visibleComps)
    {
        if (dim == "survey" && !m.Surveys.IsDefaultOrEmpty)
            return m.Surveys.Any(visibleKeys.Contains);
        if (dim == "file" && !string.IsNullOrEmpty(m.File))
            return visibleKeys.Contains(m.File);
        return visibleComps.Contains(m.Component);   // component dimension (or missing provenance)
    }

    /// <summary>Publishes the visible subset of the scene and refreshes the status line.</summary>
    private void ApplyVisibility()
    {
        IReadOnlyList<SketchSegment> segs;
        IReadOnlyList<SplaySegment> splays;
        IReadOnlyList<StationPoint> points;
        if (Groups.Count <= 1)
        {
            segs = _allSegments;
            splays = _allSplays;
            points = _allStationPoints;
            LeadMarkers = _allLeads;
            EquateMarkers = _allEquates;
        }
        else
        {
            var dim = GroupDimension;
            var visible = Groups.Where(g => g.IsVisible).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            segs = _allSegments.Where(s => visible.Contains(GroupKeyOf(s))).ToList();
            splays = _allSplays.Where(s => visible.Contains(GroupKeyOf(s))).ToList();
            points = _allStationPoints.Where(p => visible.Contains(GroupKeyOf(p))).ToList();

            // Leads track their component; an equate is hidden only when ALL the groups it joins are.
            var visibleComps = new HashSet<int>();
            foreach (var s in segs) visibleComps.Add(s.Component);
            foreach (var s in splays) visibleComps.Add(s.Component);
            foreach (var p in points) visibleComps.Add(p.Component);
            LeadMarkers = _allLeads.Where(m => visibleComps.Contains(m.Component)).ToList();
            EquateMarkers = _allEquates.Where(m => EquateVisible(m, dim, visible, visibleComps)).ToList();
        }
        Segments = segs;
        Splays = ShowSplays ? splays : Array.Empty<SplaySegment>();
        StationPoints = points;

        int surveys = segs.Select(s => s.Survey).Distinct(StringComparer.Ordinal).Count();
        int files = segs.Select(s => s.File).Where(f => !string.IsNullOrEmpty(f))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        if (segs.Count == 0)
        {
            Status = Groups.Count > 1
                ? "Nothing shown — enable a group in the legend."
                : "No drawable legs (need length, compass and clino).";
            return;
        }

        Status = $"{(IsElevation ? $"Profile {ProfileAzimuth:0}°" : "Plan")} · {segs.Count} legs · {surveys} survey(s) · {files} file(s)" +
                 (ShowSplays && splays.Count > 0 ? $" · {splays.Count} splay(s)" : string.Empty) +
                 (Groups.Count > 1 ? $" · {Groups.Count(g => g.IsVisible)}/{Groups.Count} groups" : string.Empty) +
                 (EquateMarkers.Count > 0 ? $" · {EquateMarkers.Count} junction(s)" : string.Empty) +
                 (LeadMarkers.Count > 0 ? $" · {LeadMarkers.Count} lead(s)" : string.Empty) +
                 " (preview only — not a Therion render)";
    }

    /// <summary>
    /// Rebuilds the visibility-group list for the active dimension (survey / file / component),
    /// computing each group's colour swatch and world extent. Preserves prior show/hide choices and
    /// defaults to "all visible" — except when grouping by component, where (as before) only the main
    /// piece shows so disconnected stacks stay hidden until enabled.
    /// </summary>
    private void BuildGroups()
    {
        var dim = GroupDimension;
        var acc = new Dictionary<string, GroupAcc>(StringComparer.Ordinal);

        void Add(string key, double x, double y, int weight)
        {
            if (!acc.TryGetValue(key, out var a)) acc[key] = a = new GroupAcc();
            a.Weight += weight;
            a.MinX = Math.Min(a.MinX, x); a.MaxX = Math.Max(a.MaxX, x);
            a.MinY = Math.Min(a.MinY, y); a.MaxY = Math.Max(a.MaxY, y);
            acc[key] = a;
        }

        foreach (var s in _allSegments)
        {
            var key = GroupKeyOf(s);
            Add(key, s.X1, s.Y1, 1);
            Add(key, s.X2, s.Y2, 0);
        }
        foreach (var s in _allSplays) Add(GroupKeyOf(s), s.X1, s.Y1, 0);
        foreach (var p in _allStationPoints) Add(GroupKeyOf(p), p.X, p.Y, 0);

        // Main group = the heaviest (most legs); used for the component default + "Only main".
        _mainGroupKey = acc.Count == 0 ? null
            : acc.OrderByDescending(kv => kv.Value.Weight).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;

        Groups.Clear();
        foreach (var (key, a) in acc.OrderByDescending(kv => kv.Value.Weight).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            bool dflt = dim != "component" || key == _mainGroupKey;
            bool visible = _visibilityByKey.TryGetValue(VisKey(key), out var stored) ? stored : dflt;
            _visibilityByKey[VisKey(key)] = visible;
            var swatch = new ImmutableSolidColorBrush(SketchColors.ForKey(key));
            Groups.Add(new GroupVisibility(key, GroupLabel(key, dim, a.Weight), dim, GroupInfo(key, dim), swatch,
                a.MinX, a.MinY, a.MaxX, a.MaxY, visible, OnGroupVisibilityChanged));
        }
        OnPropertyChanged(nameof(HasGroups));
    }

    private struct GroupAcc
    {
        public int Weight;
        public double MinX, MinY, MaxX, MaxY;
        public GroupAcc() { Weight = 0; MinX = MinY = double.MaxValue; MaxX = MaxY = double.MinValue; }
    }

    private string GroupLabel(string key, string dim, int legs)
    {
        string head = dim switch
        {
            "file"      => string.IsNullOrEmpty(key) ? "(no file)" : Path.GetFileName(key),
            "component" => key + (key == _mainGroupKey ? " · main" : string.Empty),
            _           => string.IsNullOrEmpty(key) ? "(root)" : key,
        };
        return legs > 0 ? $"{head}  ({legs})" : head;
    }

    /// <summary>The hover-info line for a group: survey title/date/team + leading comment, or a file's.</summary>
    private string GroupInfo(string key, string dim)
    {
        if (dim == "survey")
        {
            var parts = new List<string>();
            if (SurveyMeta(key) is { } meta) parts.Add(meta);
            if (_surveyByName.TryGetValue(key, out var sv) && SurveyLeadingComment(sv) is { Length: > 0 } c) parts.Add(c);
            var head = "Survey " + (string.IsNullOrEmpty(key) ? "(root)" : key);
            return parts.Count > 0 ? head + " · " + string.Join(" · ", parts) : head;
        }
        if (dim == "file")
        {
            var name = string.IsNullOrEmpty(key) ? "(no file)" : Path.GetFileName(key);
            var c = string.IsNullOrEmpty(key) ? null : FileLeadingComment(key);
            return string.IsNullOrEmpty(c) ? "File " + name : "File " + name + " · " + c;
        }
        return string.Empty;   // component groups carry no metadata
    }

    /// <summary>A survey's <c>title · dates · team</c> summary, or null when it carries none.</summary>
    private string? SurveyMeta(string surveyName)
    {
        if (!_surveyByName.TryGetValue(surveyName, out var sv)) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sv.Title)) parts.Add('"' + sv.Title + '"');
        if (!sv.Dates.IsDefaultOrEmpty && sv.Dates.Length > 0) parts.Add(string.Join(", ", sv.Dates));
        if (!sv.Team.IsDefaultOrEmpty && sv.Team.Length > 0) parts.Add("team " + string.Join(", ", sv.Team));
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static Dictionary<string, SurveySymbol> BuildSurveyLookup(IReadOnlyList<SemanticModel> models)
    {
        var d = new Dictionary<string, SurveySymbol>(StringComparer.Ordinal);
        foreach (var model in models)
            foreach (var sv in model.Surveys.Values)
                d[sv.Name.ToString()] = sv;
        return d;
    }

    // ---- source leading-comment extraction (cached per file) --------------

    private string[]? FileLinesFor(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_fileLines.TryGetValue(path, out var c) && c.Stamp == stamp) return c.Lines;
            var lines = File.ReadAllLines(path);
            _fileLines[path] = (stamp, lines);
            return lines;
        }
        catch { return null; }
    }

    /// <summary>The contiguous <c>#</c> comment block immediately above a <c>survey</c> declaration.</summary>
    private string? SurveyLeadingComment(SurveySymbol sv)
    {
        var lines = FileLinesFor(sv.DeclarationSpan.FilePath);
        if (lines is null) return null;
        int idx = sv.DeclarationSpan.Start.Line - 1;   // 0-based index of the `survey` line
        if (idx <= 0 || idx > lines.Length) return null;
        var collected = new List<string>();
        for (int i = idx - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('#')) collected.Add(StripComment(t));
            else break;
        }
        collected.Reverse();
        return JoinComment(collected);
    }

    /// <summary>The first contiguous <c>#</c> comment block at the top of a file (after blank lines).</summary>
    private string? FileLeadingComment(string path)
    {
        var lines = FileLinesFor(path);
        if (lines is null) return null;
        var collected = new List<string>();
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.Length == 0) { if (collected.Count > 0) break; else continue; }   // skip leading blanks
            if (t.StartsWith('#')) collected.Add(StripComment(t));
            else break;
        }
        return JoinComment(collected);
    }

    /// <summary>
    /// The contiguous <c>#</c> comment block immediately above <paramref name="line1Based"/> in a file,
    /// one entry per comment line, top-to-bottom. Stops at a blank line or any non-comment line (i.e.
    /// another command) — matching "all comments without an empty line between or another command".
    /// </summary>
    private IReadOnlyList<string> LeadingCommentLines(string? path, int line1Based)
    {
        var lines = FileLinesFor(path);
        var outp = new List<string>();
        if (lines is null) return outp;
        int idx = line1Based - 1;   // 0-based index of the target line
        if (idx <= 0 || idx > lines.Length) return outp;
        for (int i = idx - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('#')) outp.Add(StripComment(t));
            else break;   // blank line or another command ends the block
        }
        outp.Reverse();
        return outp;
    }

    private static string StripComment(string trimmed) => trimmed.TrimStart('#').Trim();

    private static string? JoinComment(List<string> parts)
    {
        var joined = string.Join(" ", parts.Where(p => p.Length > 0));
        if (joined.Length == 0) return null;
        return joined.Length > 200 ? joined[..200] + "…" : joined;
    }

    // ---- data gathering + thconfig scoping --------------------------------

    private static string SurveyOf(QualifiedName name) => name.HasParent ? name.Parent().ToString() : "(root)";

    /// <summary>The centreline shots + their per-file models, scoped to the active thconfig.</summary>
    private (List<ShotSymbol> Shots, List<SemanticModel> Models) Gather()
    {
        var models = ScopedModels();
        if (models.Count == 0 && _documents?.CurrentSemantics is { } m) models = new List<SemanticModel> { m };
        var shots = new List<ShotSymbol>();
        foreach (var model in models) shots.AddRange(model.Shots);
        return (shots, models);
    }

    /// <summary>Per-file models reachable from the active thconfig (all of them when none is active).</summary>
    private List<SemanticModel> ScopedModels()
    {
        var ws = _documents?.Workspace;
        if (ws is null || ws.PerFile.Count == 0) return new List<SemanticModel>();

        var cfg = _session?.ActiveThconfig?.FullPath;
        if (string.IsNullOrEmpty(cfg)) return ws.PerFile.Values.ToList();

        var reachable = ReachableFiles(cfg, ws.FileGraphEdges);
        var scoped = new List<SemanticModel>();
        foreach (var (path, model) in ws.PerFile)
            if (reachable.Contains(SafeFull(path))) scoped.Add(model);

        // If the thconfig's source graph wasn't captured, don't show an empty panel — fall back.
        return scoped.Count > 0 ? scoped : ws.PerFile.Values.ToList();
    }

    /// <summary>BFS the file-dependency edges (thconfig→source→input…) from <paramref name="root"/>.</summary>
    private static HashSet<string> ReachableFiles(string root, ImmutableArray<(string From, string To)> edges)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SafeFull(root) };
        var queue = new Queue<string>();
        queue.Enqueue(SafeFull(root));
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var (from, to) in edges)
                if (string.Equals(SafeFull(from), cur, StringComparison.OrdinalIgnoreCase) && seen.Add(SafeFull(to)))
                    queue.Enqueue(SafeFull(to));
        }
        return seen;
    }

    private static string SafeFull(string p) { try { return Path.GetFullPath(p); } catch { return p; } }

    /// <summary>
    /// Builds the workspace equate union-find for the given models. Two passes:
    ///   1. merge each file's already-resolved equate classes (handles relative tokens via the
    ///      binder's scope), and
    ///   2. resolve every <c>equate</c> record's tokens against the global station set and union
    ///      them — this catches <b>cross-file</b> equates (e.g. <c>equate 0@a 0@b</c> in a master
    ///      file whose per-file binder couldn't see the referenced surveys). Pure.
    /// </summary>
    public static EquateGraph BuildEquateGraph(IReadOnlyList<SemanticModel> models)
    {
        var graph = new EquateGraph();

        // Pass 1 — within-file equate classes (scope-resolved).
        foreach (var model in models)
            foreach (var group in model.Equates.Groups())
                for (int i = 1; i < group.Length; i++)
                    graph.Union(group[0], group[i]);

        // Pass 2 — cross-file: union tokens that resolve against any model's station namespace.
        var known = new HashSet<QualifiedName>();
        foreach (var model in models)
            foreach (var qn in model.Stations.Keys) known.Add(qn);
        foreach (var model in models)
            foreach (var rec in model.EquateRecords)
            {
                QualifiedName? first = null;
                foreach (var raw in NormalizeEquateTokens(rec.Stations))
                {
                    if (ResolveAgainstKnown(raw, known) is not { } qn) continue;
                    if (first is null) first = qn; else graph.Union(first.Value, qn);
                }
            }
        return graph;
    }

    /// <summary>Maps each equate-merged station that carries a <c>fix</c> to its absolute coords + cs.</summary>
    private static Dictionary<string, (double X, double Y, double Z, string? Cs)> BuildFixes(
        IReadOnlyList<SemanticModel> models, EquateGraph equates)
    {
        var fixes = new Dictionary<string, (double X, double Y, double Z, string? Cs)>(StringComparer.Ordinal);
        foreach (var model in models)
            foreach (var st in model.Stations.Values)
                if (st.FixX is { } x && st.FixY is { } y)
                    fixes.TryAdd(equates.Find(st.Name).ToString(), (x, y, st.FixZ ?? 0, st.Cs));   // first fix per node wins
        return fixes;
    }

    /// <summary>
    /// Translates each connected component so its <c>fix</c>ed station lands at the fix coordinates,
    /// expressed relative to a single global reference (the first fixed component) to keep numbers
    /// local. Only fixes sharing the reference's coordinate system are honoured (so UTM and lat/long
    /// fixes aren't mixed into one frame). Components without a (compatible) fix keep their relative
    /// layout. Pure — operates in place. No-op when there are no fixes.
    /// </summary>
    public static void AnchorByFixes(
        Dictionary<string, (double E, double N, double Z)> pos,
        IReadOnlyDictionary<string, int> component,
        int count,
        IReadOnlyDictionary<string, (double X, double Y, double Z, string? Cs)> fixes)
    {
        if (count <= 0 || pos.Count == 0 || fixes.Count == 0) return;

        // Pick one drawn, fixed station per component (deterministic: smallest rep name).
        var fixedOf = new (string Rep, (double X, double Y, double Z, string? Cs) Abs)?[count];
        foreach (var (rep, abs) in fixes)
        {
            if (!pos.ContainsKey(rep)) continue;                       // isolated fix (no shots) — can't place
            if (!component.TryGetValue(rep, out var c) || c < 0 || c >= count) continue;
            if (fixedOf[c] is { } cur && string.CompareOrdinal(cur.Rep, rep) <= 0) continue;
            fixedOf[c] = (rep, abs);
        }

        // Global reference = the lowest-index fixed component (keeps coords small; defines the cs).
        (double X, double Y, double Z, string? Cs)? reference = null;
        for (int c = 0; c < count; c++) if (fixedOf[c] is { } f) { reference = f.Abs; break; }
        if (reference is not { } refAbs) return;

        var dE = new double[count]; var dN = new double[count]; var dZ = new double[count];
        var has = new bool[count];
        for (int c = 0; c < count; c++)
        {
            if (fixedOf[c] is not { } f) continue;
            if (!SameCs(f.Abs.Cs, refAbs.Cs)) continue;               // don't mix coordinate systems
            var rel = pos[f.Rep];
            dE[c] = (f.Abs.X - refAbs.X) - rel.E;
            dN[c] = (f.Abs.Y - refAbs.Y) - rel.N;
            dZ[c] = (f.Abs.Z - refAbs.Z) - rel.Z;
            has[c] = true;
        }

        foreach (var key in pos.Keys.ToList())
        {
            if (!component.TryGetValue(key, out var c) || c < 0 || c >= count || !has[c]) continue;
            var p = pos[key];
            pos[key] = (p.E + dE[c], p.N + dN[c], p.Z + dZ[c]);
        }
    }

    private static bool SameCs(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    // ---- splays + hover points --------------------------------------------

    /// <summary>The 3-D vector (E,N,Z) of a complete shot, in metres. Caller must check the values exist.</summary>
    public static (double E, double N, double Z) ShotVector(ShotSymbol s)
    {
        double cl = s.Clino!.Value * Math.PI / 180.0, c = s.Compass!.Value * Math.PI / 180.0;
        double horiz = s.Length!.Value * Math.Cos(cl);
        return (horiz * Math.Sin(c), horiz * Math.Cos(c), s.Length.Value * Math.Sin(cl));
    }

    /// <summary>
    /// Linear projection of a world vector/delta into the current 2-D view plane. In plan it is
    /// east-vs-north (north up). In a profile it is the horizontal distance along the projection
    /// bearing (<paramref name="azimuthDeg"/>) vs up — so the section "extends" along that bearing.
    /// The default 90° keeps the classic east-vs-up elevation. Pure.
    /// </summary>
    public static (double X, double Y) ProjectVector(
        (double E, double N, double Z) v, bool isElevation, double azimuthDeg = 90)
    {
        if (!isElevation) return (v.E, -v.N);   // plan: east vs north (north up)
        double a = azimuthDeg * Math.PI / 180.0;
        return (v.E * Math.Sin(a) + v.N * Math.Cos(a), -v.Z);   // distance along the bearing vs up
    }

    /// <summary>The projected far ("wall") point of a splay: its origin plus the projected vector. Pure.</summary>
    public static (double X, double Y) SplayEndpoint(
        (double X, double Y) origin, (double E, double N, double Z) vector, bool isElevation, double azimuthDeg = 90)
    {
        var d = ProjectVector(vector, isElevation, azimuthDeg);
        return (origin.X + d.X, origin.Y + d.Y);
    }

    /// <summary>
    /// Builds a splay segment per splay shot: from its origin station (whichever endpoint is a drawn
    /// node) to the measured far/"wall" point (origin + projected shot vector). The projection is the
    /// same linear map as the legs, so it tracks the current plan/profile view.
    /// </summary>
    private IReadOnlyList<SplaySegment> BuildSplays(
        IReadOnlyList<ShotSymbol> shots, EquateGraph equates,
        IReadOnlyDictionary<string, (double X, double Y)> p2d, IReadOnlyDictionary<string, int> components)
    {
        var outp = new List<SplaySegment>();
        foreach (var shot in shots)
        {
            if ((shot.Flags & ShotFlags.Splay) == 0) continue;
            if (shot.Length is null || shot.Compass is null || shot.Clino is null) continue;
            var d = ProjectVector(ShotVector(shot), IsElevation, ProfileAzimuth);   // project the leg vector as a delta

            var fromRep = equates.Find(shot.From).ToString();
            var toRep = equates.Find(shot.To).ToString();
            double ox, oy, fx, fy;
            QualifiedName originName;
            int comp;
            if (p2d.TryGetValue(fromRep, out var a))
            {
                ox = a.X; oy = a.Y; fx = a.X + d.X; fy = a.Y + d.Y;
                originName = shot.From;
                comp = components.TryGetValue(fromRep, out var c0) ? c0 : 0;
            }
            else if (p2d.TryGetValue(toRep, out var b))
            {
                // The anonymous/wall end is the drawn node; the splay points back the other way.
                ox = b.X; oy = b.Y; fx = b.X - d.X; fy = b.Y - d.Y;
                originName = shot.To;
                comp = components.TryGetValue(toRep, out var c1) ? c1 : 0;
            }
            else continue;

            var survey = SurveyOf(originName);
            outp.Add(new SplaySegment(ox, oy, fx, fy, shot.Span,
                originName.ToString(), survey, shot.Span.FilePath ?? string.Empty, comp)
            {
                Info = BuildSplayInfo(originName.ToString(), survey, shot),
            });
        }
        return outp;
    }

    private string BuildSplayInfo(string station, string survey, ShotSymbol shot)
    {
        var info = $"Splay · from {station}";
        if (shot.Length is { } len && shot.Compass is { } c && shot.Clino is { } cl)
            info += $"  ({Num(len)} m, {Num(c)}°/{Num(cl)}°)";
        if (!string.IsNullOrEmpty(shot.Comment)) info += " — " + shot.Comment;
        if (SurveyMeta(survey) is { } meta) info += " · " + meta;
        return info;
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the hoverable/clickable centreline points (stations, entrances, fixes) at their drawn
    /// positions, with a pre-formatted info line and the span to navigate to. De-duplicates equated
    /// stations to one point, keeping the most informative kind (fix &gt; entrance &gt; station).
    /// </summary>
    private IReadOnlyList<StationPoint> BuildStationPoints(
        IReadOnlyList<SemanticModel> models, EquateGraph equates,
        IReadOnlyDictionary<string, (double X, double Y)> p2d, IReadOnlyDictionary<string, int> components)
    {
        var byRep = new Dictionary<string, StationPoint>(StringComparer.Ordinal);
        foreach (var model in models)
            foreach (var st in model.Stations.Values)
            {
                var rep = equates.Find(st.Name).ToString();
                if (!p2d.TryGetValue(rep, out var p)) continue;
                var kind = st.FixX is not null && st.FixY is not null ? StationPointKind.Fix
                         : st.IsEntrance ? StationPointKind.Entrance
                         : StationPointKind.Station;
                if (byRep.TryGetValue(rep, out var existing) && Priority(existing.Kind) >= Priority(kind))
                    continue;   // keep the richer point already recorded for this merged node
                var comp = components.TryGetValue(rep, out var c) ? c : 0;
                var survey = SurveyOf(st.Name);
                var info = BuildPointInfo(st, kind);
                if (SurveyMeta(survey) is { } meta) info += " · " + meta;
                byRep[rep] = new StationPoint(p.X, p.Y, st.Name.ToString(), kind, st.DeclarationSpan,
                    info, survey, st.DeclarationSpan.FilePath ?? string.Empty, comp)
                {
                    Detail = BuildStationDetail(st, kind, survey),
                };
            }
        return byRep.Values.ToList();
    }

    private static int Priority(StationPointKind k) => k switch
    {
        StationPointKind.Fix => 3,
        StationPointKind.Entrance => 2,
        StationPointKind.Station => 1,
        _ => 0,
    };

    private static string BuildPointInfo(StationSymbol st, StationPointKind kind)
    {
        var name = st.Name.ToString();
        string head = kind switch
        {
            StationPointKind.Fix => $"Fix · {name}" +
                (st.FixX is { } x && st.FixY is { } y
                    ? $" · {x.ToString("0.##", CultureInfo.InvariantCulture)}, {y.ToString("0.##", CultureInfo.InvariantCulture)}" +
                      (st.FixZ is { } z ? $", {z.ToString("0.##", CultureInfo.InvariantCulture)}" : string.Empty)
                    : string.Empty) +
                (string.IsNullOrEmpty(st.Cs) ? string.Empty : $" ({st.Cs})"),
            StationPointKind.Entrance => $"Entrance · {name}",
            _ => $"Station · {name}",
        };
        if (!st.Flags.IsDefaultOrEmpty)
        {
            var extra = string.Join(", ", st.Flags.Where(f => !string.Equals(f, "entrance", StringComparison.OrdinalIgnoreCase)));
            if (extra.Length > 0) head += $" · [{extra}]";
        }
        return string.IsNullOrEmpty(st.Comment) ? head : $"{head} — {st.Comment}";
    }

    /// <summary>
    /// The multi-line detail shown in the bottom-left info box when a station is hovered (Task 6.1):
    /// full name (with survey), fix coordinates / flags where relevant, the survey's date, the source
    /// file name, then any contiguous comment lines directly above the station's declaration.
    /// </summary>
    private string BuildStationDetail(StationSymbol st, StationPointKind kind, string survey)
    {
        var lines = new List<string> { st.Name.ToString() };   // full station name with survey

        if (kind == StationPointKind.Fix && st.FixX is { } x && st.FixY is { } y)
            lines.Add($"{Num(x)}, {Num(y)}" + (st.FixZ is { } z ? $", {Num(z)}" : string.Empty) +
                      (string.IsNullOrEmpty(st.Cs) ? string.Empty : $" ({st.Cs})"));

        if (!st.Flags.IsDefaultOrEmpty)
        {
            var extra = string.Join(", ", st.Flags.Where(f => !string.Equals(f, "entrance", StringComparison.OrdinalIgnoreCase)));
            if (extra.Length > 0) lines.Add("[" + extra + "]");
        }

        if (_surveyByName.TryGetValue(survey, out var sv) && !sv.Dates.IsDefaultOrEmpty && sv.Dates.Length > 0)
            lines.Add(string.Join(", ", sv.Dates));
        if (!string.IsNullOrEmpty(st.DeclarationSpan.FilePath))
            lines.Add(Path.GetFileName(st.DeclarationSpan.FilePath));

        foreach (var c in LeadingCommentLines(st.DeclarationSpan.FilePath, st.DeclarationSpan.Start.Line)) lines.Add(c);
        if (!string.IsNullOrEmpty(st.Comment)) lines.Add(st.Comment);
        return string.Join("\n", lines);
    }

    // ---- markers ----------------------------------------------------------

    // project each lead whose location is a centreline station into the sketch's frame.
    private IReadOnlyList<LeadMarker> BuildLeadMarkers(
        IReadOnlyDictionary<string, (double X, double Y)> p2d, EquateGraph equates,
        IReadOnlyDictionary<string, int> components)
    {
        var leads = LeadAnalysis.Analyze(_documents?.Workspace);
        if (leads.IsDefaultOrEmpty) return Array.Empty<LeadMarker>();
        var markers = new List<LeadMarker>();
        foreach (var lead in leads)
        {
            if (SafeParse(lead.Location) is not { } qn) continue;
            var rep = equates.Find(qn).ToString();
            if (!p2d.TryGetValue(rep, out var p)) continue;   // th2/scrap leads aren't centreline stations
            var comp = components.TryGetValue(rep, out var c) ? c : 0;
            markers.Add(new LeadMarker(p.X, p.Y, lead.Location, lead.Kind, lead.Span, comp));
        }
        return markers;
    }

    // One clickable junction per equate command, placed at the merged station's position.
    private IReadOnlyList<EquateMarker> BuildEquateMarkers(
        IReadOnlyList<SemanticModel> models, EquateGraph equates,
        IReadOnlyDictionary<string, (double X, double Y)> p2d, IReadOnlyDictionary<string, int> components)
    {
        // Fallback index: a station's last (point) name → its drawn node key, for relative tokens.
        var byLast = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in p2d.Keys) byLast.TryAdd(LastComponent(key), key);

        // Station lookup so each equated token can resolve to its declaring station (for the per-station
        // name/date/file detail lines, Task 6.1).
        var stationLookup = new Dictionary<QualifiedName, StationSymbol>();
        foreach (var model in models)
            foreach (var st in model.Stations.Values) stationLookup.TryAdd(st.Name, st);
        var known = new HashSet<QualifiedName>(stationLookup.Keys);
        StationSymbol? Resolve(string tok)
        {
            if (SafeParse(tok) is not { } qn) return null;
            if (stationLookup.TryGetValue(qn, out var s0)) return s0;
            return ResolveAgainstKnown(tok, known) is { } r && stationLookup.TryGetValue(r, out var s1) ? s1 : null;
        }

        var markers = new List<EquateMarker>();
        foreach (var model in models)
            foreach (var rec in model.EquateRecords)
            {
                var tokens = NormalizeEquateTokens(rec.Stations);
                if (LocateEquate(tokens, equates, p2d, byLast) is not { } repKey) continue;
                var p = p2d[repKey];
                var comp = components.TryGetValue(repKey, out var c) ? c : 0;
                var label = string.Join(" = ", tokens);
                var surveys = EquateSurveys(tokens);
                var info = "Junction · " + label +
                           (surveys.Length > 0 ? " · " + string.Join(" ↔ ", surveys) : string.Empty);
                markers.Add(new EquateMarker(p.X, p.Y, label, rec.Span, comp)
                {
                    Info = info,
                    Detail = BuildEquateDetail(tokens, rec.Span, Resolve),
                    Surveys = surveys,
                    File = rec.Span.FilePath ?? string.Empty,
                });
            }
        return markers;
    }

    /// <summary>
    /// The multi-line detail shown in the bottom-left info box when an equate junction is hovered
    /// (Task 6.1): one line per equated station (full name with survey · date · file), then the
    /// equate command's own source file, then any contiguous comment lines directly above it.
    /// </summary>
    private string BuildEquateDetail(IReadOnlyList<string> tokens, SourceSpan equateSpan, Func<string, StationSymbol?> resolve)
    {
        var lines = new List<string>();
        foreach (var tok in tokens)
        {
            if (resolve(tok) is not { } st) { lines.Add(tok); continue; }
            var survey = SurveyOf(st.Name);
            var parts = new List<string> { st.Name.ToString() };   // full name with survey
            if (_surveyByName.TryGetValue(survey, out var sv) && !sv.Dates.IsDefaultOrEmpty && sv.Dates.Length > 0)
                parts.Add(string.Join(", ", sv.Dates));
            if (!string.IsNullOrEmpty(st.DeclarationSpan.FilePath))
                parts.Add(Path.GetFileName(st.DeclarationSpan.FilePath));
            lines.Add(string.Join(" · ", parts));
        }
        if (!string.IsNullOrEmpty(equateSpan.FilePath))
            lines.Add("equate: " + Path.GetFileName(equateSpan.FilePath));
        foreach (var c in LeadingCommentLines(equateSpan.FilePath, equateSpan.Start.Line)) lines.Add(c);
        return string.Join("\n", lines);
    }

    /// <summary>The distinct surveys an equate's station tokens belong to.</summary>
    private static ImmutableArray<string> EquateSurveys(IReadOnlyList<string> tokens)
    {
        var set = new List<string>();
        foreach (var t in tokens)
        {
            var s = TokenSurvey(t);
            if (s.Length > 0 && !set.Contains(s, StringComparer.Ordinal)) set.Add(s);
        }
        return set.ToImmutableArray();
    }

    // The survey a station token names: the path after '@' for the `point@survey` form, otherwise the
    // dotted parent. (Matches the segment's Survey key so equate visibility lines up with the legend.)
    private static string TokenSurvey(string token)
    {
        token = token?.Trim() ?? string.Empty;
        if (token.Length == 0) return string.Empty;
        int at = token.IndexOf('@');
        if (at >= 0) return token[(at + 1)..].Trim();
        return SafeParse(token) is { } qn ? SurveyOf(qn) : string.Empty;
    }

    /// <summary>
    /// Re-joins <c>point@survey</c> references that the lexer split into two tokens (<c>point</c> +
    /// <c>@survey</c>), so equate references resolve regardless of the <c>@</c>/dotted form used.
    /// </summary>
    private static List<string> NormalizeEquateTokens(ImmutableArray<string> raw)
    {
        var outp = new List<string>(raw.Length);
        foreach (var t in raw)
        {
            if (t.StartsWith('@') && outp.Count > 0) outp[^1] += t;   // glue "@survey" back onto "point"
            else outp.Add(t);
        }
        return outp;
    }

    // Resolves any one of an equate's stations to its drawn node key (the members share a point once
    // merged, so the first that resolves wins). Tries the exact (rep) name, then a last-name fallback.
    private static string? LocateEquate(
        IReadOnlyList<string> stations, EquateGraph equates,
        IReadOnlyDictionary<string, (double X, double Y)> p2d, Dictionary<string, string> byLast)
    {
        foreach (var raw in stations)
        {
            if (SafeParse(raw) is { } qn)
            {
                var rep = equates.Find(qn).ToString();
                if (p2d.ContainsKey(rep)) return rep;
                var self = qn.ToString();
                if (p2d.ContainsKey(self)) return self;
            }
            if (byLast.TryGetValue(LastComponent(raw), out var k)) return k;
        }
        return null;
    }

    /// <summary>
    /// Resolves an equate token to a known station, trying each candidate interpretation
    /// (<see cref="ParseCandidates"/>) most-specific first. For each: an exact match, then an
    /// input-nesting fallback — the per-file binder doesn't prefix a child survey with the parent's
    /// scope, so a fully-qualified token (<c>cave.a.0</c>) may not match the child's per-file name
    /// (<c>a.0</c>); accept a known station whose name is a suffix of the token, but only when
    /// unambiguous. Trying candidates in turn lets a dotted token resolve both as a literal
    /// station name (<c>N32.23</c>) and, failing that, as a dotted survey path (<c>cave.a.1</c>).
    /// </summary>
    private static QualifiedName? ResolveAgainstKnown(string raw, HashSet<QualifiedName> known)
    {
        foreach (var qn in ParseCandidates(raw))
        {
            if (known.Contains(qn)) return qn;

            QualifiedName? match = null;
            bool ambiguous = false;
            foreach (var k in known)
            {
                if (!IsSuffix(qn.Parts, k.Parts)) continue;
                if (match is not null) { ambiguous = true; break; }   // ambiguous for this candidate
                match = k;
            }
            if (!ambiguous && match is { } m) return m;
        }
        return null;
    }

    /// <summary>True when <paramref name="tail"/> is a (non-empty) trailing slice of <paramref name="full"/>.</summary>
    private static bool IsSuffix(ImmutableArray<string> full, ImmutableArray<string> tail)
    {
        if (tail.Length == 0 || tail.Length > full.Length) return false;
        for (int i = 1; i <= tail.Length; i++)
            if (!string.Equals(full[^i], tail[^i], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// The primary qualified-name interpretation of a station token (the first of
    /// <see cref="ParseCandidates"/>): the point kept <b>whole</b> (its dots are literal — Therion
    /// station names like <c>N32.23</c> are legal), qualified by any <c>@survey</c> path.
    /// </summary>
    private static QualifiedName? SafeParse(string raw)
    {
        foreach (var qn in ParseCandidates(raw)) return qn;
        return null;
    }

    /// <summary>
    /// Candidate qualified names for a station token, most-specific first:
    /// <list type="number">
    /// <item>the point kept whole (dots literal), qualified by any <c>@survey</c> path — this is the
    /// form the binder stores via <see cref="QualifiedName.OfStation"/>, so <c>N32.23@a</c> and a
    /// dotted-name station <c>N32.23</c> match;</item>
    /// <item>for a bare token containing '.', the legacy dotted survey-path split
    /// (<c>cave.a.1</c> → survey <c>cave.a</c> + station <c>1</c>), so fully-qualified references
    /// still resolve.</item>
    /// </list>
    /// (Using only <see cref="QualifiedName.Parse"/> — which splits every dot — would wrongly turn the
    /// station <c>N32.23</c> into survey <c>N32</c> + station <c>23</c> and never match the binder.)
    /// </summary>
    private static IEnumerable<QualifiedName> ParseCandidates(string raw)
    {
        raw = raw?.Trim() ?? string.Empty;
        if (raw.Length == 0) yield break;

        QualifiedName? whole = null, split = null;
        try
        {
            var r = StationRef.Parse(raw);
            whole = QualifiedName.OfStation(r.SurveyPathTopDown, r.Point);
            // Only a bare (no-@) dotted token is ambiguous between a literal name and a survey path.
            if (!r.HasSurvey && raw.Contains('.')) split = QualifiedName.Parse(raw);
        }
        catch { yield break; }

        if (whole is { } w) yield return w;
        if (split is { } s && !s.Equals(whole)) yield return s;
    }

    /// <summary>The last (point) component of a station token, ignoring any <c>@survey</c> suffix.</summary>
    private static string LastComponent(string raw)
    {
        raw = raw?.Trim() ?? string.Empty;
        int at = raw.IndexOf('@');
        if (at >= 0) raw = raw[..at];
        int dot = raw.LastIndexOf('.');
        return dot >= 0 && dot < raw.Length - 1 ? raw[(dot + 1)..] : raw;
    }

    // ---- layout -----------------------------------------------------------

    private (double X, double Y) Project((double E, double N, double Z) p) => ProjectVector(p, IsElevation, ProfileAzimuth);

    /// <summary>
    /// Relative spanning-tree layout: BFS each component from an arbitrary root at the origin,
    /// accumulating each leg's 3-D vector. Splays and incomplete legs are skipped. Stations are
    /// keyed by their equate representative (<paramref name="equates"/>) so equated endpoints merge
    /// into one node — stitching surveys together. Also records each node's connected-component
    /// index (used to colour / separate the pieces). Pure.
    /// </summary>
    public static SketchLayout ComputeLayout(IReadOnlyList<ShotSymbol> shots, EquateGraph? equates = null)
    {
        string Rep(QualifiedName qn) => equates is null ? qn.ToString() : equates.Find(qn).ToString();

        var adj = new Dictionary<string, List<(string To, double E, double N, double Z)>>(StringComparer.Ordinal);
        void Link(string a, string b, double e, double n, double z)
        {
            (adj.TryGetValue(a, out var la) ? la : adj[a] = new()).Add((b, e, n, z));
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = new()).Add((a, -e, -n, -z));
        }

        foreach (var shot in shots)
        {
            if ((shot.Flags & ShotFlags.Splay) != 0) continue;
            if (shot.Length is not { } len || shot.Compass is not { } c || shot.Clino is not { } cl) continue;
            double cl2 = cl * Math.PI / 180.0, c2 = c * Math.PI / 180.0, horiz = len * Math.Cos(cl2);
            Link(Rep(shot.From), Rep(shot.To),
                horiz * Math.Sin(c2), horiz * Math.Cos(c2), len * Math.Sin(cl2));
        }

        var pos = new Dictionary<string, (double E, double N, double Z)>(StringComparer.Ordinal);
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        int next = 0;
        foreach (var start in adj.Keys)
        {
            if (pos.ContainsKey(start)) continue;
            pos[start] = (0, 0, 0);
            component[start] = next;
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                var pu = pos[u];
                foreach (var (v, e, n, z) in adj[u])
                    if (!pos.ContainsKey(v))
                    {
                        pos[v] = (pu.E + e, pu.N + n, pu.Z + z);
                        component[v] = next;
                        queue.Enqueue(v);
                    }
            }
            next++;
        }
        return new SketchLayout(pos, component, next);
    }

    /// <summary>
    /// Spreads disconnected components into a square-ish grid (in projected 2-D space) so they stop
    /// overlapping at the shared origin. Each component keeps its own shape; only the whole piece is
    /// translated into its cell. Pure (operates in place on the supplied 2-D positions).
    /// </summary>
    public static void TileComponents(
        Dictionary<string, (double X, double Y)> p2d, IReadOnlyDictionary<string, int> component, int count)
    {
        if (count <= 1 || p2d.Count == 0) return;

        // Per-component bounding boxes in projected space.
        var minX = new double[count]; var minY = new double[count];
        var maxX = new double[count]; var maxY = new double[count];
        for (int i = 0; i < count; i++) { minX[i] = minY[i] = double.MaxValue; maxX[i] = maxY[i] = double.MinValue; }
        foreach (var (key, p) in p2d)
        {
            int c = component.TryGetValue(key, out var ci) ? ci : 0;
            if (c < 0 || c >= count) continue;
            minX[c] = Math.Min(minX[c], p.X); maxX[c] = Math.Max(maxX[c], p.X);
            minY[c] = Math.Min(minY[c], p.Y); maxY[c] = Math.Max(maxY[c], p.Y);
        }

        double cellW = 1, cellH = 1;
        for (int i = 0; i < count; i++)
        {
            if (maxX[i] < minX[i]) { minX[i] = maxX[i] = 0; }   // empty component → point at origin
            if (maxY[i] < minY[i]) { minY[i] = maxY[i] = 0; }
            cellW = Math.Max(cellW, maxX[i] - minX[i]);
            cellH = Math.Max(cellH, maxY[i] - minY[i]);
        }
        double gapX = cellW * 0.25, gapY = cellH * 0.25;
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));

        // Per-component translation: align each piece's top-left to its grid cell.
        var dx = new double[count]; var dy = new double[count];
        for (int i = 0; i < count; i++)
        {
            int col = i % cols, rowi = i / cols;
            dx[i] = col * (cellW + gapX) - minX[i];
            dy[i] = rowi * (cellH + gapY) - minY[i];
        }

        foreach (var key in p2d.Keys.ToList())
        {
            int c = component.TryGetValue(key, out var ci) ? ci : 0;
            if (c < 0 || c >= count) continue;
            var p = p2d[key];
            p2d[key] = (p.X + dx[c], p.Y + dy[c]);
        }
    }

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
