// A passage axis resampled for station placement: points every sample spacing along it, each with
// the passage radius, the direction of the passage and a feature score — how much a surveyor would
// want a station there (a bend, a squeeze).

using Therion.Cave3D;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Plan;

internal sealed class AxisPath
{
    public required CaveVector3[] Points { get; init; }
    public required double[] Radii { get; init; }
    public required CaveVector3[] Tangents { get; init; }
    public required double[] Feature { get; init; }
    public required double[] Arc { get; init; }

    public int Count => Points.Length;
    public double Length => Arc[^1];

    /// <summary>Resamples a polyline (internal frame) at <paramref name="spacing"/>, reading radii from the void.
    /// With <paramref name="cast"/> (distance to the wall along a direction, or null), every inner sample
    /// is moved to the middle of its cross-section: the voxel axis is only good to half a voxel.</summary>
    public static AxisPath From(IReadOnlyList<CaveVector3> polyline, VoidGrid grid, double spacing, Func<CaveVector3, CaveVector3, double?>? cast = null)
    {
        var src = new List<CaveVector3> { polyline[0] };
        for (int i = 1; i < polyline.Count; i++)
            if ((polyline[i] - src[^1]).Length > 1e-9) src.Add(polyline[i]);
        if (src.Count == 1) src.Add(src[0]);
        var cum = new double[src.Count];
        for (int i = 1; i < src.Count; i++) cum[i] = cum[i - 1] + (src[i] - src[i - 1]).Length;
        double total = cum[^1];
        int n = Math.Max(1, (int)Math.Ceiling(total / spacing));
        var pts = new CaveVector3[n + 1];
        var arc = new double[n + 1];
        int seg = 0;
        for (int k = 0; k <= n; k++)
        {
            double s = total * k / n;
            while (seg < src.Count - 2 && cum[seg + 1] < s) seg++;
            double len = cum[seg + 1] - cum[seg];
            double t = len > 0 ? (s - cum[seg]) / len : 0;
            pts[k] = src[seg].Lerp(src[seg + 1], Math.Clamp(t, 0, 1));
            arc[k] = s;
        }
        pts[0] = polyline[0];
        pts[^1] = polyline[^1];
        var radii = pts.Select(p => Math.Max(grid.DistanceAt(p), RadiusNear(grid, p))).ToArray();

        var tangents = new CaveVector3[pts.Length];
        int w = Math.Max(1, (int)Math.Round(0.5 / spacing));
        for (int k = 0; k < pts.Length; k++)
        {
            var d = pts[Math.Min(pts.Length - 1, k + w)] - pts[Math.Max(0, k - w)];
            tangents[k] = d.LengthSquared > 0 ? d.Normalized() : new CaveVector3(1, 0, 0);
        }

        if (cast is not null)
            for (int k = 1; k < pts.Length - 1; k++) pts[k] = Recentre(pts[k], tangents[k], radii[k], grid, cast);

        var feature = new double[pts.Length];
        for (int k = 1; k < pts.Length - 1; k++)
        {
            // Bend: the turn between the axis a radius (at least a metre) before and after.
            double window = Math.Max(1.0, radii[k]);
            int wb = Math.Max(1, (int)Math.Round(window / spacing));
            var before = pts[k] - pts[Math.Max(0, k - wb)];
            var after = pts[Math.Min(pts.Length - 1, k + wb)] - pts[k];
            double bend = 0;
            if (before.LengthSquared > 0 && after.LengthSquared > 0)
            {
                double cos = Math.Clamp(before.Normalized().Dot(after.Normalized()), -1, 1);
                bend = Math.Clamp(Math.Acos(cos) / (Math.PI / 3), 0, 1); // 60° turn = full score
            }
            // Squeeze: the narrowest point within 3 m, relative to the median there.
            int ws = Math.Max(1, (int)Math.Round(3.0 / spacing));
            int lo = Math.Max(0, k - ws), hi = Math.Min(pts.Length - 1, k + ws);
            double min = double.PositiveInfinity;
            var around = new List<double>();
            for (int j = lo; j <= hi; j++) { min = Math.Min(min, radii[j]); around.Add(radii[j]); }
            around.Sort();
            double med = around[around.Count / 2];
            double squeeze = radii[k] <= min + 1e-9 && med > 0 ? Math.Clamp(1 - radii[k] / med, 0, 1) * 2 : 0;
            feature[k] = Math.Clamp(Math.Max(bend, squeeze), 0, 1);
        }
        // Keep only local maxima so a bend gets one station, not a run of them.
        var peaks = new double[pts.Length];
        int wp = Math.Max(1, (int)Math.Round(1.0 / spacing));
        for (int k = 1; k < pts.Length - 1; k++)
        {
            bool peak = true;
            for (int j = Math.Max(1, k - wp); j <= Math.Min(pts.Length - 2, k + wp) && peak; j++)
                if (feature[j] > feature[k] || (feature[j] == feature[k] && j < k)) peak = false;
            peaks[k] = peak ? feature[k] : 0;
        }
        return new AxisPath { Points = pts, Radii = radii, Tangents = tangents, Feature = peaks, Arc = arc };
    }

    private static CaveVector3 Recentre(CaveVector3 p, CaveVector3 tangent, double radius, VoidGrid grid, Func<CaveVector3, CaveVector3, double?> cast)
    {
        var u = new CaveVector3(tangent.Y, -tangent.X, 0);
        u = u.LengthSquared < 1e-9 ? new CaveVector3(1, 0, 0) : u.Normalized();
        var v = u.Cross(tangent).Normalized();
        var q = p;
        for (int it = 0; it < 2; it++)
        {
            if (cast(q, u) is not { } pu || cast(q, u * -1) is not { } nu || cast(q, v) is not { } pv || cast(q, v * -1) is not { } nv) break;
            var moved = q + u * ((pu - nu) / 2) + v * ((pv - nv) / 2);
            if ((moved - p).Length > Math.Max(0.5 * radius, grid.VoxelSize) || !grid.Contains(moved)) break;
            q = moved;
        }
        return q;
    }

    private static double RadiusNear(VoidGrid grid, CaveVector3 p)
    {
        int id = grid.NearestVoid(p, 3);
        return id < 0 ? 0.5 * grid.VoxelSize : grid.Distance[id];
    }

    /// <summary>The largest distance of points i+1 … j−1 of <paramref name="q"/> from the segment q_i q_j.</summary>
    public static double Deviation(CaveVector3[] q, int i, int j)
    {
        var a = q[i];
        var ab = q[j] - a;
        double len2 = ab.LengthSquared;
        double max = 0;
        for (int k = i + 1; k < j; k++)
        {
            var ap = q[k] - a;
            double t = len2 > 0 ? Math.Clamp(ap.Dot(ab) / len2, 0, 1) : 0;
            double d = (ap - ab * t).Length;
            if (d > max) max = d;
        }
        return max;
    }
}
