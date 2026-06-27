// LANG-06 — the equate / fix / station connectivity graph as a first-class, queryable model.
// Built on top of a bound SemanticModel: nodes are equate-merged stations, edges are non-splay
// shots. Powers statistics (DATA-01), disconnection diagnostics, dead-end/lead detection
// (LEAD-05), and is reused by the relational map.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Therion.Semantics;

/// <summary>
/// An immutable connectivity view of a survey: stations joined by shots, with equated stations
/// merged into a single node. Provides reachability, connected components, entrances, fixed
/// points and dead-ends.
/// </summary>
public sealed class ConnectivityGraph
{
    private readonly EquateGraph _equates;
    private readonly Dictionary<QualifiedName, HashSet<QualifiedName>> _adjacency;

    /// <summary>Total number of distinct (equate-merged) station nodes.</summary>
    public int NodeCount { get; }

    /// <summary>Number of graph edges (distinct non-splay shot connections between nodes).</summary>
    public int EdgeCount { get; }

    /// <summary>Stations carrying the <c>entrance</c> flag (as their equate representatives).</summary>
    public ImmutableArray<QualifiedName> Entrances { get; }

    /// <summary>Fixed stations: declared via <c>fix</c> or marked <c>fixed</c> (representatives).</summary>
    public ImmutableArray<QualifiedName> FixedStations { get; }

    /// <summary>
    /// Connected components, each a sorted list of member node names. A fully connected survey
    /// has exactly one; more than one indicates disconnected pieces (missing equates / fixes).
    /// </summary>
    public ImmutableArray<ImmutableArray<QualifiedName>> Components { get; }

    /// <summary>
    /// Dead-end nodes: degree ≤ 1 and not an entrance or fixed point. Strong candidates for
    /// unsurveyed leads when not already flagged <c>continuation</c> (LEAD-05).
    /// </summary>
    public ImmutableArray<QualifiedName> DeadEnds { get; }

    private ConnectivityGraph(
        EquateGraph equates,
        Dictionary<QualifiedName, HashSet<QualifiedName>> adjacency,
        ImmutableArray<QualifiedName> entrances,
        ImmutableArray<QualifiedName> fixedStations,
        ImmutableArray<ImmutableArray<QualifiedName>> components,
        ImmutableArray<QualifiedName> deadEnds,
        int edgeCount)
    {
        _equates = equates;
        _adjacency = adjacency;
        NodeCount = adjacency.Count;
        EdgeCount = edgeCount;
        Entrances = entrances;
        FixedStations = fixedStations;
        Components = components;
        DeadEnds = deadEnds;
    }

    /// <summary>The equate-merged representative of <paramref name="station"/>.</summary>
    public QualifiedName Representative(QualifiedName station) => _equates.Find(station);

    /// <summary>Number of distinct neighbours of a station's node.</summary>
    public int Degree(QualifiedName station) =>
        _adjacency.TryGetValue(_equates.Find(station), out var n) ? n.Count : 0;

    /// <summary>True if the two stations are in the same connected component.</summary>
    public bool AreConnected(QualifiedName a, QualifiedName b)
    {
        var ra = _equates.Find(a);
        var rb = _equates.Find(b);
        if (!_adjacency.ContainsKey(ra) || !_adjacency.ContainsKey(rb)) return false;
        if (ra.Equals(rb)) return true;

        var seen = new HashSet<QualifiedName> { ra };
        var queue = new Queue<QualifiedName>();
        queue.Enqueue(ra);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var next in _adjacency[cur])
            {
                if (next.Equals(rb)) return true;
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>Builds the connectivity graph for a single bound file.</summary>
    public static ConnectivityGraph Build(SemanticModel model) =>
        Build(model.Stations.Values, model.Shots, model.Equates);

    /// <summary>
    /// Builds the connectivity graph for a whole workspace by aggregating every per-file model.
    /// Cross-file <c>@</c> equates are not merged here (only the per-file equate classes are);
    /// shots and same-file equates from all files are combined.
    /// </summary>
    public static ConnectivityGraph Build(WorkspaceSemanticModel workspace)
    {
        var stations = new List<StationSymbol>();
        var shots = ImmutableArray.CreateBuilder<ShotSymbol>();
        var equates = new EquateGraph();
        foreach (var model in workspace.PerFile.Values)
        {
            stations.AddRange(model.Stations.Values);
            shots.AddRange(model.Shots);
            foreach (var group in model.Equates.Groups())
                for (int i = 1; i < group.Length; i++)
                    equates.Union(group[0], group[i]);
        }
        return Build(stations, shots.ToImmutable(), equates);
    }

    private static ConnectivityGraph Build(
        IEnumerable<StationSymbol> stations,
        ImmutableArray<ShotSymbol> shots,
        EquateGraph equates)
    {
        var adjacency = new Dictionary<QualifiedName, HashSet<QualifiedName>>();
        var entrances = new HashSet<QualifiedName>();
        var fixedStations = new HashSet<QualifiedName>();

        HashSet<QualifiedName> NeighboursOf(QualifiedName n)
        {
            if (!adjacency.TryGetValue(n, out var set))
                adjacency[n] = set = new HashSet<QualifiedName>();
            return set;
        }

        // Every station becomes a node (so isolated/fixed-only stations still appear).
        foreach (var st in stations)
        {
            var rep = equates.Find(st.Name);
            NeighboursOf(rep);
            if (st.IsEntrance) entrances.Add(rep);
            if (st.Kind == StationDeclarationKind.Fix ||
                string.Equals(st.MarkType, "fixed", System.StringComparison.OrdinalIgnoreCase))
                fixedStations.Add(rep);
        }

        // Edges from non-splay shots (splays connect to wall points, not the survey skeleton).
        int edgeCount = 0;
        foreach (var shot in shots)
        {
            if ((shot.Flags & ShotFlags.Splay) != 0) continue;
            var a = equates.Find(shot.From);
            var b = equates.Find(shot.To);
            if (a.Equals(b)) continue;
            if (NeighboursOf(a).Add(b)) edgeCount++;
            NeighboursOf(b).Add(a);
        }

        var components = ComputeComponents(adjacency);

        var deadEnds = ImmutableArray.CreateBuilder<QualifiedName>();
        foreach (var (node, neighbours) in adjacency)
            if (neighbours.Count <= 1 && !entrances.Contains(node) && !fixedStations.Contains(node))
                deadEnds.Add(node);

        return new ConnectivityGraph(
            equates, adjacency,
            entrances.OrderBy(n => n.ToString(), System.StringComparer.Ordinal).ToImmutableArray(),
            fixedStations.OrderBy(n => n.ToString(), System.StringComparer.Ordinal).ToImmutableArray(),
            components,
            deadEnds.ToImmutable(),
            edgeCount);
    }

    private static ImmutableArray<ImmutableArray<QualifiedName>> ComputeComponents(
        Dictionary<QualifiedName, HashSet<QualifiedName>> adjacency)
    {
        var seen = new HashSet<QualifiedName>();
        var components = ImmutableArray.CreateBuilder<ImmutableArray<QualifiedName>>();
        foreach (var start in adjacency.Keys)
        {
            if (!seen.Add(start)) continue;
            var members = new List<QualifiedName> { start };
            var queue = new Queue<QualifiedName>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var next in adjacency[cur])
                    if (seen.Add(next)) { members.Add(next); queue.Enqueue(next); }
            }
            members.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.ToString(), b.ToString()));
            components.Add(members.ToImmutableArray());
        }
        // Largest component first — usually the main cave.
        return components.OrderByDescending(c => c.Length).ToImmutableArray();
    }
}
