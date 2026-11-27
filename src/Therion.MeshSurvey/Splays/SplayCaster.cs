// Splays: shots from each station to the wall, cast against the model itself (the original mesh, not
// the voxels), in one of six direction patterns. A shot that hits nothing within range is dropped,
// never invented; so is one that grazes the wall, and one that only finds the rock the station stands
// on. All random choices (the rotation of a pattern) come from the seed.

using Therion.Cave3D;
using Therion.MeshSurvey.Plan;

namespace Therion.MeshSurvey.Splays;

internal static class SplayCaster
{
    public static (List<SurveySplay> Splays, SplayReport Report) Cast(MeshSurveySession session, DerivedSurvey survey, SplayOptions o,
        CoverageModel? coverage, int seed, CancellationToken ct)
    {
        var splays = new List<SurveySplay>();
        if (o.Mode == SplayMode.None || survey.Stations.Count == 0)
            return (splays, new SplayReport { Mode = o.Mode });
        int missed = 0, outOfRange = 0, grazing = 0, capped = 0;
        double cosLimit = Math.Cos(o.MaxIncidence * Math.PI / 180);
        var widths = survey.Stations.Select(s => s.Lrud is { } l && l.Left >= 0 && l.Right >= 0 ? (l.Left + l.Right) / 2 : -1).ToArray();
        var known = widths.Where(w => w > 0).Order().ToList();
        double medianWidth = known.Count > 0 ? known[known.Count / 2] : 1;

        for (int i = 0; i < survey.Stations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var station = survey.Stations[i];
            var p = session.Model.ToInternal(station.Position);
            var random = new Random(unchecked(seed * 7919 + i * 104729));
            var (along, right, up) = Frame(survey, i);
            int count = o.Count;
            if (o.PerMetre is { } perMetre)
            {
                double half = survey.Legs.Where(l => l.From == i || l.To == i).Sum(l => l.Check.Length) / 2;
                count = Math.Max(1, (int)Math.Round(perMetre * half));
            }
            if (o.ScaleBySize && widths[i] > 0 && o.Mode is not SplayMode.Lrud)
                count = Math.Max(1, (int)Math.Round(count * Math.Clamp(widths[i] / medianWidth, 0.5, 3)));

            IEnumerable<CaveVector3> directions = o.Mode switch
            {
                SplayMode.Lrud => [right * -1, right, new CaveVector3(0, 0, 1), new CaveVector3(0, 0, -1)],
                SplayMode.Ring => Ring(along, right, count, o),
                SplayMode.Sphere => Sphere(count, random),
                _ => [],
            };
            var hits = new List<CaveVector3>();
            void Shoot(CaveVector3 d)
            {
                if (!Hit(session, p, d, o.MaxRange, out var end, out var normal)) { missed++; return; }
                double len = (end - p).Length;
                if (len < Math.Max(o.MinRange, o.SelfClearance)) { outOfRange++; return; }
                if (normal.LengthSquared > 0 && Math.Abs(normal.Dot(d.Normalized())) < cosLimit) { grazing++; return; }
                hits.Add(end);
            }
            if (o.Mode == SplayMode.Adaptive) hits.AddRange(Adaptive(session, p, count, o, random, ref missed, ref outOfRange, ref grazing, cosLimit));
            else if (o.Mode == SplayMode.WallUniform) hits.AddRange(WallUniform(session, p, count, o, random, cosLimit));
            else foreach (var d in directions) Shoot(d);

            foreach (var end in hits)
            {
                if (splays.Count >= o.MaxTotal) { capped++; continue; }
                var model = session.Model.ToModel(end);
                splays.Add(new SurveySplay(i, model) { Length = (model - station.Position).Length });
            }
        }

        double sampled = 0;
        if (coverage is not null && coverage.SignificantTotal > 0 && splays.Count > 0)
        {
            var ends = splays.Select(s => session.Model.ToInternal(s.End)).ToList();
            var index = new PointIndex(ends, Math.Max(1.0, 4 * coverage.SampleSpacing));
            double radius = 2 * coverage.SampleSpacing, seen = 0;
            for (int s = 0; s < coverage.Points.Length; s++)
                if (coverage.Significant[s] && index.Within(coverage.Points[s], radius).Count > 0) seen += coverage.Weight[s];
            sampled = seen / coverage.SignificantTotal;
        }
        return (splays, new SplayReport
        {
            Mode = o.Mode, Splays = splays.Count, Missed = missed, OutOfRange = outOfRange, Grazing = grazing, Capped = capped, WallSampled = sampled,
        });
    }

    /// <summary>The station's passage frame: along the passage (mean of its legs), right (horizontal,
    /// across), up (square to both).</summary>
    private static (CaveVector3 Along, CaveVector3 Right, CaveVector3 Up) Frame(DerivedSurvey survey, int i)
    {
        var along = CaveVector3.Zero;
        var at = survey.Stations[i].Position;
        foreach (var l in survey.Legs)
        {
            if (l.Role == LegRole.Side) continue;
            if (l.From == i) along += (survey.Stations[l.To].Position - at).Normalized();
            else if (l.To == i) along += (at - survey.Stations[l.From].Position).Normalized();
        }
        if (along.LengthSquared < 1e-12) along = new CaveVector3(0, 1, 0);
        along = along.Normalized();
        var right = new CaveVector3(along.Y, -along.X, 0);
        if (right.LengthSquared < 1e-9) right = new CaveVector3(1, 0, 0);
        right = right.Normalized();
        var up = right.Cross(along).Normalized();
        return (along, right, up);
    }

    private static IEnumerable<CaveVector3> Ring(CaveVector3 along, CaveVector3 right, int count, SplayOptions o)
    {
        var up = right.Cross(along).Normalized();
        double tilt = o.RingTilt * Math.PI / 180;
        // Tilt the ring's plane about the right axis.
        var a = (along * Math.Cos(tilt) + up * Math.Sin(tilt)).Normalized();
        var u = right;
        var v = u.Cross(a).Normalized();
        const double phase = 0; // a ring starts at the right wall, as surveyors shoot it
        int n = Math.Max(1, count - (o.ForeBack ? 2 : 0));
        for (int k = 0; k < n; k++)
        {
            double t = phase + 2 * Math.PI * k / n;
            yield return u * Math.Cos(t) + v * Math.Sin(t);
        }
        if (o.ForeBack) { yield return along; yield return along * -1; }
    }

    /// <summary>Fibonacci lattice, turned by a random rotation so stations' lattices do not line up.</summary>
    private static IEnumerable<CaveVector3> Sphere(int count, Random random)
    {
        var (ax, ay, az) = RandomRotation(random);
        double golden = Math.PI * (3 - Math.Sqrt(5));
        for (int k = 0; k < count; k++)
        {
            double z = 1 - 2 * (k + 0.5) / count;
            double r = Math.Sqrt(Math.Max(0, 1 - z * z));
            double t = golden * k;
            var d = new CaveVector3(r * Math.Cos(t), r * Math.Sin(t), z);
            yield return new CaveVector3(ax.Dot(d), ay.Dot(d), az.Dot(d));
        }
    }

    private static (CaveVector3, CaveVector3, CaveVector3) RandomRotation(Random random)
    {
        // A random orthonormal frame (Gram–Schmidt on two random vectors).
        CaveVector3 Rand() => new(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1);
        var a = Rand().Normalized();
        if (a.LengthSquared == 0) a = new CaveVector3(1, 0, 0);
        var b = Rand();
        b = (b - a * a.Dot(b)).Normalized();
        if (b.LengthSquared == 0) b = Math.Abs(a.X) < 0.9 ? new CaveVector3(1, 0, 0).Cross(a).Normalized() : new CaveVector3(0, 1, 0).Cross(a).Normalized();
        return (a, b, a.Cross(b));
    }

    private static bool Hit(MeshSurveySession session, CaveVector3 p, CaveVector3 d, double range, out CaveVector3 end, out CaveVector3 normal)
    {
        normal = CaveVector3.Zero;
        if (session.Bvh is { } bvh)
        {
            if (bvh.Raycast(p, d, range, out var h)) { end = h.Point; normal = h.Normal; return true; }
            end = default;
            return false;
        }
        return Anchoring.Cast(session, p, d, range, out end);
    }

    private static List<CaveVector3> Adaptive(MeshSurveySession session, CaveVector3 p, int count, SplayOptions o, Random random,
        ref int missed, ref int outOfRange, ref int grazing, double cosLimit)
    {
        // Icosahedron, refined along the edges whose two ends differ in depth by more than the jump.
        double t = (1 + Math.Sqrt(5)) / 2;
        var dirs = new List<CaveVector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var (ax, ay, az) = RandomRotation(random);
        dirs = dirs.Select(d => { var n = d.Normalized(); return new CaveVector3(ax.Dot(n), ay.Dot(n), az.Dot(n)); }).ToList();
        var depth = new List<double>();
        var ends = new List<CaveVector3?>();
        void Probe(CaveVector3 d)
        {
            if (Hit(session, p, d, o.MaxRange, out var e, out _)) { depth.Add((e - p).Length); ends.Add(e); }
            else { depth.Add(o.MaxRange); ends.Add(null); }
        }
        foreach (var d in dirs) Probe(d);
        double edgeAngle = 1.2; // icosahedron neighbours are 63° apart
        while (dirs.Count < count)
        {
            var add = new List<CaveVector3>();
            for (int i = 0; i < dirs.Count && dirs.Count + add.Count < count; i++)
                for (int j = i + 1; j < dirs.Count && dirs.Count + add.Count < count; j++)
                {
                    double angle = Math.Acos(Math.Clamp(dirs[i].Dot(dirs[j]), -1, 1));
                    if (angle > edgeAngle || angle < 0.05) continue;
                    double a = depth[i], b = depth[j];
                    if (Math.Abs(a - b) <= o.DepthJump * Math.Max(a, b)) continue;
                    var mid = (dirs[i] + dirs[j]).Normalized();
                    if (add.Any(x => x.Dot(mid) > 0.9999) || dirs.Any(x => x.Dot(mid) > 0.9999)) continue;
                    add.Add(mid);
                }
            if (add.Count == 0) break;
            foreach (var d in add) { dirs.Add(d); Probe(d); }
            edgeAngle /= 2;
        }
        var result = new List<CaveVector3>();
        for (int i = 0; i < dirs.Count; i++)
        {
            if (ends[i] is not { } e) { missed++; continue; }
            double len = (e - p).Length;
            if (len < Math.Max(o.MinRange, o.SelfClearance)) { outOfRange++; continue; }
            if (session.Bvh is { } bvh && bvh.Raycast(p, dirs[i], o.MaxRange, out var h) && Math.Abs(h.Normal.Dot(dirs[i])) < cosLimit) { grazing++; continue; }
            result.Add(e);
        }
        return result;
    }

    /// <summary>A dense probe, then the hits spread evenly on the wall by farthest-point selection.</summary>
    private static List<CaveVector3> WallUniform(MeshSurveySession session, CaveVector3 p, int count, SplayOptions o, Random random, double cosLimit)
    {
        var candidates = new List<CaveVector3>();
        foreach (var d in Sphere(512, random))
        {
            if (!Hit(session, p, d, o.MaxRange, out var e, out var n)) continue;
            double len = (e - p).Length;
            if (len < Math.Max(o.MinRange, o.SelfClearance)) continue;
            if (n.LengthSquared > 0 && Math.Abs(n.Dot(d)) < cosLimit) continue;
            candidates.Add(e);
        }
        var chosen = new List<CaveVector3>();
        if (candidates.Count == 0) return chosen;
        var minD = candidates.Select(_ => double.PositiveInfinity).ToArray();
        int next = 0;
        // Start from the farthest hit: the deepest part of the view.
        for (int i = 1; i < candidates.Count; i++) if ((candidates[i] - p).LengthSquared > (candidates[next] - p).LengthSquared) next = i;
        while (chosen.Count < count && chosen.Count < candidates.Count)
        {
            var c = candidates[next];
            chosen.Add(c);
            int best = -1;
            double bestD = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                minD[i] = Math.Min(minD[i], (candidates[i] - c).LengthSquared);
                if (minD[i] > bestD) { bestD = minD[i]; best = i; }
            }
            if (best < 0 || bestD <= 1e-12) break;
            next = best;
        }
        return chosen;
    }
}
