// VIS-02 — live centreline preview. Plots the parsed centreline (from our own model, no Therion
// compile) as a quick plan or elevation sketch that refreshes as you edit. Positions are a
// relative spanning-tree layout from shot length/compass/clino; click a leg to jump to its source.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Semantics;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>A drawn leg in world coordinates (Y already flipped so north/up points up).</summary>
public sealed record SketchSegment(double X1, double Y1, double X2, double Y2, SourceSpan Span);

/// <summary>LEAD-02: a lead plotted at a station's world position, coloured by kind, click→source.</summary>
public sealed record LeadMarker(double X, double Y, string Location, LeadKind Kind, SourceSpan Span);

public sealed partial class LivePreviewViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;

    [ObservableProperty] private IReadOnlyList<SketchSegment> _segments = Array.Empty<SketchSegment>();
    [ObservableProperty] private IReadOnlyList<LeadMarker> _leadMarkers = Array.Empty<LeadMarker>();   // LEAD-02
    [ObservableProperty] private bool _isElevation;
    [ObservableProperty] private string _status = "No centreline yet.";

    public LivePreviewViewModel() { } // design-time
    public LivePreviewViewModel(IDocumentService documents)
    {
        _documents = documents;
        _documents.DocumentChanged += (_, _) => OnUi(Rebuild);
        _documents.ActiveDocumentChanged += (_, _) => OnUi(Rebuild);
        Rebuild();
    }

    partial void OnIsElevationChanged(bool value) => Rebuild();

    [RelayCommand] private void Refresh() => Rebuild();
    [RelayCommand] private void TogglePlanElevation() => IsElevation = !IsElevation;

    /// <summary>Navigates the editor to a leg's source (called by the control on click).</summary>
    public void Activate(SourceSpan span)
    {
        if (!span.IsEmpty && !string.IsNullOrEmpty(span.FilePath))
            _ = _documents?.NavigateToSpanAsync(span);
    }

    private void Rebuild()
    {
        var shots = GatherShots();
        if (shots.Count == 0) { Segments = Array.Empty<SketchSegment>(); Status = "No centreline data to preview."; return; }

        var pos = LayoutPositions(shots);
        var segs = new List<SketchSegment>(shots.Count);
        foreach (var shot in shots)
        {
            if ((shot.Flags & ShotFlags.Splay) != 0) continue;
            if (!pos.TryGetValue(shot.From.ToString(), out var a) || !pos.TryGetValue(shot.To.ToString(), out var b))
                continue;
            var (x1, y1) = Project(a);
            var (x2, y2) = Project(b);
            segs.Add(new SketchSegment(x1, y1, x2, y2, shot.Span));
        }
        Segments = segs;
        LeadMarkers = BuildLeadMarkers(pos);   // LEAD-02
        Status = segs.Count == 0
            ? "No drawable legs (need length, compass and clino)."
            : $"{(IsElevation ? "Elevation" : "Plan")} · {segs.Count} legs" +
              (LeadMarkers.Count > 0 ? $" · {LeadMarkers.Count} lead(s)" : "") +
              " (preview only — not a Therion render)";
    }

    // LEAD-02: project each lead whose location is a centreline station into the sketch's frame.
    private IReadOnlyList<LeadMarker> BuildLeadMarkers(Dictionary<string, (double E, double N, double Z)> pos)
    {
        var leads = LeadAnalysis.Analyze(_documents?.Workspace);
        if (leads.IsDefaultOrEmpty) return Array.Empty<LeadMarker>();
        var markers = new List<LeadMarker>();
        foreach (var lead in leads)
        {
            if (!pos.TryGetValue(lead.Location, out var p)) continue;   // th2/scrap leads aren't centreline stations
            var (x, y) = Project(p);
            markers.Add(new LeadMarker(x, y, lead.Location, lead.Kind, lead.Span));
        }
        return markers;
    }

    private (double X, double Y) Project((double E, double N, double Z) p) =>
        IsElevation ? (p.E, -p.Z)   // east vs up
                    : (p.E, -p.N);  // east vs north (north up)

    private List<ShotSymbol> GatherShots()
    {
        var list = new List<ShotSymbol>();
        if (_documents?.Workspace is { PerFile.Count: > 0 } ws)
            foreach (var model in ws.PerFile.Values) list.AddRange(model.Shots);
        else if (_documents?.CurrentSemantics is { } m)
            list.AddRange(m.Shots);
        return list;
    }

    // Relative spanning-tree layout: BFS each component from an arbitrary root at the origin,
    // accumulating each leg's 3-D vector. Splays and incomplete legs are skipped.
    private static Dictionary<string, (double E, double N, double Z)> LayoutPositions(List<ShotSymbol> shots)
    {
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
            Link(shot.From.ToString(), shot.To.ToString(),
                horiz * Math.Sin(c2), horiz * Math.Cos(c2), len * Math.Sin(cl2));
        }

        var pos = new Dictionary<string, (double E, double N, double Z)>(StringComparer.Ordinal);
        foreach (var start in adj.Keys)
        {
            if (pos.ContainsKey(start)) continue;
            pos[start] = (0, 0, 0);
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                var pu = pos[u];
                foreach (var (v, e, n, z) in adj[u])
                    if (!pos.ContainsKey(v)) { pos[v] = (pu.E + e, pu.N + n, pu.Z + z); queue.Enqueue(v); }
            }
        }
        return pos;
    }

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
