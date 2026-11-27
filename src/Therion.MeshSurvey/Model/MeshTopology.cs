// Edge-level facts about a welded triangle mesh: consistent winding, connected parts, open
// boundary loops (scan holes and entrances). Pure functions over index lists.

using Therion.Cave3D;

namespace Therion.MeshSurvey.Model;

/// <summary>A closed loop of boundary edges: the rim of a hole in the mesh.</summary>
/// <param name="Vertices">The loop's vertex indices in order.</param>
/// <param name="Centroid">Mean of the loop's vertices.</param>
/// <param name="Area">Area of the loop's fan from its centroid (the hole's size).</param>
/// <param name="Perimeter">Length of the loop.</param>
public sealed record BoundaryLoop(IReadOnlyList<int> Vertices, CaveVector3 Centroid, double Area, double Perimeter);

/// <summary>Result of <see cref="MeshTopology.OrientConsistently"/>.</summary>
public sealed record OrientationResult(List<CaveTriangle> Triangles, int Flipped, int NonManifoldEdges);

public static class MeshTopology
{
    private static long EdgeKey(uint a, uint b) => a < b ? ((long)a << 32) | b : ((long)b << 32) | a;

    /// <summary>
    /// Flips triangles so that neighbours across every manifold edge traverse it in opposite
    /// directions. Each connected part keeps the winding of its first triangle; a part whose winding
    /// cannot be made consistent (a Möbius strip) keeps what the walk gives. Non-manifold edges (three
    /// or more triangles) are not crossed.
    /// </summary>
    public static OrientationResult OrientConsistently(IReadOnlyList<CaveVector3> vertices, IReadOnlyList<CaveTriangle> triangles)
    {
        var tris = triangles.ToArray();
        var edges = new Dictionary<long, List<int>>(tris.Length * 2, Spatial.MixedLongComparer.Instance);
        for (int t = 0; t < tris.Length; t++)
        {
            foreach (var (a, b) in Edges(tris[t]))
            {
                long key = EdgeKey(a, b);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(t);
            }
        }
        int nonManifold = edges.Values.Count(l => l.Count > 2);
        var visited = new bool[tris.Length];
        int flipped = 0;
        var queue = new Queue<int>();
        for (int seed = 0; seed < tris.Length; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                foreach (var (a, b) in Edges(tris[t]))
                {
                    var list = edges[EdgeKey(a, b)];
                    if (list.Count != 2) continue;
                    int n = list[0] == t ? list[1] : list[0];
                    if (visited[n]) continue;
                    visited[n] = true;
                    // Consistent: the neighbour runs the shared edge b→a.
                    if (HasDirectedEdge(tris[n], a, b))
                    {
                        var x = tris[n];
                        tris[n] = new CaveTriangle(x.A, x.C, x.B);
                        flipped++;
                    }
                    queue.Enqueue(n);
                }
            }
        }
        return new OrientationResult(tris.ToList(), flipped, nonManifold);
    }

    private static bool HasDirectedEdge(CaveTriangle t, uint a, uint b) =>
        (t.A == a && t.B == b) || (t.B == a && t.C == b) || (t.C == a && t.A == b);

    private static IEnumerable<(uint, uint)> Edges(CaveTriangle t)
    {
        yield return (t.A, t.B);
        yield return (t.B, t.C);
        yield return (t.C, t.A);
    }

    /// <summary>Edges used by exactly one triangle.</summary>
    public static int CountBoundaryEdges(IReadOnlyList<CaveTriangle> triangles)
    {
        var count = new Dictionary<long, int>(triangles.Count * 2, Spatial.MixedLongComparer.Instance);
        foreach (var t in triangles)
            foreach (var (a, b) in Edges(t))
                count[EdgeKey(a, b)] = count.GetValueOrDefault(EdgeKey(a, b)) + 1;
        return count.Values.Count(c => c == 1);
    }

    /// <summary>The connected part (through shared vertices) of every triangle.</summary>
    public static int[] TriangleComponents(int vertexCount, IReadOnlyList<CaveTriangle> triangles, out int count)
    {
        var parent = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) { if (a < b) parent[b] = a; else parent[a] = b; } }
        foreach (var t in triangles) { Union((int)t.A, (int)t.B); Union((int)t.B, (int)t.C); }
        var label = new Dictionary<int, int>();
        var result = new int[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            int root = Find((int)triangles[i].A);
            if (!label.TryGetValue(root, out int k)) label[root] = k = label.Count;
            result[i] = k;
        }
        count = label.Count;
        return result;
    }

    /// <summary>The loops of boundary edges, largest area first. Boundary edges are walked in their
    /// triangle's direction; a vertex where several loops touch is resolved by taking the first
    /// unused outgoing edge, so the loops stay deterministic.</summary>
    public static IReadOnlyList<BoundaryLoop> BoundaryLoops(IReadOnlyList<CaveVector3> vertices, IReadOnlyList<CaveTriangle> triangles)
    {
        var count = new Dictionary<long, int>(triangles.Count * 2, Spatial.MixedLongComparer.Instance);
        foreach (var t in triangles)
            foreach (var (a, b) in Edges(t))
                count[EdgeKey(a, b)] = count.GetValueOrDefault(EdgeKey(a, b)) + 1;
        var next = new SortedDictionary<uint, List<uint>>();
        foreach (var t in triangles)
            foreach (var (a, b) in Edges(t))
                if (count[EdgeKey(a, b)] == 1)
                {
                    if (!next.TryGetValue(a, out var list)) next[a] = list = [];
                    list.Add(b);
                }
        var loops = new List<BoundaryLoop>();
        foreach (var start in next.Keys.ToList())
        {
            while (next.TryGetValue(start, out var outs) && outs.Count > 0)
            {
                var loop = new List<int> { (int)start };
                uint current = start;
                int guard = 0;
                while (guard++ < 10_000_000)
                {
                    if (!next.TryGetValue(current, out var o) || o.Count == 0) break;
                    uint n = o[0];
                    o.RemoveAt(0);
                    if (n == start) break;
                    loop.Add((int)n);
                    current = n;
                }
                if (loop.Count >= 3) loops.Add(Describe(vertices, loop));
            }
        }
        return loops.OrderByDescending(l => l.Area).ThenBy(l => l.Vertices[0]).ToList();
    }

    private static BoundaryLoop Describe(IReadOnlyList<CaveVector3> vertices, List<int> loop)
    {
        var c = CaveVector3.Zero;
        foreach (int i in loop) c += vertices[i];
        c /= loop.Count;
        double area = 0, perimeter = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = vertices[loop[i]];
            var b = vertices[loop[(i + 1) % loop.Count]];
            area += 0.5 * (a - c).Cross(b - c).Length;
            perimeter += (b - a).Length;
        }
        return new BoundaryLoop(loop, c, area, perimeter);
    }
}
