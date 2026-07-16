// Approximate per-station 3D positions by dead-reckoning traversal of the shot network — solver A of
// the dual-solver design. NO loop closure: errors accumulate along traverses and loops disagree; we
// *report* that disagreement (MisclosureHint) rather than hide it. The authoritative numbers come from
// Therion's own compiler (solver B, a later batch); this places a cave well enough to answer
// "which stations are near -500 m", not to survey by.
//
// Built on ConnectivityGraph (mergeCrossFileEquates:true) so nodes, equate folding and connected
// components are exactly what survey_graph reports — a station's ComponentId here means the same
// "piece" a caver would count. Positions are component-local (anchor at the origin); horizontal
// easting/northing from a georeferenced fix are deliberately NOT folded in (a lat/long fix is degrees,
// not metres — mixing them would corrupt the frame), so only absolute *altitude* is derived from a fix.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Therion.Semantics;

/// <summary>Which solver produced a position (see the dual-solver design).</summary>
public enum PositionSource
{
    /// <summary>No position available.</summary>
    None,
    /// <summary>Dead-reckoning estimate — no loop closure. Carries a misclosure error bar.</summary>
    Approximate,
    /// <summary>Read back from Therion's compiled, loop-closed output (a later batch).</summary>
    Compiled,
}

/// <summary>
/// An estimated station location. Axes are metres, X=east, Y=north, Z=up, in the component-local
/// frame (the component's anchor sits at the origin). <see cref="Depth"/> is metres below the
/// component datum (the highest entrance, or the highest surveyed point when the piece has none),
/// positive going down. <see cref="AbsoluteAltitude"/> is metres above the fix datum and is present
/// only when a <c>fix</c> with an elevation anchors the component. The reliability flags are false
/// when the station was reached through a shot missing the relevant reading; a consumer may suppress
/// an unreliable coordinate rather than present a fabricated one.
/// </summary>
public sealed record StationPosition(
    double X, double Y, double Z,
    double Depth,
    double? AbsoluteAltitude,
    int ComponentId,
    bool HorizontalReliable,
    bool VerticalReliable,
    double? MisclosureHint,
    PositionSource Source = PositionSource.Approximate);

/// <summary>
/// Every placeable station's estimated position, keyed by station name (each equate alias resolves to
/// the same physical point, so all aliases are present). A station that could not be placed — isolated,
/// or reachable only through shots with no length — is simply absent.
/// </summary>
public sealed record PositionSet(
    IReadOnlyDictionary<QualifiedName, StationPosition> Positions,
    PositionSource Source,
    string DatumDescription,
    DateTimeOffset ComputedUtc)
{
    /// <summary>An empty set (no workspace, or nothing placeable).</summary>
    public static PositionSet Empty { get; } = new(
        new Dictionary<QualifiedName, StationPosition>(),
        PositionSource.None,
        "no positions",
        DateTimeOffset.UnixEpoch);

    /// <summary>The position of <paramref name="station"/> (any equate alias), or null when unplaced.</summary>
    public StationPosition? For(QualifiedName station) =>
        Positions.TryGetValue(station, out var p) ? p : null;
}

/// <summary>Dead-reckoning position estimator (solver A). Pure; results depend only on the model.</summary>
public static class StationPositionEstimator
{
    private const double DegToRad = Math.PI / 180.0;

    // Immutable models are shared, so cache per instance (thread-safe, evicts with the model).
    private static readonly ConditionalWeakTable<WorkspaceSemanticModel, PositionSet> Cache = new();

    /// <summary>Cached estimate for <paramref name="model"/> (computed once per model instance).</summary>
    public static PositionSet Get(WorkspaceSemanticModel model) => Cache.GetValue(model, Estimate);

    /// <summary>Computes the estimate fresh (tests call this; production goes through <see cref="Get"/>).</summary>
    public static PositionSet Estimate(WorkspaceSemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.PerFile.Count == 0) return PositionSet.Empty;

        var graph = ConnectivityGraph.Build(model, mergeCrossFileEquates: true);

        var adjacency = BuildVectorAdjacency(model, graph);
        var fixByRep = FixesByRepresentative(model, graph);
        var entranceReps = graph.Entrances.ToHashSet();

        var byRep = new Dictionary<QualifiedName, StationPosition>();
        string? datumDescription = null;

        for (int componentId = 0; componentId < graph.Components.Length; componentId++)
            PlaceComponent(componentId, graph.Components[componentId], adjacency, fixByRep,
                entranceReps, byRep, ref datumDescription);

        // Expand representatives back to every alias so callers look up by any station name.
        var positions = new Dictionary<QualifiedName, StationPosition>();
        foreach (var name in AllStationNames(model))
            if (byRep.TryGetValue(graph.Representative(name), out var p))
                positions[name] = p;

        return positions.Count == 0
            ? PositionSet.Empty
            : new PositionSet(positions, PositionSource.Approximate,
                datumDescription ?? DefaultDatumDescription, DateTimeOffset.UtcNow);
    }

    private const string DefaultDatumDescription =
        "Approximate positions from dead-reckoning (no loop closure); depth is measured down from each "
        + "piece's highest entrance (or highest surveyed point when it has no entrance).";

    // ---- graph construction ---------------------------------------------------------------------

    /// <summary>A shot edge as a 3D vector between equate representatives, with per-axis reliability.</summary>
    private readonly record struct Edge(
        QualifiedName To, double Dx, double Dy, double Dz, bool HorizontalKnown, bool VerticalKnown);

    private static Dictionary<QualifiedName, List<Edge>> BuildVectorAdjacency(
        WorkspaceSemanticModel model, ConnectivityGraph graph)
    {
        var adjacency = new Dictionary<QualifiedName, List<Edge>>();
        List<Edge> EdgesOf(QualifiedName n) =>
            adjacency.TryGetValue(n, out var list) ? list : adjacency[n] = new List<Edge>();

        foreach (var file in model.PerFile.Values)
            foreach (var shot in file.Shots)
            {
                // Splays hang off wall points, not the survey skeleton (matches ConnectivityGraph).
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                if (shot.Length is not { } length) continue;   // no length → no geometry

                var a = graph.Representative(shot.From);
                var b = graph.Representative(shot.To);
                if (a.Equals(b)) continue;                      // equated onto itself

                var (dx, dy, dz, horiz, vert) = ShotVector(length, shot.Compass, shot.Clino);
                EdgesOf(a).Add(new Edge(b, dx, dy, dz, horiz, vert));
                EdgesOf(b).Add(new Edge(a, -dx, -dy, -dz, horiz, vert));
            }

        return adjacency;
    }

    /// <summary>
    /// The shot's displacement in metres (Therion conventions: compass 0=north clockwise, clino
    /// positive up). A missing clino is treated as horizontal (dz=0) and a missing compass drops the
    /// horizontal component (direction unknown); the reliability flags record which reading was absent.
    /// </summary>
    private static (double Dx, double Dy, double Dz, bool HorizontalKnown, bool VerticalKnown) ShotVector(
        double length, double? compass, double? clino)
    {
        bool clinoKnown = clino.HasValue;
        bool compassKnown = compass.HasValue;

        double horizontal = clinoKnown ? length * Math.Cos(clino!.Value * DegToRad) : length;
        double dz = clinoKnown ? length * Math.Sin(clino!.Value * DegToRad) : 0.0;

        double dx = 0.0, dy = 0.0;
        if (compassKnown)
        {
            double azimuth = compass!.Value * DegToRad;
            dx = horizontal * Math.Sin(azimuth);   // east
            dy = horizontal * Math.Cos(azimuth);   // north
        }

        // Horizontal is trustworthy only with both a direction and a true horizontal length (needs
        // clino); vertical needs the clino.
        return (dx, dy, dz, compassKnown && clinoKnown, clinoKnown);
    }

    /// <summary>Best fix per representative (a georeferenced fix beats a bare one); only fixes with an elevation.</summary>
    private static Dictionary<QualifiedName, (double Z, bool Georeferenced)> FixesByRepresentative(
        WorkspaceSemanticModel model, ConnectivityGraph graph)
    {
        var fixes = new Dictionary<QualifiedName, (double Z, bool Georeferenced)>();
        foreach (var station in model.StationsByQn.Values)
        {
            if (station.Kind != StationDeclarationKind.Fix || station.FixZ is not { } z) continue;
            var rep = graph.Representative(station.Name);
            bool georeferenced = !string.IsNullOrWhiteSpace(station.Cs);
            if (!fixes.TryGetValue(rep, out var current) || (georeferenced && !current.Georeferenced))
                fixes[rep] = (z, georeferenced);
        }
        return fixes;
    }

    // ---- per-component placement ----------------------------------------------------------------

    private readonly record struct Local(double X, double Y, double Z, bool Horizontal, bool Vertical);

    private static void PlaceComponent(
        int componentId,
        ImmutableArray<QualifiedName> members,
        Dictionary<QualifiedName, List<Edge>> adjacency,
        Dictionary<QualifiedName, (double Z, bool Georeferenced)> fixByRep,
        HashSet<QualifiedName> entranceReps,
        Dictionary<QualifiedName, StationPosition> output,
        ref string? datumDescription)
    {
        var anchor = ChooseAnchor(members, fixByRep, entranceReps);

        // BFS from the anchor accumulating edge vectors; first visit wins.
        var placed = new Dictionary<QualifiedName, Local> { [anchor] = new(0, 0, 0, true, true) };
        var queue = new Queue<QualifiedName>();
        queue.Enqueue(anchor);

        double misclosure = 0.0;
        bool sawLoop = false;

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            var pu = placed[u];
            if (!adjacency.TryGetValue(u, out var edges)) continue;
            foreach (var e in edges)
            {
                double nx = pu.X + e.Dx, ny = pu.Y + e.Dy, nz = pu.Z + e.Dz;
                if (!placed.TryGetValue(e.To, out var existing))
                {
                    placed[e.To] = new Local(nx, ny, nz,
                        pu.Horizontal && e.HorizontalKnown, pu.Vertical && e.VerticalKnown);
                    queue.Enqueue(e.To);
                }
                else
                {
                    // Re-reaching a placed node: the reverse of the edge we arrived by closes to ~0
                    // (harmless); an independent path is a real loop whose gap is the error bar.
                    double gap = Distance(nx, ny, nz, existing.X, existing.Y, existing.Z);
                    if (gap > misclosure) misclosure = gap;
                    if (gap > 1e-6) sawLoop = true;
                }
            }
        }

        // An isolated station with no shot connecting it sits at the arbitrary origin — depth 0 there
        // would be a fabricated fact (a splay wall-point, or a station named but never surveyed to).
        // Emit it only when a fix gives it a real altitude; otherwise say nothing.
        if (placed.Count == 1 && !fixByRep.ContainsKey(anchor)) return;

        var (absoluteOffset, multiFixDisagreement) = ResolveAltitude(members, fixByRep, placed);
        if (multiFixDisagreement is { } mf && mf > misclosure) misclosure = mf;

        double datumZ = DatumZ(members, entranceReps, placed, out bool datumFromEntrance);
        double? errorBar = sawLoop || multiFixDisagreement is not null ? misclosure : null;

        foreach (var (rep, p) in placed)
            output[rep] = new StationPosition(
                X: p.X, Y: p.Y, Z: p.Z,
                Depth: datumZ - p.Z,
                AbsoluteAltitude: absoluteOffset is { } off ? p.Z + off : null,
                ComponentId: componentId,
                HorizontalReliable: p.Horizontal,
                VerticalReliable: p.Vertical,
                MisclosureHint: errorBar,
                Source: PositionSource.Approximate);

        // Describe the datum from the main piece (largest component, id 0).
        if (componentId == 0)
            datumDescription =
                (datumFromEntrance
                    ? "Depth is measured down from the highest entrance"
                    : "Depth is measured down from the highest surveyed point (this piece has no entrance)")
                + (absoluteOffset is null
                    ? "; no absolute altitude (no georeferenced fix). "
                    : "; absolute altitude is anchored on a fix. ")
                + "Positions are approximate (dead-reckoning, no loop closure).";
    }

    /// <summary>Anchor priority: georeferenced fix &gt; any fix &gt; entrance &gt; lexically smallest node.</summary>
    private static QualifiedName ChooseAnchor(
        ImmutableArray<QualifiedName> members,
        Dictionary<QualifiedName, (double Z, bool Georeferenced)> fixByRep,
        HashSet<QualifiedName> entranceReps)
    {
        QualifiedName? georeferencedFix = null, anyFix = null, entrance = null;
        foreach (var m in members)
        {
            if (fixByRep.TryGetValue(m, out var f))
            {
                anyFix ??= m;
                if (f.Georeferenced) { georeferencedFix ??= m; }
            }
            if (entranceReps.Contains(m)) entrance ??= m;
        }
        // Components are sorted ordinal-ascending, so members[0] is the lexically smallest.
        return georeferencedFix ?? anyFix ?? entrance ?? members[0];
    }

    /// <summary>
    /// The offset that turns local Z into absolute altitude (from the best placed fix), plus the worst
    /// altitude disagreement between that fix and any other fix in the component. Both null when there
    /// is no usable fix / only one.
    /// </summary>
    private static (double? Offset, double? Disagreement) ResolveAltitude(
        ImmutableArray<QualifiedName> members,
        Dictionary<QualifiedName, (double Z, bool Georeferenced)> fixByRep,
        Dictionary<QualifiedName, Local> placed)
    {
        (QualifiedName Rep, double Z, bool Geo)? best = null;
        foreach (var m in members)
            if (fixByRep.TryGetValue(m, out var f) && placed.ContainsKey(m))
                if (best is null || (f.Georeferenced && !best.Value.Geo)) best = (m, f.Z, f.Georeferenced);

        if (best is not { } anchorFix) return (null, null);

        double offset = anchorFix.Z - placed[anchorFix.Rep].Z;

        double? disagreement = null;
        foreach (var m in members)
        {
            if (m.Equals(anchorFix.Rep) || !fixByRep.TryGetValue(m, out var f) || !placed.TryGetValue(m, out var p))
                continue;
            double gap = Math.Abs((p.Z + offset) - f.Z);
            if (disagreement is null || gap > disagreement) disagreement = gap;
        }
        return (offset, disagreement);
    }

    /// <summary>Datum Z: the highest entrance in the component, else the highest placed station.</summary>
    private static double DatumZ(
        ImmutableArray<QualifiedName> members,
        HashSet<QualifiedName> entranceReps,
        Dictionary<QualifiedName, Local> placed,
        out bool fromEntrance)
    {
        double? entranceMax = null;
        foreach (var m in members)
            if (entranceReps.Contains(m) && placed.TryGetValue(m, out var p))
                entranceMax = entranceMax is { } e ? Math.Max(e, p.Z) : p.Z;

        if (entranceMax is { } datum) { fromEntrance = true; return datum; }

        fromEntrance = false;
        double highest = 0.0;
        bool any = false;
        foreach (var p in placed.Values) { highest = any ? Math.Max(highest, p.Z) : p.Z; any = true; }
        return highest;
    }

    // ---- shared helpers -------------------------------------------------------------------------

    private static double Distance(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx, dy = ay - by, dz = az - bz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Every station name that could carry a position: declared stations and shot endpoints.</summary>
    private static IEnumerable<QualifiedName> AllStationNames(WorkspaceSemanticModel model)
    {
        var seen = new HashSet<QualifiedName>();
        foreach (var station in model.StationsByQn.Values)
            if (seen.Add(station.Name)) yield return station.Name;
        foreach (var file in model.PerFile.Values)
            foreach (var shot in file.Shots)
            {
                if (seen.Add(shot.From)) yield return shot.From;
                if (seen.Add(shot.To)) yield return shot.To;
            }
    }
}
