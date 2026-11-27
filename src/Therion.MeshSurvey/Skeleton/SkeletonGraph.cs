// The skeleton as a graph: nodes where passages end or meet, edges along the passages between them,
// each edge carrying its polyline and the passage radius along it. Built from the thinned voxels
// (junction voxels clustered into one node), then cleaned: spurs from wall bumps pruned, junctions a
// few decimetres apart merged, degree-two nodes dissolved. Mutable while it is being cleaned; the
// planner reads it afterwards.

using Therion.Cave3D;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Skeleton;

public enum SkeletonNodeKind
{
    /// <summary>A passage ends here.</summary>
    End,
    /// <summary>Three or more passages meet.</summary>
    Junction,
    /// <summary>A loop with no junction on it needs one node to hang on.</summary>
    Ring,
    /// <summary>The middle of a chamber (chamber policies).</summary>
    Chamber,
    /// <summary>Where a passage enters a chamber.</summary>
    Portal,
    /// <summary>A node made to hold a user's station.</summary>
    Guide,
}

public sealed class SkeletonNode
{
    public int Id { get; internal set; }
    public CaveVector3 Position { get; internal set; }
    public double Radius { get; internal set; }
    public SkeletonNodeKind Kind { get; internal set; }
    public List<SkeletonEdge> Edges { get; } = [];
    internal bool Removed { get; set; }

    /// <summary>Never pruned or merged away (entrance, guide station).</summary>
    public bool Protected { get; internal set; }

    /// <summary>The chamber this node belongs to (chamber and portal nodes), or -1.</summary>
    public int Chamber { get; internal set; } = -1;

    /// <summary>An end at a hole in the model: the passage goes on beyond the scan.</summary>
    public bool OpenEnd { get; internal set; }

    public int Degree => Edges.Count(e => !e.Removed) + Edges.Count(e => !e.Removed && e.A == e.B);

    public override string ToString() => $"{Kind} {Id} @ {Position} r={Radius:0.00}";
}

public sealed class SkeletonEdge
{
    public int Id { get; internal set; }
    public SkeletonNode A { get; internal set; } = null!;
    public SkeletonNode B { get; internal set; } = null!;

    /// <summary>The axis from A to B (internal frame), first and last points at the nodes.</summary>
    public List<CaveVector3> Points { get; internal set; } = [];

    /// <summary>Passage radius at each point.</summary>
    public List<double> Radii { get; internal set; } = [];

    internal bool Removed { get; set; }

    /// <summary>A straight link made by a chamber policy (no passage axis behind it).</summary>
    public bool Synthetic { get; internal set; }

    public double Length
    {
        get
        {
            double l = 0;
            for (int i = 1; i < Points.Count; i++) l += (Points[i] - Points[i - 1]).Length;
            return l;
        }
    }

    public SkeletonNode Other(SkeletonNode n) => n == A ? B : A;

    public double MedianRadius
    {
        get
        {
            if (Radii.Count == 0) return 0;
            var sorted = Radii.Order().ToList();
            return sorted[sorted.Count / 2];
        }
    }

    public override string ToString() => $"edge {Id}: {A.Id}–{B.Id}, {Length:0.0} m";
}

public sealed class SkeletonGraph
{
    public List<SkeletonNode> Nodes { get; } = [];
    public List<SkeletonEdge> Edges { get; } = [];

    public IEnumerable<SkeletonNode> LiveNodes => Nodes.Where(n => !n.Removed);
    public IEnumerable<SkeletonEdge> LiveEdges => Edges.Where(e => !e.Removed);

    /// <summary>Independent loops: E − V + components.</summary>
    public int CycleRank
    {
        get
        {
            var nodes = LiveNodes.ToList();
            var edges = LiveEdges.ToList();
            return edges.Count - nodes.Count + Components().Count;
        }
    }

    public double TotalLength => LiveEdges.Sum(e => e.Length);

    internal SkeletonNode AddNode(CaveVector3 position, double radius, SkeletonNodeKind kind)
    {
        var n = new SkeletonNode { Id = Nodes.Count, Position = position, Radius = radius, Kind = kind };
        Nodes.Add(n);
        return n;
    }

    internal SkeletonEdge AddEdge(SkeletonNode a, SkeletonNode b, List<CaveVector3> points, List<double> radii, bool synthetic = false)
    {
        var e = new SkeletonEdge { Id = Edges.Count, A = a, B = b, Points = points, Radii = radii, Synthetic = synthetic };
        Edges.Add(e);
        a.Edges.Add(e);
        if (b != a) b.Edges.Add(e);
        return e;
    }

    internal void RemoveEdge(SkeletonEdge e)
    {
        e.Removed = true;
        e.A.Edges.Remove(e);
        e.B.Edges.Remove(e);
    }

    /// <summary>Connected parts, each a list of live nodes (in id order).</summary>
    public List<List<SkeletonNode>> Components()
    {
        var seen = new HashSet<SkeletonNode>();
        var result = new List<List<SkeletonNode>>();
        foreach (var start in LiveNodes)
        {
            if (!seen.Add(start)) continue;
            var comp = new List<SkeletonNode>();
            var stack = new Stack<SkeletonNode>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                comp.Add(n);
                foreach (var e in n.Edges)
                {
                    if (e.Removed) continue;
                    var o = e.Other(n);
                    if (seen.Add(o)) stack.Push(o);
                }
            }
            comp.Sort((x, y) => x.Id.CompareTo(y.Id));
            result.Add(comp);
        }
        return result;
    }

    // ---------------- building from voxels ----------------

    internal static SkeletonGraph FromVoxels(VoidGrid grid, bool[] alive)
    {
        var g = new SkeletonGraph();
        int n = grid.Count;
        var degree = new int[n];
        Span<int> n26 = stackalloc int[26];
        for (int id = 0; id < n; id++)
        {
            if (!alive[id]) continue;
            grid.Neighbours26(id, n26);
            int d = 0;
            foreach (int k in n26) if (k >= 0 && alive[k]) d++;
            degree[id] = d;
        }

        // Junction clusters: 26-connected sets of voxels with three or more skeleton neighbours.
        var cluster = new int[n];
        Array.Fill(cluster, -1);
        var clusterVoxels = new List<List<int>>();
        for (int id = 0; id < n; id++)
        {
            if (!alive[id] || degree[id] < 3 || cluster[id] >= 0) continue;
            int c = clusterVoxels.Count;
            var members = new List<int>();
            var stack = new Stack<int>();
            cluster[id] = c;
            stack.Push(id);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                members.Add(v);
                grid.Neighbours26(v, n26);
                foreach (int k in n26)
                    if (k >= 0 && alive[k] && degree[k] >= 3 && cluster[k] < 0) { cluster[k] = c; stack.Push(k); }
            }
            members.Sort();
            clusterVoxels.Add(members);
        }

        // A node per cluster and per curve end (or isolated voxel).
        var nodeOfCluster = new SkeletonNode[clusterVoxels.Count];
        for (int c = 0; c < clusterVoxels.Count; c++)
        {
            var members = clusterVoxels[c];
            var sum = CaveVector3.Zero;
            double r = 0;
            foreach (int v in members) { sum += grid.Center(v); r = Math.Max(r, grid.Distance[v]); }
            nodeOfCluster[c] = g.AddNode(sum / members.Count, r, SkeletonNodeKind.Junction);
        }
        var nodeOfEnd = new Dictionary<int, SkeletonNode>();
        for (int id = 0; id < n; id++)
            if (alive[id] && degree[id] <= 1)
                nodeOfEnd[id] = g.AddNode(grid.Center(id), grid.Distance[id], SkeletonNodeKind.End);

        SkeletonNode? NodeAt(int v) =>
            cluster[v] >= 0 ? nodeOfCluster[cluster[v]] : nodeOfEnd.TryGetValue(v, out var e) ? e : null;

        // Trace edges from every node voxel along runs of degree-2 voxels. A degree-2 voxel has exactly
        // two skeleton neighbours (thinning left no redundant ones), so each run is unambiguous; a run
        // is marked as it is walked, so its other end does not trace it again.
        var used = new bool[n];
        Span<int> nb = stackalloc int[26];
        void AddPath(SkeletonNode from, List<int> path, SkeletonNode to)
        {
            var pts = new List<CaveVector3> { from.Position };
            var radii = new List<double> { from.Radius };
            foreach (int v in path) { pts.Add(grid.Center(v)); radii.Add(grid.Distance[v]); }
            pts.Add(to.Position);
            radii.Add(to.Radius);
            g.AddEdge(from, to, pts, radii);
        }
        var direct = new HashSet<(int, int)>();
        for (int v = 0; v < n; v++)
        {
            if (!alive[v]) continue;
            var startNode = NodeAt(v);
            if (startNode is null) continue;
            grid.Neighbours26(v, n26);
            for (int i = 0; i < 26; i++)
            {
                int k = n26[i];
                if (k < 0 || !alive[k]) continue;
                if (cluster[v] >= 0 && cluster[k] == cluster[v]) continue;
                if (NodeAt(k) is { } other)
                {
                    // Two node voxels side by side: one edge, added once.
                    var key = (Math.Min(startNode.Id, other.Id), Math.Max(startNode.Id, other.Id));
                    if (direct.Add(key)) AddPath(startNode, [], other);
                    continue;
                }
                if (used[k]) continue;
                var path = new List<int>();
                int prev = v, cur = k;
                SkeletonNode? end = null;
                while (true)
                {
                    used[cur] = true;
                    path.Add(cur);
                    grid.Neighbours26(cur, nb);
                    int next = -1;
                    foreach (int j in nb)
                    {
                        if (j < 0 || !alive[j] || j == prev) continue;
                        if (cluster[v] >= 0 && cluster[j] == cluster[v] && path.Count == 1) continue; // still at the start cluster
                        next = j;
                        break;
                    }
                    if (next < 0) break;
                    if (NodeAt(next) is { } reached) { end = reached; break; }
                    if (used[next]) break;
                    prev = cur;
                    cur = next;
                }
                if (end is not null) AddPath(startNode, path, end);
            }
        }

        // Rings: loops of degree-2 voxels with no node on them.
        for (int id = 0; id < n; id++)
        {
            if (!alive[id] || used[id] || degree[id] != 2 || cluster[id] >= 0) continue;
            var ring = new List<int>();
            int prev = -1, cur = id;
            while (cur >= 0 && !used[cur])
            {
                used[cur] = true;
                ring.Add(cur);
                grid.Neighbours26(cur, n26);
                int next = -1;
                foreach (int k in n26) if (k >= 0 && alive[k] && k != prev && !used[k]) { next = k; break; }
                prev = cur;
                cur = next;
            }
            if (ring.Count < 3) continue;
            var node = g.AddNode(grid.Center(ring[0]), grid.Distance[ring[0]], SkeletonNodeKind.Ring);
            var pts = ring.Select(grid.Center).ToList();
            pts.Add(grid.Center(ring[0]));
            var radii = ring.Select(v => (double)grid.Distance[v]).ToList();
            radii.Add(grid.Distance[ring[0]]);
            g.AddEdge(node, node, pts, radii);
        }

        return g;
    }
}
