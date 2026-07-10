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
