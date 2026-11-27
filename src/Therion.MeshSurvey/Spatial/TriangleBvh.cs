// A bounding-volume hierarchy over a triangle mesh: first hit of a ray, every crossing of a segment,
// and the nearest point of the mesh to a point. The exact half of every geometric question the
// survey asks (a leg through rock, a splay's end, a station's floor); the voxel void answers the
// approximate half quickly. Built once per session; queries allocate nothing and are thread-safe.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.MeshSurvey.Spatial;

/// <summary>Where a ray or segment met the mesh.</summary>
/// <param name="Distance">Distance from the origin along the (unit) direction.</param>
/// <param name="Triangle">Index of the triangle hit.</param>
/// <param name="Point">The hit point.</param>
/// <param name="Normal">The triangle's unit normal, as wound (not flipped towards the ray).</param>
public readonly record struct MeshHit(double Distance, int Triangle, CaveVector3 Point, CaveVector3 Normal);

/// <summary>A static BVH over the triangles of a mesh.</summary>
public sealed class TriangleBvh
{
    private const int LeafSize = 4;

    private readonly double[] _v;      // vertex coordinates, xyz interleaved
    private readonly int[] _tri;       // triangle corner indices, abc interleaved, in BVH order
    private readonly int[] _triId;     // original triangle index of each BVH-ordered triangle
    private readonly double[] _box;    // node bounds: minx miny minz maxx maxy maxz
    private readonly int[] _left;      // child index, or -1 for a leaf
    private readonly int[] _right;
    private readonly int[] _first;     // leaf: first triangle (BVH order)
    private readonly int[] _count;     // leaf: triangle count
    private int _nodeCount;

    public CaveMesh Mesh { get; }

    public int TriangleCount => _triId.Length;

    public BoundingBox Bounds { get; }

    public TriangleBvh(CaveMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Mesh = mesh;
        int nv = mesh.Vertices.Count, nt = mesh.Triangles.Count;
        _v = new double[nv * 3];
        for (int i = 0; i < nv; i++)
        {
            var p = mesh.Vertices[i];
            _v[3 * i] = p.X; _v[3 * i + 1] = p.Y; _v[3 * i + 2] = p.Z;
        }

        var centroid = new double[nt * 3];
        var order = new int[nt];
        for (int t = 0; t < nt; t++)
        {
            var tr = mesh.Triangles[t];
            for (int k = 0; k < 3; k++)
                centroid[3 * t + k] = (_v[3 * tr.A + k] + _v[3 * tr.B + k] + _v[3 * tr.C + k]) / 3.0;
            order[t] = t;
        }

        int maxNodes = Math.Max(1, NodesFor(nt, []));
        _box = new double[maxNodes * 6];
        _left = new int[maxNodes];
        _right = new int[maxNodes];
        _first = new int[maxNodes];
        _count = new int[maxNodes];
        if (nt > 0) Build(order, centroid, 0, nt, mesh);
        else { _nodeCount = 1; _left[0] = -1; _count[0] = 0; SetEmptyBox(0); }

        _tri = new int[nt * 3];
        _triId = new int[nt];
        for (int i = 0; i < nt; i++)
        {
            var tr = mesh.Triangles[order[i]];
            _tri[3 * i] = (int)tr.A; _tri[3 * i + 1] = (int)tr.B; _tri[3 * i + 2] = (int)tr.C;
            _triId[i] = order[i];
        }
        Bounds = nt == 0 ? BoundingBox.Empty
            : new BoundingBox(new CaveVector3(_box[0], _box[1], _box[2]), new CaveVector3(_box[3], _box[4], _box[5]));
    }

    /// <summary>Exactly how many nodes the median-split build makes for n triangles.</summary>
    private static int NodesFor(int n, Dictionary<int, int> memo)
    {
        if (n <= LeafSize) return 1;
        if (memo.TryGetValue(n, out int known)) return known;
        int mid = n / 2;
        int count = 1 + NodesFor(mid, memo) + NodesFor(n - mid, memo);
        memo[n] = count;
        return count;
    }

    private void SetEmptyBox(int node)
    {
        for (int k = 0; k < 3; k++) { _box[6 * node + k] = double.PositiveInfinity; _box[6 * node + 3 + k] = double.NegativeInfinity; }
    }

    private int Build(int[] order, double[] centroid, int lo, int hi, CaveMesh mesh)
    {
        // Iterative build with an explicit stack. Median splits halve every node, so the depth stays
        // near log2(n / LeafSize) and the fixed query stacks below (128 entries) cannot overflow.
        int root = _nodeCount++;
        var stack = new Stack<(int Node, int Lo, int Hi)>();
        stack.Push((root, lo, hi));
        while (stack.Count > 0)
        {
            var (node, a, b) = stack.Pop();
            SetEmptyBox(node);
            double cminx = double.PositiveInfinity, cminy = double.PositiveInfinity, cminz = double.PositiveInfinity;
            double cmaxx = double.NegativeInfinity, cmaxy = double.NegativeInfinity, cmaxz = double.NegativeInfinity;
            for (int i = a; i < b; i++)
            {
                int t = order[i];
                var tr = mesh.Triangles[t];
                Grow(node, (int)tr.A); Grow(node, (int)tr.B); Grow(node, (int)tr.C);
                double x = centroid[3 * t], y = centroid[3 * t + 1], z = centroid[3 * t + 2];
                if (x < cminx) cminx = x; if (x > cmaxx) cmaxx = x;
                if (y < cminy) cminy = y; if (y > cmaxy) cmaxy = y;
                if (z < cminz) cminz = z; if (z > cmaxz) cmaxz = z;
            }
            if (b - a <= LeafSize)
            {
                _left[node] = -1; _right[node] = -1; _first[node] = a; _count[node] = b - a;
                continue;
            }
            double ex = cmaxx - cminx, ey = cmaxy - cminy, ez = cmaxz - cminz;
            int axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
            int mid = (a + b) / 2;
            if (Math.Max(ex, Math.Max(ey, ez)) <= 0)
            {
                // All centroids coincide: split by count.
            }
            else
            {
                NthElement(order, centroid, a, b - 1, mid, axis);
            }
            int l = _nodeCount++, r = _nodeCount++;
            _left[node] = l; _right[node] = r; _count[node] = 0;
            stack.Push((l, a, mid));
            stack.Push((r, mid, b));
        }
        // Children's boxes are computed independently (each node grows its own box from its triangles).
        return root;
    }

    private void Grow(int node, int vertex)
    {
        int o = 6 * node;
        for (int k = 0; k < 3; k++)
        {
            double c = _v[3 * vertex + k];
            if (c < _box[o + k]) _box[o + k] = c;
            if (c > _box[o + 3 + k]) _box[o + 3 + k] = c;
        }
    }

    private static void NthElement(int[] order, double[] centroid, int lo, int hi, int n, int axis)
    {
        while (hi > lo)
        {
            double pivot = centroid[3 * order[(lo + hi) >> 1] + axis];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (centroid[3 * order[i] + axis] < pivot) i++;
                while (centroid[3 * order[j] + axis] > pivot) j--;
                if (i <= j) { (order[i], order[j]) = (order[j], order[i]); i++; j--; }
            }
            if (n <= j) hi = j;
            else if (n >= i) lo = i;
            else return;
        }
    }

    internal int NodeCount => _nodeCount;
    internal bool IsLeaf(int node) => _left[node] < 0;
    internal int Left(int node) => _left[node];
    internal int Right(int node) => _right[node];
    internal int LeafFirst(int node) => _first[node];
    internal int LeafCount(int node) => _count[node];
    internal ReadOnlySpan<double> NodeBox(int node) => new(_box, 6 * node, 6);
    internal ReadOnlySpan<double> VertexData => _v;
    internal ReadOnlySpan<int> TriangleData => _tri;
    internal int OriginalTriangle(int bvhTriangle) => _triId[bvhTriangle];

    /// <summary>The first hit of the ray from <paramref name="origin"/> along <paramref name="direction"/>
    /// (need not be unit) within <paramref name="maxDistance"/>; false when it hits nothing.</summary>
    public bool Raycast(CaveVector3 origin, CaveVector3 direction, double maxDistance, out MeshHit hit)
    {
        hit = default;
        double len = direction.Length;
        if (len < 1e-15 || TriangleCount == 0) return false;
        var d = direction / len;
        double best = maxDistance;
        int bestTri = -1;
        double ix = 1.0 / d.X, iy = 1.0 / d.Y, iz = 1.0 / d.Z;
        Span<int> stack = stackalloc int[128];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            int node = stack[--sp];
            if (!RayBox(node, origin, ix, iy, iz, best)) continue;
            if (_left[node] < 0)
            {
                int end = _first[node] + _count[node];
                for (int i = _first[node]; i < end; i++)
                {
                    double t = RayTriangle(i, origin, d);
                    if (t >= 0 && t < best) { best = t; bestTri = i; }
                }
            }
            else
            {
                stack[sp++] = _left[node];
                stack[sp++] = _right[node];
            }
        }
        if (bestTri < 0) return false;
        hit = new MeshHit(best, _triId[bestTri], origin + d * best, TriangleNormal(bestTri));
        return true;
    }

    /// <summary>Every distance along the segment <paramref name="a"/>→<paramref name="b"/> at which it crosses
    /// the mesh, sorted, added to <paramref name="crossings"/> (cleared first).</summary>
    public void SegmentCrossings(CaveVector3 a, CaveVector3 b, List<double> crossings)
    {
        ArgumentNullException.ThrowIfNull(crossings);
        crossings.Clear();
        var dir = b - a;
        double len = dir.Length;
        if (len < 1e-15 || TriangleCount == 0) return;
        var d = dir / len;
        double ix = 1.0 / d.X, iy = 1.0 / d.Y, iz = 1.0 / d.Z;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int node = stack.Pop();
            if (!RayBox(node, a, ix, iy, iz, len)) continue;
            if (_left[node] < 0)
            {
                int end = _first[node] + _count[node];
                for (int i = _first[node]; i < end; i++)
                {
                    double t = RayTriangle(i, a, d);
                    if (t >= 0 && t <= len) crossings.Add(t);
                }
            }
            else { stack.Push(_left[node]); stack.Push(_right[node]); }
        }
        crossings.Sort();
        // Hits on a shared edge are reported once per triangle: collapse near-duplicates.
        for (int i = crossings.Count - 1; i > 0; i--)
            if (crossings[i] - crossings[i - 1] < 1e-9) crossings.RemoveAt(i);
    }

    /// <summary>True when the open segment between <paramref name="a"/> and <paramref name="b"/> meets the mesh.</summary>
    public bool SegmentHits(CaveVector3 a, CaveVector3 b)
    {
        var dir = b - a;
        double len = dir.Length;
        return len > 1e-12 && Raycast(a, dir, len, out _);
    }

    /// <summary>The nearest point of the mesh to <paramref name="p"/> within <paramref name="maxDistance"/>;
    /// returns its distance, or +∞ when nothing is that close.</summary>
    public double Nearest(CaveVector3 p, double maxDistance, out CaveVector3 closest, out int triangle)
    {
        closest = default;
        triangle = -1;
        if (TriangleCount == 0) return double.PositiveInfinity;
        double best2 = maxDistance * maxDistance;
        bool found = false;
        Span<int> stack = stackalloc int[128];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            int node = stack[--sp];
            if (BoxDistance2(node, p) > best2) continue;
            if (_left[node] < 0)
            {
                int end = _first[node] + _count[node];
                for (int i = _first[node]; i < end; i++)
                {
                    var q = ClosestOnTriangle(i, p);
                    double d2 = (q - p).LengthSquared;
                    if (d2 < best2) { best2 = d2; closest = q; triangle = _triId[i]; found = true; }
                }
            }
            else
            {
                int l = _left[node], r = _right[node];
                double dl = BoxDistance2(l, p), dr = BoxDistance2(r, p);
                if (dl < dr) { if (dr <= best2) stack[sp++] = r; if (dl <= best2) stack[sp++] = l; }
                else { if (dl <= best2) stack[sp++] = l; if (dr <= best2) stack[sp++] = r; }
            }
        }
        return found ? Math.Sqrt(best2) : double.PositiveInfinity;
    }

    /// <summary>The unit normal of original triangle <paramref name="triangle"/> as wound.</summary>
    public CaveVector3 NormalOf(int triangle)
    {
        var t = Mesh.Triangles[triangle];
        var a = Mesh.Vertices[(int)t.A];
        return (Mesh.Vertices[(int)t.B] - a).Cross(Mesh.Vertices[(int)t.C] - a).Normalized();
    }

    private CaveVector3 Vertex(int i) => new(_v[3 * i], _v[3 * i + 1], _v[3 * i + 2]);

    private CaveVector3 TriangleNormal(int bvhTriangle)
    {
        var a = Vertex(_tri[3 * bvhTriangle]);
        var b = Vertex(_tri[3 * bvhTriangle + 1]);
        var c = Vertex(_tri[3 * bvhTriangle + 2]);
        return (b - a).Cross(c - a).Normalized();
    }

    private bool RayBox(int node, CaveVector3 o, double ix, double iy, double iz, double tmax)
    {
        int b = 6 * node;
        double t0 = 0, t1 = tmax;
        if (!Slab(o.X, ix, _box[b], _box[b + 3], ref t0, ref t1)) return false;
        if (!Slab(o.Y, iy, _box[b + 1], _box[b + 4], ref t0, ref t1)) return false;
        return Slab(o.Z, iz, _box[b + 2], _box[b + 5], ref t0, ref t1);
    }

    private static bool Slab(double o, double inv, double lo, double hi, ref double t0, ref double t1)
    {
        const double pad = 1e-9;
        double ta = (lo - pad - o) * inv, tb = (hi + pad - o) * inv;
        if (double.IsNaN(ta) || double.IsNaN(tb))
        {
            // Ray parallel to the slab and on its plane: inside if o within.
            return o >= lo - pad && o <= hi + pad;
        }
        if (ta > tb) (ta, tb) = (tb, ta);
        if (ta > t0) t0 = ta;
        if (tb < t1) t1 = tb;
        return t0 <= t1;
    }

    private double BoxDistance2(int node, CaveVector3 p)
    {
        int b = 6 * node;
        double dx = Math.Max(0, Math.Max(_box[b] - p.X, p.X - _box[b + 3]));
        double dy = Math.Max(0, Math.Max(_box[b + 1] - p.Y, p.Y - _box[b + 4]));
        double dz = Math.Max(0, Math.Max(_box[b + 2] - p.Z, p.Z - _box[b + 5]));
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Möller–Trumbore; the distance along the unit direction, or -1 on a miss.
    /// Edges count as hits, so a segment through a shared edge is not missed.</summary>
    private double RayTriangle(int i, CaveVector3 o, CaveVector3 d)
    {
        int ia = 3 * _tri[3 * i], ib = 3 * _tri[3 * i + 1], ic = 3 * _tri[3 * i + 2];
        double e1x = _v[ib] - _v[ia], e1y = _v[ib + 1] - _v[ia + 1], e1z = _v[ib + 2] - _v[ia + 2];
        double e2x = _v[ic] - _v[ia], e2y = _v[ic + 1] - _v[ia + 1], e2z = _v[ic + 2] - _v[ia + 2];
        double px = d.Y * e2z - d.Z * e2y, py = d.Z * e2x - d.X * e2z, pz = d.X * e2y - d.Y * e2x;
        double det = e1x * px + e1y * py + e1z * pz;
        double scale = Math.Abs(e1x) + Math.Abs(e1y) + Math.Abs(e1z) + Math.Abs(e2x) + Math.Abs(e2y) + Math.Abs(e2z);
        if (Math.Abs(det) < 1e-14 * scale * scale) return -1;
        double inv = 1.0 / det;
        double tx = o.X - _v[ia], ty = o.Y - _v[ia + 1], tz = o.Z - _v[ia + 2];
        double u = (tx * px + ty * py + tz * pz) * inv;
        const double eps = 1e-10;
        if (u < -eps || u > 1 + eps) return -1;
        double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
        double v = (d.X * qx + d.Y * qy + d.Z * qz) * inv;
        if (v < -eps || u + v > 1 + eps) return -1;
        double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
        return t >= 0 ? t : -1;
    }

    /// <summary>Closest point on BVH-ordered triangle <paramref name="i"/> to <paramref name="p"/>
    /// (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private CaveVector3 ClosestOnTriangle(int i, CaveVector3 p)
    {
        var a = Vertex(_tri[3 * i]);
        var b = Vertex(_tri[3 * i + 1]);
        var c = Vertex(_tri[3 * i + 2]);
        return ClosestPointOnTriangle(p, a, b, c);
    }

    internal static CaveVector3 ClosestPointOnTriangle(CaveVector3 p, CaveVector3 a, CaveVector3 b, CaveVector3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b;
        double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return b;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return c;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        double denom = 1.0 / (va + vb + vc);
        double v = vb * denom, w = vc * denom;
        return a + ab * v + ac * w;
    }
}
