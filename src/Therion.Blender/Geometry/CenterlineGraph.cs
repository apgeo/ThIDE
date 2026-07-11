namespace Therion.Blender.Geometry;

/// <summary>
/// One connected component of the survey centerline: the stations it spans plus their
/// centroid and bounds. Feeds component labels (BA-B8) and scene-meta (BA-B3).
/// </summary>
public sealed record CenterlineComponent
{
    public required int Index { get; init; }
    public required IReadOnlyList<int> StationIndices { get; init; }
    public required CaveVector3 Centroid { get; init; }
    public required BoundingBox Bounds { get; init; }
    public int StationCount => StationIndices.Count;
}

/// <summary>
/// The centerline as a graph over stations connected by real survey legs (splay,
/// surface and duplicate legs excluded — they are not structural passage). Endpoints
/// are matched to stations by exact position, so it works for both formats
/// (<c>.lox</c> shots carry station ids, <c>.3d</c> legs carry only coordinates).
/// </summary>
public sealed class CenterlineGraph
{
    private readonly IReadOnlyList<CaveStation> _stations;

    public IReadOnlyList<CenterlineComponent> Components { get; }

    /// <summary>Undirected station-index pairs for the structural legs used.</summary>
    public IReadOnlyList<(int From, int To)> Edges { get; }

    private CenterlineGraph(
        IReadOnlyList<CaveStation> stations,
        IReadOnlyList<(int, int)> edges,
        IReadOnlyList<CenterlineComponent> components)
    {
        _stations = stations;
        Edges = edges;
        Components = components;
    }

    public CaveVector3 StationPosition(int index) => _stations[index].Position;

    /// <summary>
    /// The longest passage through the centerline, as an ordered polyline of station
    /// positions — the flythrough route (BA-B6). Computed by a double-sweep of Dijkstra
    /// over the largest connected component (weighted by leg length): the graph-diameter
    /// heuristic, deterministic, and cycle-tolerant. Returns an empty list when there are
    /// no structural legs.
    /// </summary>
    public IReadOnlyList<CaveVector3> LongestPathPolyline()
    {
        if (Components.Count == 0 || Edges.Count == 0) return [];

        // Adjacency over stations that take part in a structural leg (leg length weights).
        var adjacency = new Dictionary<int, List<(int To, double Weight)>>();
        void Link(int a, int b)
        {
            double w = (_stations[a].Position - _stations[b].Position).Length;
            (adjacency.TryGetValue(a, out var list) ? list : adjacency[a] = []).Add((b, w));
        }
        foreach (var (from, to) in Edges) { Link(from, to); Link(to, from); }

        // Largest component (deterministic: components are index-ordered), start at its
        // lowest station index; both Dijkstra sweeps stay inside that component.
        var largest = Components.OrderByDescending(c => c.StationCount).ThenBy(c => c.Index).First();
        int start = largest.StationIndices[0];

        int endA = Farthest(adjacency, start, out _);
        int endB = Farthest(adjacency, endA, out var prev);

        // Walk predecessors from endB back to endA, then reverse to get A → B order.
        var indices = new List<int>();
        for (int node = endB; node != -1; node = prev[node]) indices.Add(node);
        indices.Reverse();

        var polyline = new CaveVector3[indices.Count];
        for (int i = 0; i < indices.Count; i++) polyline[i] = _stations[indices[i]].Position;
        return polyline;
    }

    /// <summary>Dijkstra from <paramref name="source"/>; returns the farthest reachable
    /// node (ties broken by lowest index) and the predecessor map for path reconstruction.</summary>
    private static int Farthest(
        Dictionary<int, List<(int To, double Weight)>> adjacency, int source, out Dictionary<int, int> prev)
    {
        var dist = new Dictionary<int, double> { [source] = 0.0 };
        prev = new Dictionary<int, int> { [source] = -1 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(source, 0.0);

        int best = source;
        double bestDist = 0.0;
        while (queue.TryDequeue(out int node, out double d))
        {
            if (d > dist[node]) continue; // stale entry
            if (d > bestDist || (d == bestDist && node < best)) { bestDist = d; best = node; }
            if (!adjacency.TryGetValue(node, out var neighbours)) continue;
            foreach (var (to, weight) in neighbours)
            {
                double nd = d + weight;
                if (!dist.TryGetValue(to, out double old) || nd < old)
                {
                    dist[to] = nd;
                    prev[to] = node;
                    queue.Enqueue(to, nd);
                }
            }
        }
        return best;
    }

    public static CenterlineGraph Build(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stations = model.Stations;

        // Map station position → index. A .3d file can emit several stations at one
        // point (named + anonymous); first writer wins as the join target.
        var byPosition = new Dictionary<CaveVector3, int>(stations.Count);
        for (int i = 0; i < stations.Count; i++)
            byPosition.TryAdd(stations[i].Position, i);

        var parent = new int[stations.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        var edges = new List<(int, int)>();
        const CaveShotFlags skip = CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;
        foreach (var shot in model.Shots)
        {
            if ((shot.Flags & skip) != 0) continue;
            if (!byPosition.TryGetValue(shot.FromPosition, out int from)) continue;
            if (!byPosition.TryGetValue(shot.ToPosition, out int to)) continue;
            if (from == to) continue;
            edges.Add((from, to));
            Union(parent, from, to);
        }

        // Group stations that participate in at least one structural leg by their root.
        var members = new Dictionary<int, List<int>>();
        foreach (var (from, to) in edges)
        {
            AddMember(members, parent, from);
            AddMember(members, parent, to);
        }

        var components = new List<CenterlineComponent>();
        foreach (var (_, indices) in members.OrderBy(kv => kv.Value.Min()))
        {
            indices.Sort();
            var box = BoundingBox.Empty;
            var sum = CaveVector3.Zero;
            foreach (var i in indices)
            {
                var p = stations[i].Position;
                box = box.Encapsulate(p);
                sum += p;
            }
            components.Add(new CenterlineComponent
            {
                Index = components.Count,
                StationIndices = indices,
                Centroid = sum / indices.Count,
                Bounds = box,
            });
        }

        return new CenterlineGraph(stations, edges, components);
    }

    private static void AddMember(Dictionary<int, List<int>> members, int[] parent, int node)
    {
        int root = Find(parent, node);
        if (!members.TryGetValue(root, out var list))
            members[root] = list = [];
        if (!list.Contains(node)) list.Add(node);
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }
        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra != rb) parent[ra] = rb;
    }
}
