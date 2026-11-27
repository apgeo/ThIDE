// Coverage: how much of the cave the stations can see. The wall (or, for the volume measure, the
// void) is sampled with an area weight per sample; a sample is seen from a station within range, at a
// view less grazing than the limit, with nothing in between. Samples in shallow pockets — further from
// the passage axis than the wall is, but less than the minimum feature — are not significant: they do
// not count towards the target, so the target never demands a lateral into every crack.

using Therion.Cave3D;
using Therion.MeshSurvey.Skeleton;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Plan;

/// <summary>A uniform hash grid over points, for nearest and range queries.</summary>
internal sealed class PointIndex
{
    private readonly double _cell;
    private readonly Dictionary<(int, int, int), List<int>> _cells = [];
    private readonly IReadOnlyList<CaveVector3> _points;

    public PointIndex(IReadOnlyList<CaveVector3> points, double cell)
    {
        _points = points;
        _cell = cell;
        for (int i = 0; i < points.Count; i++)
        {
            var k = Key(points[i]);
            if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = [];
            list.Add(i);
        }
    }

    private (int, int, int) Key(CaveVector3 p) => ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));

    /// <summary>Indices of the points within <paramref name="radius"/> of <paramref name="p"/>, ascending.</summary>
    public List<int> Within(CaveVector3 p, double radius)
    {
        var result = new List<int>();
        int r = (int)Math.Ceiling(radius / _cell);
        var (cx, cy, cz) = Key(p);
        double r2 = radius * radius;
        for (int z = cz - r; z <= cz + r; z++)
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (_cells.TryGetValue((x, y, z), out var list))
                        foreach (int i in list)
                            if ((_points[i] - p).LengthSquared <= r2) result.Add(i);
        result.Sort();
        return result;
    }

    /// <summary>The nearest point (searching outwards ring by ring), or -1 when there are none.</summary>
    public int Nearest(CaveVector3 p)
    {
        if (_points.Count == 0) return -1;
        var (cx, cy, cz) = Key(p);
        int best = -1;
        double bestD = double.PositiveInfinity;
        for (int r = 0; r < 100_000; r++)
        {
            for (int z = cz - r; z <= cz + r; z++)
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz))) != r) continue;
                        if (!_cells.TryGetValue((x, y, z), out var list)) continue;
                        foreach (int i in list)
                        {
                            double d = (_points[i] - p).LengthSquared;
                            if (d < bestD || (d == bestD && i < best)) { bestD = d; best = i; }
                        }
                    }
            // Anything in a further ring is at least r·cell away.
            if (best >= 0 && Math.Sqrt(bestD) <= r * _cell) break;
            if (r >= 64)
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    double d = (_points[i] - p).LengthSquared;
                    if (d < bestD) { bestD = d; best = i; }
                }
                break;
            }
        }
        return best;
    }
}

internal sealed class CoverageModel
{
    public CaveVector3[] Points { get; }
    public CaveVector3[] Normals { get; }
    public double[] Weight { get; }
    public bool[] Significant { get; }

    /// <summary>The skeleton edge each sample belongs to (nearest axis point), or -1.</summary>
    public int[] Edge { get; }

    public int[] SeenCount { get; }

    public double SignificantTotal { get; }
    public double InsignificantTotal { get; }
    public double SampleSpacing { get; }

    private readonly MeshSurveySession _session;
    private readonly VoidGrid _grid;
    private readonly CoverageOptions _o;
    private readonly PointIndex _index;
    private readonly Dictionary<(long, long, long), int[]> _cache = [];
    private readonly double _cosLimit;
    private readonly bool _volume;

    private CoverageModel(MeshSurveySession session, VoidGrid grid, CoverageOptions o, List<CaveVector3> pts, List<CaveVector3> normals,
        List<double> weight, bool volume, double spacing, SkeletonGraph graph)
    {
        _session = session;
        _grid = grid;
        _o = o;
        _volume = volume;
        SampleSpacing = spacing;
        Points = pts.ToArray();
        Normals = normals.ToArray();
        Weight = weight.ToArray();
        SeenCount = new int[Points.Length];
        _cosLimit = Math.Cos(o.MaxIncidence * Math.PI / 180);
        _index = new PointIndex(Points, Math.Max(1.0, o.Range / 4));

        // Significance and the edge of each sample, from the nearest axis point.
        var axis = new List<CaveVector3>();
        var axisRadius = new List<double>();
        var axisEdge = new List<int>();
        foreach (var e in graph.LiveEdges)
            for (int i = 0; i < e.Points.Count; i++) { axis.Add(e.Points[i]); axisRadius.Add(e.Radii[i]); axisEdge.Add(e.Id); }
        var axisIndex = new PointIndex(axis, 1.0);
        Significant = new bool[Points.Length];
        Edge = new int[Points.Length];
        double sig = 0, insig = 0;
        for (int s = 0; s < Points.Length; s++)
        {
            int a = axisIndex.Nearest(Points[s]);
            if (a < 0) { Significant[s] = true; Edge[s] = -1; sig += Weight[s]; continue; }
            double excess = (Points[s] - axis[a]).Length - axisRadius[a];
            double tolerance = Math.Max(0.25, 0.25 * axisRadius[a]);
            bool shallowPocket = excess > tolerance && excess < o.MinFeature;
            Significant[s] = !shallowPocket;
            Edge[s] = axisEdge[a];
            if (Significant[s]) sig += Weight[s]; else insig += Weight[s];
        }
        SignificantTotal = sig;
        InsignificantTotal = insig;
    }

    public static CoverageModel Build(MeshSurveySession session, VoidGrid grid, SkeletonGraph graph, CoverageOptions o, int seed)
    {
        var random = new Random(seed);
        var pts = new List<CaveVector3>();
        var normals = new List<CaveVector3>();
        var weight = new List<double>();
        double spacing = o.SampleSpacing > 0 ? o.SampleSpacing : 0.5;
        if (o.Measure == CoverageMeasure.Volume)
        {
            double h = grid.VoxelSize;
            int stride = Math.Max(1, (int)Math.Round(spacing / h));
            while ((double)grid.Count / ((double)stride * stride * stride) > o.MaxSamples) stride++;
            spacing = stride * h;
            for (int id = 0; id < grid.Count; id++)
            {
                var (x, y, z) = grid.Coordinates(id);
                if (Mod(x, stride) != 0 || Mod(y, stride) != 0 || Mod(z, stride) != 0) continue;
                pts.Add(grid.Center(id));
                normals.Add(CaveVector3.Zero);
                weight.Add(spacing * spacing * spacing);
            }
            return new CoverageModel(session, grid, o, pts, normals, weight, true, spacing, graph);
        }
        if (session.Model.Mesh is { } mesh)
        {
            double area = 0;
            foreach (var t in mesh.Triangles)
                area += 0.5 * (mesh.Vertices[(int)t.B] - mesh.Vertices[(int)t.A]).Cross(mesh.Vertices[(int)t.C] - mesh.Vertices[(int)t.A]).Length;
            if (area / (spacing * spacing) > o.MaxSamples) spacing = Math.Sqrt(area / o.MaxSamples);
            double w = spacing * spacing;
            foreach (var t in mesh.Triangles)
            {
                var a = mesh.Vertices[(int)t.A]; var b = mesh.Vertices[(int)t.B]; var c = mesh.Vertices[(int)t.C];
                var cross = (b - a).Cross(c - a);
                double ta = 0.5 * cross.Length;
                if (ta <= 0) continue;
                double expected = ta / w;
                int count = (int)Math.Floor(expected);
                if (random.NextDouble() < expected - count) count++;
                var n = cross.Normalized();
                for (int k = 0; k < count; k++)
                {
                    double r1 = Math.Sqrt(random.NextDouble()), r2 = random.NextDouble();
                    pts.Add(a * (1 - r1) + b * (r1 * (1 - r2)) + c * (r1 * r2));
                    normals.Add(n);
                    weight.Add(w);
                }
            }
        }
        else
        {
            // Point cloud: one sample per occupied cell of the sample spacing.
            var cells = new HashSet<(long, long, long)>();
            foreach (var p in session.Model.Points ?? [])
            {
                var key = ((long)Math.Floor(p.X / spacing), (long)Math.Floor(p.Y / spacing), (long)Math.Floor(p.Z / spacing));
                if (!cells.Add(key)) continue;
                pts.Add(p);
                normals.Add(CaveVector3.Zero);
                weight.Add(spacing * spacing);
            }
        }
        return new CoverageModel(session, grid, o, pts, normals, weight, false, spacing, graph);
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>Samples visible from <paramref name="station"/> (internal frame), cached by position.</summary>
    public int[] VisibleFrom(CaveVector3 station)
    {
        var key = ((long)Math.Round(station.X * 1000), (long)Math.Round(station.Y * 1000), (long)Math.Round(station.Z * 1000));
        lock (_cache)
            if (_cache.TryGetValue(key, out var known)) return known;
        var near = _index.Within(station, _o.Range);
        var visible = new bool[near.Count];
        var bvh = _session.Bvh;
        Parallel.For(0, near.Count, i =>
        {
            int s = near[i];
            var target = Points[s];
            var d = station - target;
            double len = d.Length;
            if (len < 1e-6) { visible[i] = true; return; }
            var dir = d / len;
            if (Normals[s].LengthSquared > 0 && Math.Abs(Normals[s].Dot(dir)) < _cosLimit) return;
            if (bvh is not null && !_volume)
            {
                // From the station towards the sample, stopping just short of it.
                visible[i] = !bvh.Raycast(station, dir * -1, Math.Max(0, len - 0.02), out _);
            }
            else
            {
                visible[i] = _grid.SegmentClear(station, target, 0, 0, Math.Min(len, 2 * _grid.VoxelSize));
            }
        });
        var result = new List<int>();
        for (int i = 0; i < near.Count; i++) if (visible[i]) result.Add(near[i]);
        var array = result.ToArray();
        lock (_cache) _cache[key] = array;
        return array;
    }

    /// <summary>Significant weight newly visible from these stations (not yet seen by anything).</summary>
    public double Gain(IEnumerable<CaveVector3> stations)
    {
        var counted = new HashSet<int>();
        double gain = 0;
        foreach (var st in stations)
            foreach (int s in VisibleFrom(st))
                if (SeenCount[s] == 0 && Significant[s] && counted.Add(s)) gain += Weight[s];
        return gain;
    }

    public void Add(CaveVector3 station)
    {
        foreach (int s in VisibleFrom(station)) SeenCount[s]++;
    }

    public void Remove(CaveVector3 station)
    {
        foreach (int s in VisibleFrom(station)) SeenCount[s] = Math.Max(0, SeenCount[s] - 1);
    }

    /// <summary>Significant weight seen by this station and by no other.</summary>
    public double Unique(CaveVector3 station)
    {
        double sum = 0;
        foreach (int s in VisibleFrom(station)) if (SeenCount[s] == 1 && Significant[s]) sum += Weight[s];
        return sum;
    }

    /// <summary>Unseen significant weight on the given skeleton edges.</summary>
    public double UnseenOnEdges(ISet<int> edges)
    {
        double sum = 0;
        for (int s = 0; s < Points.Length; s++)
            if (SeenCount[s] == 0 && Significant[s] && edges.Contains(Edge[s])) sum += Weight[s];
        return sum;
    }

    public double SeenSignificant
    {
        get
        {
            double sum = 0;
            for (int s = 0; s < Points.Length; s++) if (SeenCount[s] > 0 && Significant[s]) sum += Weight[s];
            return sum;
        }
    }

    public double SeenInsignificant
    {
        get
        {
            double sum = 0;
            for (int s = 0; s < Points.Length; s++) if (SeenCount[s] > 0 && !Significant[s]) sum += Weight[s];
            return sum;
        }
    }

    public double Fraction => SignificantTotal <= 0 ? 1 : SeenSignificant / SignificantTotal;

    /// <summary>The largest unseen significant clusters (by weight), for the report.</summary>
    public List<(CaveVector3 Position, double Weight)> UncoveredRegions(int max)
    {
        var cells = new Dictionary<(int, int, int), (CaveVector3 Sum, int N, double W)>();
        double cell = Math.Max(2.0, 4 * SampleSpacing);
        for (int s = 0; s < Points.Length; s++)
        {
            if (SeenCount[s] > 0 || !Significant[s]) continue;
            var p = Points[s];
            var k = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));
            cells.TryGetValue(k, out var v);
            cells[k] = (v.Sum + p, v.N + 1, v.W + Weight[s]);
        }
        return cells.Values.OrderByDescending(v => v.W).ThenBy(v => v.Sum.X).Take(max).Select(v => (v.Sum / v.N, v.W)).ToList();
    }
}
