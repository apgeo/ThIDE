// Questions a test asks of a wall mesh as a surface: how near a point is to it, whether a ray
// from inside gets out, whether its triangles close up edge to edge. Brute force over the
// triangles, which is quick enough for the invented caves.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

internal static class MeshProbe
{
    public static (CaveVector3 A, CaveVector3 B, CaveVector3 C) Corners(CaveMesh mesh, CaveTriangle triangle)
        => (mesh.Vertices[(int)triangle.A], mesh.Vertices[(int)triangle.B], mesh.Vertices[(int)triangle.C]);

    /// <summary>The distance from <paramref name="point"/> to the nearest triangle.</summary>
    public static double Nearest(CaveMesh mesh, CaveVector3 point)
    {
        double nearest = double.PositiveInfinity;
        foreach (var triangle in mesh.Triangles)
        {
            var (a, b, c) = Corners(mesh, triangle);
            nearest = Math.Min(nearest, Distance(point, a, b, c));
        }
        return nearest;
    }

    /// <summary>The distance from a point to a triangle: to its face where the point is over
    /// it, else to the nearest edge or corner.</summary>
    public static double Distance(CaveVector3 p, CaveVector3 a, CaveVector3 b, CaveVector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return ap.Length;

        var bp = p - b;
        double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return bp.Length;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).Length;

        var cp = p - c;
        double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return cp.Length;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).Length;

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).Length;

        double scale = 1.0 / (va + vb + vc);
        return (p - (a + ab * (vb * scale) + ac * (vc * scale))).Length;
    }

    /// <summary>True when the ray from <paramref name="origin"/> along
    /// <paramref name="direction"/> meets a triangle, edges included.</summary>
    public static bool RayHits(CaveMesh mesh, CaveVector3 origin, CaveVector3 direction)
    {
        foreach (var triangle in mesh.Triangles)
        {
            var (a, b, c) = Corners(mesh, triangle);
            var e1 = b - a;
            var e2 = c - a;
            var h = direction.Cross(e2);
            double det = e1.Dot(h);
            if (Math.Abs(det) < 1e-14) continue;
            var s = origin - a;
            double u = s.Dot(h) / det;
            if (u < -1e-9 || u > 1 + 1e-9) continue;
            var q = s.Cross(e1);
            double v = direction.Dot(q) / det;
            if (v < -1e-9 || u + v > 1 + 1e-9) continue;
            if (e2.Dot(q) / det > 1e-9) return true;
        }
        return false;
    }

    /// <summary>The share of <paramref name="count"/> directions, spread evenly over the
    /// sphere, in which a ray from <paramref name="origin"/> meets no triangle: how much of
    /// the view from there is a hole in the mesh.</summary>
    public static double OpenShare(CaveMesh mesh, CaveVector3 origin, int count = 600)
    {
        double turn = Math.PI * (3.0 - Math.Sqrt(5.0));
        int open = 0;
        for (int i = 0; i < count; i++)
        {
            double z = 1.0 - 2.0 * (i + 0.5) / count, r = Math.Sqrt(1.0 - z * z);
            if (!RayHits(mesh, origin, new CaveVector3(r * Math.Cos(turn * i), r * Math.Sin(turn * i), z))) open++;
        }
        return (double)open / count;
    }

    /// <summary>How many triangles use each edge, with vertices at one position counted as one
    /// vertex, and how many edges are run the same way by two triangles (which face opposite
    /// ways across that edge). A closed, consistently wound skin has two triangles on every edge
    /// and none run twice the same way.</summary>
    public static (IReadOnlyDictionary<(CaveVector3, CaveVector3), int> Uses, int RunTwiceTheSameWay) Edges(CaveMesh mesh)
    {
        var uses = new Dictionary<(CaveVector3, CaveVector3), int>();
        var runs = new HashSet<(CaveVector3, CaveVector3)>();
        int twice = 0;
        foreach (var triangle in mesh.Triangles)
        {
            var (a, b, c) = Corners(mesh, triangle);
            foreach (var (from, to) in new[] { (a, b), (b, c), (c, a) })
            {
                if (!runs.Add((from, to))) twice++;
                var key = Before(from, to) ? (from, to) : (to, from);
                uses[key] = uses.GetValueOrDefault(key) + 1;
            }
        }
        return (uses, twice);
    }

    private static bool Before(CaveVector3 p, CaveVector3 q)
        => p.X != q.X ? p.X < q.X : p.Y != q.Y ? p.Y < q.Y : p.Z < q.Z;
}
