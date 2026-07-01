// Phase 2 — pure centreline geometry: world station positions + the cave main-line.
//
// Replicates the spanning-tree solve from LivePreviewViewModel.ComputeLayout (same polar→cartesian
// convention) but free of Avalonia/UI types, so the core lib can co-locate fitted planes with the
// cave line. Stations are keyed by their equate representative so equated endpoints merge into one
// node (surveys draw in continuation). Disconnected components are each rooted at the origin; callers
// that need them apart can tile in projected space (the UI already does this for the preview).

using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Semantics;

namespace Therion.Structural;

/// <summary>World positions + legs from a centreline solve. <see cref="PositionOf"/> resolves equates.</summary>
public sealed class CenterlineSolution
{
    private readonly IReadOnlyDictionary<QualifiedName, Vec3> _positions; // keyed by equate representative
    private readonly EquateGraph? _equates;

    internal CenterlineSolution(
        IReadOnlyDictionary<QualifiedName, Vec3> positions, EquateGraph? equates,
        ImmutableArray<(Vec3 A, Vec3 B)> legs, int componentCount)
    {
        _positions = positions;
        _equates = equates;
        CaveLegs = legs;
        ComponentCount = componentCount;
    }

    /// <summary>The centreline legs (non-splay shots with full data) as world-space segments.</summary>
    public ImmutableArray<(Vec3 A, Vec3 B)> CaveLegs { get; }

    /// <summary>Number of disconnected components in the network.</summary>
    public int ComponentCount { get; }

    /// <summary>World position of a station (resolving equates), or null if it isn't placed.</summary>
    public Vec3? PositionOf(QualifiedName station)
    {
        var rep = _equates is null ? station : _equates.Find(station);
        return _positions.TryGetValue(rep, out var p) ? p : null;
    }
}

public static class CenterlineGeometry
{
    /// <summary>Polar (length, compass°, clino°) → cartesian vector in the E/N/Up frame.</summary>
    public static Vec3 ShotVector(double length, double compassDeg, double clinoDeg)
    {
        double cl = clinoDeg * System.Math.PI / 180.0;
        double c = compassDeg * System.Math.PI / 180.0;
        double horiz = length * System.Math.Cos(cl);
        return new Vec3(horiz * System.Math.Sin(c), horiz * System.Math.Cos(c), length * System.Math.Sin(cl));
    }

    /// <summary>
    /// Solves world positions for every station reachable through full, non-splay legs, plus the legs
    /// themselves for drawing the cave main-line. Pure.
    /// </summary>
    public static CenterlineSolution Solve(IReadOnlyList<ShotSymbol> shots, EquateGraph? equates = null)
    {
        QualifiedName Rep(QualifiedName qn) => equates is null ? qn : equates.Find(qn);

        // Build adjacency over representatives from the drawable legs.
        var adj = new Dictionary<QualifiedName, List<(QualifiedName To, Vec3 V)>>();
        void Link(QualifiedName a, QualifiedName b, Vec3 v)
        {
            (adj.TryGetValue(a, out var la) ? la : adj[a] = new()).Add((b, v));
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = new()).Add((a, v * -1.0));
        }

        var legShots = new List<(QualifiedName From, QualifiedName To, Vec3 V)>();
        foreach (var s in shots)
        {
            if ((s.Flags & ShotFlags.Splay) != 0) continue;
            if (s.Length is not { } len || s.Compass is not { } c || s.Clino is not { } cl) continue;
            var v = ShotVector(len, c, cl);
            var ra = Rep(s.From);
            var rb = Rep(s.To);
            Link(ra, rb, v);
            legShots.Add((ra, rb, v));
        }

        // BFS each component from an arbitrary root at the origin.
        var pos = new Dictionary<QualifiedName, Vec3>();
        int components = 0;
        foreach (var start in adj.Keys)
        {
            if (pos.ContainsKey(start)) continue;
            pos[start] = Vec3.Zero;
            components++;
            var queue = new Queue<QualifiedName>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                var pu = pos[u];
                foreach (var (v, vec) in adj[u])
                    if (!pos.ContainsKey(v))
                    {
                        pos[v] = pu + vec;
                        queue.Enqueue(v);
                    }
            }
        }

        var legs = ImmutableArray.CreateBuilder<(Vec3 A, Vec3 B)>(legShots.Count);
        foreach (var (from, to, _) in legShots)
            if (pos.TryGetValue(from, out var a) && pos.TryGetValue(to, out var b))
                legs.Add((a, b));

        return new CenterlineSolution(pos, equates, legs.ToImmutable(), components);
    }
}
