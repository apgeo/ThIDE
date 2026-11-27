// The generalised winding number of a triangle mesh at a point (Jacobson et al. 2013), evaluated
// hierarchically on the BVH (Barill et al. 2018): exact solid angles near the point, a dipole per
// BVH node far from it. About 1 inside a closed surface and 0 outside whatever the mesh's quality;
// a hole only blurs it locally (≈ 0.5 across the hole), so "inside" (|w| > 0.5) does not leak through
// scan holes the way a flood fill does. The sign depends on the winding direction, so callers use
// the magnitude.

using Therion.Cave3D;

namespace Therion.MeshSurvey.Spatial;

/// <summary>Winding-number queries over a <see cref="TriangleBvh"/>.</summary>
public sealed class FastWindingNumber
{
    private const double Beta = 3.0;

    private readonly TriangleBvh _bvh;
    private readonly double[] _dipole;  // per node: Nx Ny Nz (area-weighted normal sum ×½) px py pz (area-weighted centroid) r

    public FastWindingNumber(TriangleBvh bvh)
    {
        ArgumentNullException.ThrowIfNull(bvh);
        _bvh = bvh;
        _dipole = new double[Math.Max(1, bvh.NodeCount) * 7];
        if (bvh.TriangleCount > 0) Prepare();
    }

    private void Prepare()
    {
        var v = _bvh.VertexData;
        var t = _bvh.TriangleData;
        int n = _bvh.NodeCount;
        var area = new double[n];
        // Post-order: children have higher indices than their parent in the builder, so a reverse
        // sweep sees children first.
        for (int node = n - 1; node >= 0; node--)
        {
            int o = 7 * node;
            double nx = 0, ny = 0, nz = 0, px = 0, py = 0, pz = 0, a = 0;
            if (_bvh.IsLeaf(node))
            {
                int end = _bvh.LeafFirst(node) + _bvh.LeafCount(node);
                for (int i = _bvh.LeafFirst(node); i < end; i++)
                {
                    int ia = 3 * t[3 * i], ib = 3 * t[3 * i + 1], ic = 3 * t[3 * i + 2];
                    double e1x = v[ib] - v[ia], e1y = v[ib + 1] - v[ia + 1], e1z = v[ib + 2] - v[ia + 2];
                    double e2x = v[ic] - v[ia], e2y = v[ic + 1] - v[ia + 1], e2z = v[ic + 2] - v[ia + 2];
                    double cx = 0.5 * (e1y * e2z - e1z * e2y), cy = 0.5 * (e1z * e2x - e1x * e2z), cz = 0.5 * (e1x * e2y - e1y * e2x);
                    double ta = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                    nx += cx; ny += cy; nz += cz;
                    px += ta * (v[ia] + v[ib] + v[ic]) / 3.0;
                    py += ta * (v[ia + 1] + v[ib + 1] + v[ic + 1]) / 3.0;
                    pz += ta * (v[ia + 2] + v[ib + 2] + v[ic + 2]) / 3.0;
                    a += ta;
                }
            }
            else
            {
                foreach (int c in (ReadOnlySpan<int>)[_bvh.Left(node), _bvh.Right(node)])
                {
                    int oc = 7 * c;
                    nx += _dipole[oc]; ny += _dipole[oc + 1]; nz += _dipole[oc + 2];
                    px += area[c] * _dipole[oc + 3]; py += area[c] * _dipole[oc + 4]; pz += area[c] * _dipole[oc + 5];
                    a += area[c];
                }
            }
            area[node] = a;
            var box = _bvh.NodeBox(node);
            if (a > 0) { px /= a; py /= a; pz /= a; }
            else { px = 0.5 * (box[0] + box[3]); py = 0.5 * (box[1] + box[4]); pz = 0.5 * (box[2] + box[5]); }
            double r2 = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                double cx = (corner & 1) == 0 ? box[0] : box[3];
                double cy = (corner & 2) == 0 ? box[1] : box[4];
                double cz = (corner & 4) == 0 ? box[2] : box[5];
                double d2 = (cx - px) * (cx - px) + (cy - py) * (cy - py) + (cz - pz) * (cz - pz);
                if (d2 > r2) r2 = d2;
            }
            _dipole[o] = nx; _dipole[o + 1] = ny; _dipole[o + 2] = nz;
            _dipole[o + 3] = px; _dipole[o + 4] = py; _dipole[o + 5] = pz;
            _dipole[o + 6] = Math.Sqrt(r2);
        }
    }

    /// <summary>The winding number at <paramref name="q"/> (signed; ±1 inside a closed surface, 0 outside).</summary>
    public double At(CaveVector3 q)
    {
        if (_bvh.TriangleCount == 0) return 0;
        var v = _bvh.VertexData;
        var t = _bvh.TriangleData;
        double sum = 0;
        Span<int> stack = stackalloc int[128];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            int node = stack[--sp];
            int o = 7 * node;
            double dx = _dipole[o + 3] - q.X, dy = _dipole[o + 4] - q.Y, dz = _dipole[o + 5] - q.Z;
            double d2 = dx * dx + dy * dy + dz * dz;
            double r = _dipole[o + 6];
            if (d2 > Beta * Beta * r * r)
            {
                double d = Math.Sqrt(d2);
                sum += (dx * _dipole[o] + dy * _dipole[o + 1] + dz * _dipole[o + 2]) / (d2 * d);
                continue;
            }
            if (_bvh.IsLeaf(node))
            {
                int end = _bvh.LeafFirst(node) + _bvh.LeafCount(node);
                for (int i = _bvh.LeafFirst(node); i < end; i++)
                    sum += SolidAngle(v, 3 * t[3 * i], 3 * t[3 * i + 1], 3 * t[3 * i + 2], q);
            }
            else
            {
                stack[sp++] = _bvh.Left(node);
                stack[sp++] = _bvh.Right(node);
            }
        }
        return sum / (4.0 * Math.PI);
    }

    /// <summary>True when <paramref name="q"/> is inside (|w| &gt; 0.5).</summary>
    public bool IsInside(CaveVector3 q) => Math.Abs(At(q)) > 0.5;

    /// <summary>Signed solid angle of a triangle seen from q (Van Oosterom &amp; Strackee 1983).</summary>
    private static double SolidAngle(ReadOnlySpan<double> v, int ia, int ib, int ic, CaveVector3 q)
    {
        double ax = v[ia] - q.X, ay = v[ia + 1] - q.Y, az = v[ia + 2] - q.Z;
        double bx = v[ib] - q.X, by = v[ib + 1] - q.Y, bz = v[ib + 2] - q.Z;
        double cx = v[ic] - q.X, cy = v[ic + 1] - q.Y, cz = v[ic + 2] - q.Z;
        double la = Math.Sqrt(ax * ax + ay * ay + az * az);
        double lb = Math.Sqrt(bx * bx + by * by + bz * bz);
        double lc = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        double det = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
        double div = la * lb * lc + (ax * bx + ay * by + az * bz) * lc + (ax * cx + ay * cy + az * cz) * lb + (bx * cx + by * cy + bz * cz) * la;
        return 2.0 * Math.Atan2(det, div);
    }
}
