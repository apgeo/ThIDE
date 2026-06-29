// VIS-02 — live centreline preview. Plots the parsed centreline (from our own model, no Therion
// compile) as a quick plan or elevation sketch that refreshes as you edit. Positions are a
// relative spanning-tree layout from shot length/compass/clino; click a leg to jump to its source.
//
// Surveys are stitched together through the project's `equate` graph: equated stations are merged
// (union-find) so a sub-survey is drawn in continuation of its parent instead of stacking on the
// origin (the cause of the old "superimposed tracks"). Each junction is shown as a clickable marker
// that jumps to the `equate` command. The preview is scoped to the files reachable from the active
// thconfig.
//
// Debug aids: each segment also carries its fully-qualified station names, owning survey, source
// file and connected-component index so the control can label stations and colour legs by
// survey / file / component. "Separate components" tiles any genuinely disconnected pieces (no
// equate/shot link) so they no longer overlap.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Semantics;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>
/// A drawn leg in world coordinates (Y already flipped so north/up points up). Beyond the geometry
/// it carries debug provenance: the endpoints' fully-qualified names, the owning survey, the source
/// file, and the connected-component index it belongs to.
/// </summary>
public sealed record SketchSegment(
    double X1, double Y1, double X2, double Y2, SourceSpan Span,
    string FromName, string ToName, string Survey, string File, int Component);

/// <summary>LEAD-02: a lead plotted at a station's world position, coloured by kind, click→source.</summary>
public sealed record LeadMarker(double X, double Y, string Location, LeadKind Kind, SourceSpan Span);

/// <summary>An <c>equate</c> junction plotted at the merged station's position; click→the equate command.</summary>
public sealed record EquateMarker(double X, double Y, string Label, SourceSpan Span);

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
    [ObservableProperty] private IReadOnlyList<LeadMarker> _leadMarkers = Array.Empty<LeadMarker>();      // LEAD-02
    [ObservableProperty] private IReadOnlyList<EquateMarker> _equateMarkers = Array.Empty<EquateMarker>();
    [ObservableProperty] private bool _isElevation;
    [ObservableProperty] private string _status = "No centreline yet.";

    // ---- debug overlays (render-only; consumed by the control) ----
    /// <summary>Draw a small label at each station.</summary>
    [ObservableProperty] private bool _showStationLabels;
    /// <summary>Qualify station labels as <c>survey.station</c> instead of the bare station name.</summary>
    [ObservableProperty] private bool _showSurveyNames;
    /// <summary>Show the clickable <c>equate</c> junction markers (on by default).</summary>
    [ObservableProperty] private bool _showJunctions = true;
    /// <summary>Leg colouring: <c>none</c> | <c>survey</c> | <c>file</c> | <c>component</c>.</summary>
    [ObservableProperty] private string _colorMode = "none";

    // ---- debug layout (affects geometry → triggers a rebuild) ----
    /// <summary>Tile each disconnected component in its own grid cell instead of stacking at the origin.</summary>
    [ObservableProperty] private bool _separateComponents;

    public LivePreviewViewModel() { } // design-time
    public LivePreviewViewModel(IDocumentService documents, IWorkspaceSession? session = null)
    {
        _documents = documents;
        _session = session;
        _documents.DocumentChanged += (_, _) => OnUi(Rebuild);
        _documents.ActiveDocumentChanged += (_, _) => OnUi(Rebuild);
        Rebuild();
    }

    partial void OnIsElevationChanged(bool value) => Rebuild();
    partial void OnSeparateComponentsChanged(bool value) => Rebuild();

    [RelayCommand] private void Refresh() => Rebuild();
    [RelayCommand] private void TogglePlanElevation() => IsElevation = !IsElevation;

    /// <summary>Sets the leg-colouring mode (mirrors the 3D viewer's Color-by buttons).</summary>
    [RelayCommand]
    private void SetColorMode(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        ColorMode = mode;
        // Re-raise so the choice buttons refresh their pushed state even when re-clicked.
        OnPropertyChanged(nameof(ColorMode));
    }

    /// <summary>Navigates the editor to a leg / lead / junction source (called by the control on click).</summary>
    public void Activate(SourceSpan span)
    {
        if (!span.IsEmpty && !string.IsNullOrEmpty(span.FilePath))
            _ = _documents?.NavigateToSpanAsync(span);
    }

    private void Rebuild()
    {
        var (shots, models) = Gather();
        if (shots.Count == 0)
        {
            Segments = Array.Empty<SketchSegment>();
            LeadMarkers = Array.Empty<LeadMarker>();
            EquateMarkers = Array.Empty<EquateMarker>();
            Status = "No centreline data to preview.";
            return;
        }

        // Merge equated stations so connected surveys lay out in continuation.
        var equates = BuildEquates(models);
        string Rep(QualifiedName qn) => equates.Find(qn).ToString();

        var layout = ComputeLayout(shots, equates);

        // Project every (merged) station into the current view's 2-D plane, then optionally spread
        // any genuinely disconnected components so they no longer overlap at the origin.
        var p2d = new Dictionary<string, (double X, double Y)>(layout.Positions.Count, StringComparer.Ordinal);
        foreach (var (key, p) in layout.Positions) p2d[key] = Project(p);
        if (SeparateComponents && layout.ComponentCount > 1)
            TileComponents(p2d, layout.Components, layout.ComponentCount);

        var segs = new List<SketchSegment>(shots.Count);
        var surveys = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shot in shots)
        {
            if ((shot.Flags & ShotFlags.Splay) != 0) continue;
            var fromRep = Rep(shot.From);
            var toRep = Rep(shot.To);
            if (!p2d.TryGetValue(fromRep, out var a) || !p2d.TryGetValue(toRep, out var b))
                continue;
            var survey = shot.From.HasParent ? shot.From.Parent().ToString() : "(root)";
            var file = shot.Span.FilePath ?? string.Empty;
            var component = layout.Components.TryGetValue(fromRep, out var ci) ? ci : 0;
            surveys.Add(survey);
            if (!string.IsNullOrEmpty(file)) files.Add(file);
            // Labels show each leg's own station names, even though equated endpoints share a point.
            segs.Add(new SketchSegment(a.X, a.Y, b.X, b.Y, shot.Span,
                shot.From.ToString(), shot.To.ToString(), survey, file, component));
        }
        Segments = segs;
        LeadMarkers = BuildLeadMarkers(p2d, equates);              // LEAD-02
        EquateMarkers = BuildEquateMarkers(models, equates, p2d);  // junctions

        Status = segs.Count == 0
            ? "No drawable legs (need length, compass and clino)."
            : $"{(IsElevation ? "Elevation" : "Plan")} · {segs.Count} legs · {surveys.Count} survey(s)" +
              $" · {files.Count} file(s) · {layout.ComponentCount} component(s)" +
              (EquateMarkers.Count > 0 ? $" · {EquateMarkers.Count} junction(s)" : string.Empty) +
              (layout.ComponentCount > 1 && !SeparateComponents
                  ? " · ⚠ disconnected pieces overlap — add equates, or try “Separate”/“Color: Component”"
                  : string.Empty) +
              (LeadMarkers.Count > 0 ? $" · {LeadMarkers.Count} lead(s)" : string.Empty) +
              " (preview only — not a Therion render)";
    }

    // ---- data gathering + thconfig scoping --------------------------------

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

    private static EquateGraph BuildEquates(IReadOnlyList<SemanticModel> models)
    {
        var graph = new EquateGraph();
        foreach (var model in models)
            foreach (var group in model.Equates.Groups())
                for (int i = 1; i < group.Length; i++)
                    graph.Union(group[0], group[i]);
        return graph;
    }

    // ---- markers ----------------------------------------------------------

    // LEAD-02: project each lead whose location is a centreline station into the sketch's frame.
    private IReadOnlyList<LeadMarker> BuildLeadMarkers(
        IReadOnlyDictionary<string, (double X, double Y)> p2d, EquateGraph equates)
    {
        var leads = LeadAnalysis.Analyze(_documents?.Workspace);
        if (leads.IsDefaultOrEmpty) return Array.Empty<LeadMarker>();
        var markers = new List<LeadMarker>();
        foreach (var lead in leads)
        {
            if (SafeParse(lead.Location) is not { } qn) continue;
            if (!p2d.TryGetValue(equates.Find(qn).ToString(), out var p)) continue;   // th2/scrap leads aren't centreline stations
            markers.Add(new LeadMarker(p.X, p.Y, lead.Location, lead.Kind, lead.Span));
        }
        return markers;
    }

    // One clickable junction per equate command, placed at the merged station's position.
    private IReadOnlyList<EquateMarker> BuildEquateMarkers(
        IReadOnlyList<SemanticModel> models, EquateGraph equates,
        IReadOnlyDictionary<string, (double X, double Y)> p2d)
    {
        // Fallback index: a station's last (point) name → a position, for resolving relative tokens.
        var byLast = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        foreach (var (key, p) in p2d) byLast.TryAdd(LastComponent(key), p);

        var markers = new List<EquateMarker>();
        foreach (var model in models)
            foreach (var rec in model.EquateRecords)
            {
                if (LocateEquate(rec.Stations, equates, p2d, byLast) is not { } p) continue;
                markers.Add(new EquateMarker(p.X, p.Y, string.Join(" = ", rec.Stations), rec.Span));
            }
        return markers;
    }

    // Resolves any one of an equate's stations to a drawn position (the members share a point once
    // merged, so the first that resolves wins). Tries the exact (rep) name, then a last-name fallback.
    private static (double X, double Y)? LocateEquate(
        ImmutableArray<string> stations, EquateGraph equates,
        IReadOnlyDictionary<string, (double X, double Y)> p2d, Dictionary<string, (double X, double Y)> byLast)
    {
        foreach (var raw in stations)
        {
            if (SafeParse(raw) is { } qn)
            {
                if (p2d.TryGetValue(equates.Find(qn).ToString(), out var pr)) return pr;
                if (p2d.TryGetValue(qn.ToString(), out var pq)) return pq;
            }
            if (byLast.TryGetValue(LastComponent(raw), out var pl)) return pl;
        }
        return null;
    }

    /// <summary>Parses a station token (dotted or <c>point@survey</c>) to a top-down qualified name.</summary>
    private static QualifiedName? SafeParse(string raw)
    {
        raw = raw?.Trim() ?? string.Empty;
        if (raw.Length == 0) return null;
        try { return QualifiedName.Parse(raw.Contains('@') ? StationRef.Parse(raw).StationQuery : raw); }
        catch { return null; }
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

    private (double X, double Y) Project((double E, double N, double Z) p) =>
        IsElevation ? (p.E, -p.Z)   // east vs up
                    : (p.E, -p.N);  // east vs north (north up)

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
