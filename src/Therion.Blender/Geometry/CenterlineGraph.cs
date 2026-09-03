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
/// The shape of one survey leg between two stations, measured from the station positions.
/// </summary>
/// <param name="From">Station index the leg is measured from.</param>
/// <param name="To">Station index the leg is measured to.</param>
/// <param name="Length">Straight three-dimensional distance between the two stations.</param>
/// <param name="PlanLength">The same distance projected onto the horizontal plane; zero for a
/// leg that goes straight up or down.</param>
/// <param name="AzimuthDegrees">Compass bearing of <c>From → To</c>, degrees clockwise from
/// north in [0, 360). <see cref="double.NaN"/> when the leg is vertical and so has no bearing.
/// Callers that treat a leg as undirected fold this into [0, 180) themselves — the two ends of
/// one leg give bearings 180° apart, and which one you get depends on which station the file
/// happened to list first.</param>
/// <param name="DipDegrees">Inclination of <c>From → To</c> in degrees, positive when
/// <c>To</c> is the higher station; ±90 for a vertical leg.</param>
public readonly record struct CenterlineEdgeGeometry(
    int From,
    int To,
    double Length,
    double PlanLength,
    double AzimuthDegrees,
    double DipDegrees);

/// <summary>
/// The centerline as a graph over stations connected by real survey legs (splay,
/// surface and duplicate legs excluded — they are not structural passage). Endpoints
/// are matched to stations by exact position, so it works for both formats
/// (<c>.lox</c> shots carry station ids, <c>.3d</c> legs carry only coordinates).
/// </summary>
/// <remarks>
/// Two edge lists are published and they are not interchangeable. <see cref="Edges"/> is the
/// raw list of legs used, one entry per leg, so a passage surveyed twice appears twice; it is
/// what says how much of the file became network. <see cref="DistinctEdges"/> is the same set
/// with repeated pairs collapsed, which is what degree, adjacency and every count derived from
/// them are measured over — a station where one passage was surveyed twice is not a junction.
/// </remarks>
public sealed class CenterlineGraph
{
    private readonly IReadOnlyList<CaveStation> _stations;
    private readonly Dictionary<int, int[]> _adjacency;

    public IReadOnlyList<CenterlineComponent> Components { get; }

    /// <summary>Undirected station-index pairs for the structural legs used, one entry per leg:
    /// a pair repeats when the same passage was surveyed more than once.</summary>
    public IReadOnlyList<(int From, int To)> Edges { get; }

    /// <summary>
    /// <see cref="Edges"/> with repeated pairs collapsed, each stored with the lower station
    /// index first and the list ordered by that pair. This is the graph in the graph-theory
    /// sense, and the only edge list a degree or an edge count may be taken from.
    /// </summary>
    public IReadOnlyList<(int From, int To)> DistinctEdges { get; }

    /// <summary>
    /// How many structural legs the build could not turn into an edge: one or both endpoints
    /// stood at no station, or the two endpoints resolved to the same station because their
    /// positions had already been merged. Splay, surface and duplicate legs are not counted —
    /// they were never network, so leaving them out is not a loss.
    /// </summary>
    /// <remarks>
    /// Published because the loss is otherwise silent: an incomplete network still yields
    /// counts, degrees and path lengths that look entirely reasonable, and this is the only
    /// figure that says they were computed over less than the file contained.
    /// </remarks>
    public int DroppedShotCount { get; }

    /// <summary>
    /// How many stations stood at a position an earlier station already occupied and so became
    /// the same node. A file that names a station twice at one point is ordinary; the count
    /// matters because merging is what causes legs to collapse into
    /// <see cref="DroppedShotCount"/>.
    /// </summary>
    public int MergedStationCount { get; }

    private CenterlineGraph(
        IReadOnlyList<CaveStation> stations,
        IReadOnlyList<(int, int)> edges,
        IReadOnlyList<(int, int)> distinctEdges,
        Dictionary<int, int[]> adjacency,
        IReadOnlyList<CenterlineComponent> components,
        int droppedShotCount,
        int mergedStationCount)
    {
        _stations = stations;
        _adjacency = adjacency;
        Edges = edges;
        DistinctEdges = distinctEdges;
        Components = components;
        DroppedShotCount = droppedShotCount;
        MergedStationCount = mergedStationCount;
    }

    public CaveVector3 StationPosition(int index) => _stations[index].Position;

    /// <summary>The station's name as the survey file gave it.</summary>
    public string StationName(int index) => _stations[index].Name;

    /// <summary>
    /// The stations directly joined to this one, in ascending index order and each listed once
    /// however many times the passage between them was surveyed. Empty for a station no
    /// structural leg reaches.
    /// </summary>
    public IReadOnlyList<int> Neighbours(int station) =>
        _adjacency.TryGetValue(station, out var list) ? list : [];

    /// <summary>How many distinct stations this one is joined to.</summary>
    public int Degree(int station) => _adjacency.TryGetValue(station, out var list) ? list.Length : 0;

    /// <summary>Stations where three or more passages meet.</summary>
    public IEnumerable<int> Junctions => _adjacency.Where(kv => kv.Value.Length > 2).Select(kv => kv.Key).Order();

    /// <summary>Stations where the survey stops: one passage in, none out.</summary>
    public IEnumerable<int> Extremities => _adjacency.Where(kv => kv.Value.Length == 1).Select(kv => kv.Key).Order();

    /// <summary>The shape of one leg, measured from the two station positions.</summary>
    public CenterlineEdgeGeometry EdgeGeometry(int from, int to)
    {
        var a = _stations[from].Position;
        var b = _stations[to].Position;
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;

        double plan = Math.Sqrt((dx * dx) + (dy * dy));
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        // A leg with no horizontal run points nowhere on the compass, and saying "north" for it
        // would put a pitch into whichever orientation bin happens to start at zero.
        double azimuth = plan == 0
            ? double.NaN
            : NormalizeDegrees(RadiansToDegrees(Math.Atan2(dx, dy)));
        double dip = plan == 0
            ? (dz >= 0 ? 90.0 : -90.0)
            : RadiansToDegrees(Math.Atan(dz / plan));

        return new CenterlineEdgeGeometry(from, to, length, plan, azimuth, dip);
    }

    /// <summary>The shape of every distinct leg, in <see cref="DistinctEdges"/> order.</summary>
    public IReadOnlyList<CenterlineEdgeGeometry> EdgeGeometries()
    {
        var result = new CenterlineEdgeGeometry[DistinctEdges.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var (from, to) = DistinctEdges[i];
            result[i] = EdgeGeometry(from, to);
        }
        return result;
    }

    /// <summary>
    /// Shortest distance from <paramref name="source"/> to every station reachable from it,
    /// including the source at zero. Unreachable stations are absent rather than infinite.
    /// </summary>
    /// <param name="source">Station index to measure from.</param>
    /// <param name="weighted">
    /// True to measure in metres along the legs, false to count legs traversed. The two answer
    /// different questions: how far somebody walks, and how many junctions apart two places are.
    /// </param>
    public IReadOnlyDictionary<int, double> ShortestPathLengths(int source, bool weighted = true) =>
        Dijkstra(source, weighted, out _);

    /// <summary>
    /// The passage network with every run of degree-2 stations contracted into a single branch
    /// between the junctions and dead ends at its ends.
    /// </summary>
    public CenterlineReducedGraph Reduce() => CenterlineReducedGraph.Build(this);

    /// <summary>
    /// The longest passage through the centerline, as an ordered polyline of station
    /// positions — the flythrough route (BA-B6). Computed by a double-sweep of Dijkstra
    /// over the largest connected component (weighted by leg length): the graph-diameter
    /// heuristic, deterministic, and cycle-tolerant. Returns an empty list when there are
    /// no structural legs.
    /// </summary>
    public IReadOnlyList<CaveVector3> LongestPathPolyline()
    {
        if (Components.Count == 0 || DistinctEdges.Count == 0) return [];

        // Largest component (deterministic: components are index-ordered), start at its
        // lowest station index; both Dijkstra sweeps stay inside that component.
        var largest = Components.OrderByDescending(c => c.StationCount).ThenBy(c => c.Index).First();
        int start = largest.StationIndices[0];

        int endA = Farthest(start, out _);
        int endB = Farthest(endA, out var prev);

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
    private int Farthest(int source, out Dictionary<int, int> prev)
    {
        var dist = Dijkstra(source, weighted: true, out prev);

        int best = source;
        double bestDist = 0.0;
        foreach (var (node, d) in dist)
        {
            if (d > bestDist || (d == bestDist && node < best)) { bestDist = d; best = node; }
        }
        return best;
    }

    private Dictionary<int, double> Dijkstra(int source, bool weighted, out Dictionary<int, int> prev)
    {
        var dist = new Dictionary<int, double> { [source] = 0.0 };
        prev = new Dictionary<int, int> { [source] = -1 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(source, 0.0);

        while (queue.TryDequeue(out int node, out double d))
        {
            if (d > dist[node]) continue; // stale entry
            foreach (int to in Neighbours(node))
            {
                double weight = weighted ? (_stations[node].Position - _stations[to].Position).Length : 1.0;
                double nd = d + weight;
                if (!dist.TryGetValue(to, out double old) || nd < old)
                {
                    dist[to] = nd;
                    prev[to] = node;
                    queue.Enqueue(to, nd);
                }
            }
        }
        return dist;
    }

    public static CenterlineGraph Build(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stations = model.Stations;

        // Map station position → index. A .3d file can emit several stations at one
        // point (named + anonymous); first writer wins as the join target.
        var byPosition = new Dictionary<CaveVector3, int>(stations.Count);
        int merged = 0;
        for (int i = 0; i < stations.Count; i++)
        {
            if (!byPosition.TryAdd(stations[i].Position, i)) merged++;
        }

        var parent = new int[stations.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        var edges = new List<(int, int)>();
        int dropped = 0;
        const CaveShotFlags skip = CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;
        foreach (var shot in model.Shots)
        {
            if ((shot.Flags & skip) != 0) continue;
            if (!byPosition.TryGetValue(shot.FromPosition, out int from)) { dropped++; continue; }
            if (!byPosition.TryGetValue(shot.ToPosition, out int to)) { dropped++; continue; }
            if (from == to) { dropped++; continue; }
            edges.Add((from, to));
            Union(parent, from, to);
        }

        // One entry per joined pair, lowest index first, so that a passage surveyed twice does
        // not read as two passages meeting.
        var seen = new HashSet<(int, int)>();
        var distinct = new List<(int From, int To)>();
        foreach (var (from, to) in edges)
        {
            var pair = from < to ? (from, to) : (to, from);
            if (seen.Add(pair)) distinct.Add(pair);
        }
        distinct.Sort(static (a, b) => a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));

        var neighbours = new Dictionary<int, List<int>>();
        foreach (var (from, to) in distinct)
        {
            (neighbours.TryGetValue(from, out var f) ? f : neighbours[from] = []).Add(to);
            (neighbours.TryGetValue(to, out var t) ? t : neighbours[to] = []).Add(from);
        }
        var adjacency = new Dictionary<int, int[]>(neighbours.Count);
        foreach (var (station, list) in neighbours)
        {
            list.Sort();
            adjacency[station] = [.. list];
        }

        // Group stations that participate in at least one structural leg by their root.
        var members = new Dictionary<int, HashSet<int>>();
        foreach (var (from, to) in distinct)
        {
            AddMember(members, parent, from);
            AddMember(members, parent, to);
        }

        var components = new List<CenterlineComponent>();
        foreach (var (_, set) in members.OrderBy(kv => kv.Value.Min()))
        {
            var indices = set.ToList();
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

        return new CenterlineGraph(stations, edges, distinct, adjacency, components, dropped, merged);
    }

    private static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);

    private static double NormalizeDegrees(double degrees)
    {
        double a = degrees % 360.0;
        return a < 0 ? a + 360.0 : a;
    }

    private static void AddMember(Dictionary<int, HashSet<int>> members, int[] parent, int node)
    {
        int root = Find(parent, node);
        if (!members.TryGetValue(root, out var set))
            members[root] = set = [];
        set.Add(node);
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
