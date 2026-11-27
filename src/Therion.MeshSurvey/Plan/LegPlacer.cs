// Stations along one passage, chosen by dynamic programming. The candidates are points every sample
// spacing along the axis (anchored as the settings say); a leg between two candidates is allowed when
// its length is in range, the passage stays within the deviation limit of it, and the void leaves
// the clearance round it. Among all allowed sequences the cheapest is taken, the cost depending on
// the strategy: closest to the preferred length, fewest legs, even spacing, or features only. When no
// sequence is allowed the limits are relaxed one step at a time, and each relaxation is reported.

using Therion.Cave3D;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Plan;

internal sealed record Placement(List<CaveVector3> Stations, List<int> Indices, int RelaxLevel, List<double> Features)
{
    public bool Relaxed => RelaxLevel > 0;
}

internal static class LegPlacer
{
    public static readonly string[] RelaxationNames =
    [
        "none", "clearance halved", "no clearance", "minimum length ignored", "deviation doubled", "deviation ignored", "line of sight ignored",
    ];

    public static Placement Place(MeshSurveySession session, VoidGrid grid, AxisPath axis, CaveVector3 start, CaveVector3 end,
        bool startAnchored, bool endAnchored, LegOptions legs, AnchorOptions anchor, ISet<(int, int)>? forbidden = null)
    {
        int n = axis.Count - 1;
        var q = new CaveVector3[axis.Count];
        for (int k = 1; k < n; k++) q[k] = Anchoring.Place(session, axis.Points[k], axis.Tangents[k], axis.Radii[k], anchor);
        q[0] = start;
        q[n] = end;
        bool anchoredMid = anchor.Mode != AnchorMode.Axial;
        if (n <= 1) return new Placement([start, end], [0, n], 0, [0, 0]);

        for (int level = 0; level < RelaxationNames.Length; level++)
        {
            var chosen = Solve(grid, axis, q, startAnchored, endAnchored, anchoredMid, legs, anchor, level, forbidden);
            if (chosen is not null)
                return new Placement(chosen.Select(k => q[k]).ToList(), chosen, level, chosen.Select(k => axis.Feature[k]).ToList());
        }
        // Cannot happen (the last level allows every step between neighbouring samples), but stay total.
        return new Placement([start, end], [0, n], RelaxationNames.Length, [0, 0]);
    }

    private static List<int>? Solve(VoidGrid grid, AxisPath axis, CaveVector3[] q, bool startAnchored, bool endAnchored, bool anchoredMid,
        LegOptions o, AnchorOptions anchor, int level, ISet<(int, int)>? forbidden)
    {
        int n = q.Length - 1;
        double clearance = level >= 2 ? 0 : level == 1 ? o.Clearance / 2 : o.Clearance;
        bool ignoreMin = level >= 3;
        double devFactor = level >= 5 ? double.PositiveInfinity : level >= 4 ? 2 : 1;
        bool ignoreSight = level >= 6;
        double sinVertical = Math.Sin(o.VerticalAngle * Math.PI / 180);
        double maxArc = Math.Max(o.Max, o.MaxVertical ?? 200.0) * 1.6 + 1;
        double offset = Math.Max(anchor.FloorOffset, anchor.WallOffset) + Anchoring.Nudge;
        double Allow(int k) => (k == 0 ? startAnchored : k == n ? endAnchored : anchoredMid) ? o.Clearance + offset : o.Clearance;
        int legsTarget = Math.Max(1, (int)Math.Round(axis.Length / o.Preferred));
        double uniformLength = axis.Length / legsTarget;

        var best = new double[n + 1];
        var prev = new int[n + 1];
        Array.Fill(best, double.PositiveInfinity);
        Array.Fill(prev, -1);
        best[0] = 0;
        for (int i = 0; i < n; i++)
        {
            if (double.IsPositiveInfinity(best[i])) continue;
            for (int j = i + 1; j <= n; j++)
            {
                if (axis.Arc[j] - axis.Arc[i] > maxArc) break;
                if (forbidden is not null && forbidden.Contains((i, j))) continue;
                var chord = q[j] - q[i];
                double d = chord.Length;
                if (d < 1e-6) continue;
                bool vertical = Math.Abs(chord.Z) / d >= sinVertical;
                double maxLen = vertical ? o.MaxVertical ?? double.PositiveInfinity : o.Max;
                if (d > maxLen && !(level >= 6 && j == i + 1)) continue;
                bool whole = i == 0 && j == n;
                if (d < o.Min && !ignoreMin && !whole) continue;
                // The passage stays near the leg.
                bool near = true;
                if (!double.IsPositiveInfinity(devFactor))
                {
                    var ab = chord;
                    double len2 = ab.LengthSquared;
                    for (int k = i + 1; k < j && near; k++)
                    {
                        var ap = q[k] - q[i];
                        double t = Math.Clamp(ap.Dot(ab) / len2, 0, 1);
                        double dev = (ap - ab * t).Length;
                        double limit = Math.Max(o.MaxDeviationFloor, o.MaxDeviation * axis.Radii[k]) * devFactor;
                        if (dev > limit) near = false;
                    }
                }
                if (!near) continue;
                if (!ignoreSight && !grid.SegmentClear(q[i], q[j], clearance, Allow(i), Allow(j))) continue;

                double cost = Cost(o, axis, q, i, j, d, uniformLength, n);
                double total = best[i] + cost - (j < n ? o.FeatureWeight * axis.Feature[j] : 0);
                if (total < best[j]) { best[j] = total; prev[j] = i; }
            }
        }
        if (double.IsPositiveInfinity(best[n])) return null;
        var chosen = new List<int>();
        for (int k = n; k >= 0; k = prev[k])
        {
            chosen.Add(k);
            if (k == 0) break;
        }
        chosen.Reverse();
        return chosen;
    }

    private static double Cost(LegOptions o, AxisPath axis, CaveVector3[] q, int i, int j, double d, double uniformLength, int n)
    {
        switch (o.Strategy)
        {
            case LegStrategy.Fewest:
                return 1 + 0.01 * Square((d - o.Preferred) / o.Preferred);
            case LegStrategy.Uniform:
                return 0.02 + Square((d - uniformLength) / uniformLength);
            case LegStrategy.Features:
                return 0.1 * d / o.Max + (j == n || axis.Feature[j] >= 0.4 ? 0 : 5);
            default:
            {
                double length = Square((d - o.Preferred) / o.Preferred);
                // Zig-zag: how far the leg turns away from the passage's own direction.
                var mean = CaveVector3.Zero;
                for (int k = i; k <= j; k++) mean += axis.Tangents[k];
                double turn = mean.LengthSquared > 0 ? 1 - Math.Abs(mean.Normalized().Dot((q[j] - q[i]) / d)) : 0;
                return length + o.TurnWeight * turn;
            }
        }
    }

    private static double Square(double x) => x * x;
}
