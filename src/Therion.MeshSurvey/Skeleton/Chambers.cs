// Chambers: where the void is far wider than the passages around it, any skeleton is a tangle of
// loops round boulders and spurs into every alcove. A chamber is found as the union of the largest
// balls that fit in it (core voxels whose wall distance passes the threshold, each with its ball);
// the passages entering it become portals, and the skeleton inside is replaced by the chosen policy:
// one hub, a line along its long axis, or a loop round its walls.

using Therion.Cave3D;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Skeleton;

/// <summary>A chamber found in the void.</summary>
public sealed record ChamberInfo(int Index, CaveVector3 Centroid, double Volume, double MaxRadius, int Portals, ChamberPolicy Policy);

internal static class Chambers
{
    public static List<ChamberInfo> Apply(SkeletonGraph g, VoidGrid grid, SkeletonOptions o, CancellationToken ct)
    {
        var result = new List<ChamberInfo>();
        if (o.ChamberPolicy == ChamberPolicy.Keep) return result;
        double median = LengthWeightedMedianRadius(g);
        double threshold = Math.Max(o.ChamberMinRadius, o.ChamberRatio * median);
        var label = Bodies(grid, threshold, out int count, ct);
        if (count == 0) return result;

        for (int c = 0; c < count; c++)
        {
            ct.ThrowIfCancellationRequested();
            var body = new List<int>();
            for (int i = 0; i < grid.Count; i++) if (label[i] == c) body.Add(i);
            if (body.Count == 0) continue;
            bool InBody(CaveVector3 p) { int id = grid.IdAt(p); return id >= 0 && label[id] == c; }

            // Cut every edge where it enters the body; drop what is inside.
            var portals = new List<SkeletonNode>();
            foreach (var e in g.LiveEdges.ToList())
            {
                var inside = e.Points.Select(InBody).ToArray();
                if (!inside.Any(x => x)) continue;
                g.RemoveEdge(e);
                // Runs of outside points become edges; each run end next to the body is a portal.
                int i = 0;
                while (i < inside.Length)
                {
                    if (inside[i]) { i++; continue; }
                    int start = i;
                    while (i < inside.Length && !inside[i]) i++;
                    int end = i - 1;
                    var pts = e.Points.GetRange(start, end - start + 1);
                    var radii = e.Radii.GetRange(start, end - start + 1);
                    var from = start == 0 ? e.A : Portal(g, pts[0], radii[0], c, portals);
                    var to = end == inside.Length - 1 ? e.B : Portal(g, pts[^1], radii[^1], c, portals);
                    if (from == to && pts.Count < 2) continue;
                    if (pts.Count == 1) { pts.Add(pts[0]); radii.Add(radii[0]); }
                    pts[0] = from.Position; pts[^1] = to.Position;
                    g.AddEdge(from, to, pts, radii);
                }
            }
            // Nodes left with nothing (they were inside) disappear.
            foreach (var n in g.Nodes)
                if (!n.Removed && n.Chamber != c && n.Edges.All(x => x.Removed) && InBody(n.Position) && !n.Protected) n.Removed = true;

            var centroid = CaveVector3.Zero;
            double maxR = 0;
            foreach (int id in body) { centroid += grid.Center(id); maxR = Math.Max(maxR, grid.Distance[id]); }
            centroid /= body.Count;
            var policy = o.ChamberPolicy;
            switch (policy)
            {
                case ChamberPolicy.Axis: Axis(g, grid, body, centroid, portals, c); break;
                case ChamberPolicy.Perimeter: Perimeter(g, grid, body, label, c, centroid, portals, o.PerimeterOffset); break;
                default: Hub(g, grid, body, label, c, centroid, portals); break;
            }
            // Protected nodes inside (an entrance, a guide station) join the chamber's structure.
            foreach (var n in g.Nodes.Where(n => !n.Removed && n.Protected && n.Edges.All(x => x.Removed) && InBody(n.Position)).ToList())
                Link(g, grid, label, c, n, NearestChamberNode(g, n.Position, c) ?? n);
            result.Add(new ChamberInfo(c, centroid, body.Count * Math.Pow(grid.VoxelSize, 3), maxR, portals.Count, policy));
        }
        SkeletonCleaner.DissolveDegreeTwo(g);
        return result;
    }

    private static SkeletonNode Portal(SkeletonGraph g, CaveVector3 p, double r, int chamber, List<SkeletonNode> portals)
    {
        foreach (var existing in portals)
            if ((existing.Position - p).Length < Math.Max(0.5, existing.Radius)) return existing;
        var n = g.AddNode(p, r, SkeletonNodeKind.Portal);
        n.Chamber = chamber;
        portals.Add(n);
        return n;
    }

    private static double LengthWeightedMedianRadius(SkeletonGraph g)
    {
        var samples = new List<(double R, double W)>();
        foreach (var e in g.LiveEdges)
            for (int i = 1; i < e.Points.Count; i++)
                samples.Add((0.5 * (e.Radii[i] + e.Radii[i - 1]), (e.Points[i] - e.Points[i - 1]).Length));
        if (samples.Count == 0) return 0;
        samples.Sort((a, b) => a.R.CompareTo(b.R));
        double total = samples.Sum(s => s.W), acc = 0;
        foreach (var (r, w) in samples) { acc += w; if (acc >= total / 2) return r; }
        return samples[^1].R;
    }

    /// <summary>Chamber label per voxel (-1 = none): core voxels (radius ≥ threshold) grouped by
    /// 26-adjacency, each grown by its own ball.</summary>
    private static int[] Bodies(VoidGrid grid, double threshold, out int count, CancellationToken ct)
    {
        var label = new int[grid.Count];
        Array.Fill(label, -1);
        count = 0;
        Span<int> n26 = stackalloc int[26];
        var core = new List<int>();
        for (int i = 0; i < grid.Count; i++) if (grid.Distance[i] >= threshold) core.Add(i);
        if (core.Count == 0) return label;
        var coreLabel = new int[grid.Count];
        Array.Fill(coreLabel, -1);
        foreach (int s in core)
        {
            if (coreLabel[s] >= 0) continue;
            int c = count++;
            var stack = new Stack<int>();
            coreLabel[s] = c;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                grid.Neighbours26(v, n26);
                foreach (int k in n26)
                    if (k >= 0 && coreLabel[k] < 0 && grid.Distance[k] >= threshold) { coreLabel[k] = c; stack.Push(k); }
            }
        }
        // Grow: a voxel joins when it lies in the ball of the core voxel it was reached from.
        var source = new int[grid.Count];
        Array.Fill(source, -1);
        var queue = new Queue<int>();
        foreach (int s in core) { label[s] = coreLabel[s]; source[s] = s; queue.Enqueue(s); }
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            int src = source[v];
            var centre = grid.Center(src);
            double r = grid.Distance[src];
            grid.Neighbours26(v, n26);
            foreach (int k in n26)
            {
                if (k < 0 || label[k] >= 0) continue;
                if ((grid.Center(k) - centre).Length > r) continue;
                label[k] = label[v];
                source[k] = src;
                queue.Enqueue(k);
            }
            ct.ThrowIfCancellationRequested();
        }
        return label;
    }

    private static int NearestBodyVoxel(VoidGrid grid, List<int> body, CaveVector3 p, bool preferRoomy)
    {
        int best = body[0];
        double bestScore = double.PositiveInfinity;
        foreach (int id in body)
        {
            double d = (grid.Center(id) - p).Length;
            double score = preferRoomy ? d - 0.5 * grid.Distance[id] : d;
            if (score < bestScore) { bestScore = score; best = id; }
        }
        return best;
    }

    private static void Hub(SkeletonGraph g, VoidGrid grid, List<int> body, int[] label, int c, CaveVector3 centroid, List<SkeletonNode> portals)
    {
        int hubVoxel = NearestBodyVoxel(grid, body, centroid, preferRoomy: true);
        var hub = g.AddNode(grid.Center(hubVoxel), grid.Distance[hubVoxel], SkeletonNodeKind.Chamber);
        hub.Chamber = c;
        foreach (var p in portals) Link(g, grid, label, c, hub, p);
    }

    private static void Axis(SkeletonGraph g, VoidGrid grid, List<int> body, CaveVector3 centroid, List<SkeletonNode> portals, int c)
    {
        // Principal axis by power iteration on the covariance.
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (int id in body)
        {
            var d = grid.Center(id) - centroid;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        var axis = new CaveVector3(1, 0.3, 0.1).Normalized();
        for (int it = 0; it < 50; it++)
            axis = new CaveVector3(xx * axis.X + xy * axis.Y + xz * axis.Z, xy * axis.X + yy * axis.Y + yz * axis.Z, xz * axis.X + yz * axis.Y + zz * axis.Z).Normalized();
        if (axis.LengthSquared == 0) axis = new CaveVector3(1, 0, 0);
        double tmin = double.PositiveInfinity, tmax = double.NegativeInfinity;
        foreach (int id in body)
        {
            double t = (grid.Center(id) - centroid).Dot(axis);
            if (t < tmin) tmin = t;
            if (t > tmax) tmax = t;
        }
        // Stations along the axis where it is in the void; ends pulled in by a metre.
        var axisNodes = new List<(double T, SkeletonNode Node)>();
        double margin = Math.Min(1.0, (tmax - tmin) / 4);
        foreach (double t in new[] { tmin + margin, tmax - margin })
        {
            int v = NearestBodyVoxel(grid, body, centroid + axis * t, preferRoomy: false);
            var n = g.AddNode(grid.Center(v), grid.Distance[v], SkeletonNodeKind.Chamber);
            n.Chamber = c;
            axisNodes.Add(((grid.Center(v) - centroid).Dot(axis), n));
        }
        foreach (var p in portals)
        {
            double t = Math.Clamp((p.Position - centroid).Dot(axis), tmin + margin, tmax - margin);
            int v = NearestBodyVoxel(grid, body, centroid + axis * t, preferRoomy: false);
            var near = axisNodes.FirstOrDefault(a => (a.Node.Position - grid.Center(v)).Length < Math.Max(1.0, 2 * grid.VoxelSize));
            var n = near.Node;
            if (n is null)
            {
                n = g.AddNode(grid.Center(v), grid.Distance[v], SkeletonNodeKind.Chamber);
                n.Chamber = c;
                axisNodes.Add(((grid.Center(v) - centroid).Dot(axis), n));
            }
            Link(g, grid, null, c, n, p);
        }
        axisNodes.Sort((a, b) => a.T.CompareTo(b.T));
        for (int i = 1; i < axisNodes.Count; i++)
            Link(g, grid, null, c, axisNodes[i - 1].Node, axisNodes[i].Node);
    }

    private static void Perimeter(SkeletonGraph g, VoidGrid grid, List<int> body, int[] label, int c, CaveVector3 centroid, List<SkeletonNode> portals, double offset)
    {
        int hubVoxel = NearestBodyVoxel(grid, body, centroid, preferRoomy: true);
        var hub = grid.Center(hubVoxel);
        const int directions = 24;
        var ring = new List<CaveVector3>();
        for (int k = 0; k < directions; k++)
        {
            double a = 2 * Math.PI * k / directions;
            var dir = new CaveVector3(Math.Cos(a), Math.Sin(a), 0);
            var last = hub;
            for (double s = grid.VoxelSize; s < 1000; s += grid.VoxelSize)
            {
                var p = hub + dir * s;
                int id = grid.IdAt(p);
                if (id < 0 || label[id] != c || grid.Distance[id] < offset) break;
                last = p;
            }
            ring.Add(last);
        }
        // A node at the ring point nearest each portal (or one node when there is none).
        var anchors = portals.Count == 0 ? [0] : portals.Select(p => Enumerable.Range(0, directions).OrderBy(k => (ring[k] - p.Position).Length).First()).Distinct().Order().ToList();
        var nodes = new Dictionary<int, SkeletonNode>();
        foreach (int k in anchors)
        {
            var n = g.AddNode(ring[k], grid.DistanceAt(ring[k]), SkeletonNodeKind.Chamber);
            n.Chamber = c;
            nodes[k] = n;
        }
        for (int i = 0; i < anchors.Count; i++)
        {
            int from = anchors[i], to = anchors[(i + 1) % anchors.Count];
            var pts = new List<CaveVector3>();
            int k = from;
            do { pts.Add(ring[k]); k = (k + 1) % directions; } while (k != to);
            pts.Add(ring[to]);
            if (anchors.Count == 1) { /* full circle back to itself */ }
            g.AddEdge(nodes[from], nodes[to], pts, pts.Select(grid.DistanceAt).ToList(), synthetic: true);
        }
        foreach (var p in portals)
        {
            int k = Enumerable.Range(0, directions).OrderBy(j => (ring[j] - p.Position).Length).First();
            Link(g, grid, label, c, nodes[k], p);
        }
    }

    private static SkeletonNode? NearestChamberNode(SkeletonGraph g, CaveVector3 p, int c) =>
        g.LiveNodes.Where(n => n.Chamber == c && n.Kind == SkeletonNodeKind.Chamber).OrderBy(n => (n.Position - p).Length).FirstOrDefault();

    /// <summary>Joins two nodes inside a chamber: straight when the void allows, else along the
    /// shortest voxel path through the void.</summary>
    private static void Link(SkeletonGraph g, VoidGrid grid, int[]? label, int c, SkeletonNode a, SkeletonNode b)
    {
        if (a == b) return;
        if (grid.SegmentClear(a.Position, b.Position, 0.5 * grid.VoxelSize, 0, 0))
        {
            g.AddEdge(a, b, [a.Position, b.Position], [a.Radius, b.Radius], synthetic: true);
            return;
        }
        var path = VoxelPath(grid, a.Position, b.Position);
        if (path.Count == 0)
        {
            g.AddEdge(a, b, [a.Position, b.Position], [a.Radius, b.Radius], synthetic: true);
            return;
        }
        var pts = new List<CaveVector3> { a.Position };
        pts.AddRange(path.Select(grid.Center));
        pts.Add(b.Position);
        g.AddEdge(a, b, pts, pts.Select(p => Math.Max(grid.DistanceAt(p), 0.5 * grid.VoxelSize)).ToList(), synthetic: true);
        _ = label; _ = c;
    }

    /// <summary>Shortest 26-connected path through the void, preferring roomy voxels (cost grows as the
    /// wall gets near), from the voxel nearest a to the voxel nearest b.</summary>
    internal static List<int> VoxelPath(VoidGrid grid, CaveVector3 a, CaveVector3 b)
    {
        int s = grid.NearestVoid(a, 8), t = grid.NearestVoid(b, 8);
        if (s < 0 || t < 0) return [];
        var dist = new Dictionary<int, double> { [s] = 0 };
        var prev = new Dictionary<int, int>();
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(s, 0);
        Span<int> n26 = stackalloc int[26];
        var target = grid.Center(t);
        while (queue.TryDequeue(out int v, out double pri))
        {
            if (v == t) break;
            double dv = dist[v];
            if (pri - (grid.Center(v) - target).Length > dv + 1e-9) continue;
            grid.Neighbours26(v, n26);
            foreach (int k in n26)
            {
                if (k < 0) continue;
                double step = (grid.Center(k) - grid.Center(v)).Length * (1 + 1.0 / Math.Max(0.05, grid.Distance[k]));
                double nd = dv + step;
                if (dist.TryGetValue(k, out double old) && old <= nd) continue;
                dist[k] = nd;
                prev[k] = v;
                queue.Enqueue(k, nd + (grid.Center(k) - target).Length);
            }
        }
        if (!prev.ContainsKey(t) && s != t) return [];
        var path = new List<int>();
        for (int v = t; v != s; v = prev[v]) path.Add(v);
        path.Add(s);
        path.Reverse();
        return path;
    }
}
