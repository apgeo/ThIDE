// The cut-away of the terrain: a hole straight down through it over the cave, so that the cave is
// seen beneath it (SurfaceSpec.Cutaway). A plan holds it as lines and a radius
// (PlanCutaway): the cave's centreline, seen from above, for a footprint; the middle of the cave,
// one point, for a circle. The player works out how far every place on the ground is from the
// lines and lets the terrain's material through where that is within the radius; the mesh stays
// as it is. The hole may open while the clip runs: a change keys how much of its radius is open.
//
// The centreline is joined into polylines (a run of legs between two stations that are not on
// one passage alone, or round a loop) and thinned out to the points that matter seen from above,
// so that a large cave makes a plan of a size that still reads.

using Therion.Blender.Emit;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>Plans the cut-away of a terrain (<see cref="SurfaceSpec.Cutaway"/>).</summary>
public static class CutawayPlanner
{
    /// <summary>The colour of the rim: a dark earth.</summary>
    public static readonly ColorRgb RimColor = new(0.03, 0.025, 0.02);

    /// <summary>The narrowest rim, in metres: it is as wide as the soft edge, and at least this.</summary>
    public const double MinRimWidth = 2.0;

    /// <summary>How far, in metres, a thinned-out line may lie from the centreline it follows.</summary>
    public const double Tolerance = 0.25;

    /// <summary>
    /// The cut-away of the terrain of <paramref name="spec"/>; null when it asks for none. A
    /// footprint follows the centreline of <paramref name="framing"/> (its graph, or else its
    /// longest path); without a centreline it is a circle round the cave
    /// (<see cref="HasFootprint"/> says which).
    /// </summary>
    public static PlanCutaway? Plan(SceneSpec spec, CameraFraming framing)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(framing);
        if (spec.Surface.Cutaway is not { } cut) return null;
        var footprint = cut.Shape == CutawayShape.Footprint ? Footprint(framing) : [];
        IReadOnlyList<IReadOnlyList<CaveVector3>> lines;
        double radius;
        if (footprint.Count > 0)
        {
            lines = footprint;
            radius = cut.Margin;
        }
        else
        {
            var (centre, halfDiagonal) = Circle(framing.DrawnBounds);
            lines = [[centre]];
            radius = halfDiagonal + cut.Margin;
        }
        double edge = Math.Min(cut.Edge, radius);
        return new PlanCutaway
        {
            Lines = lines,
            Radius = Round(radius),
            Edge = Round(edge),
            Rim = cut.Rim > 0
                ? new PlanCutawayRim { Color = RimColor, Width = Round(Math.Max(MinRimWidth, edge)), Strength = Round(cut.Rim) }
                : null,
        };
    }

    /// <summary>Whether a footprint can follow the centreline of <paramref name="framing"/>; a
    /// cut-away asked for as a footprint is a circle when it cannot.</summary>
    public static bool HasFootprint(CameraFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);
        return framing.Centerline is { DistinctEdges.Count: > 0 } || framing.CenterlinePath is { Count: >= 2 };
    }

    /// <summary>
    /// The change that opens the cut-away of <paramref name="spec"/> on the model
    /// <paramref name="target"/>: shut until <see cref="CutawaySpec.OpenAt"/>, then open over
    /// <see cref="CutawaySpec.OpenSeconds"/>, eased in and out (at once when that is 0). Null
    /// when the hole is open from the start, or there is none.
    /// </summary>
    public static PlanChange? Change(SceneSpec spec, string target)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Surface.Cutaway is not { } cut || (cut.OpenAt <= 0 && cut.OpenSeconds <= 0)) return null;
        double at = Round(cut.OpenAt);
        IReadOnlyList<PlanNumberKey> keys = cut.OpenSeconds > 0
            ? [new PlanNumberKey(at, 0), new PlanNumberKey(Round(cut.OpenAt + cut.OpenSeconds), 1, PlanEasing.Smooth)]
            : [new PlanNumberKey(0, 0), new PlanNumberKey(at, 1, PlanEasing.Constant)];
        return new PlanChange { Target = new PlanChangeTarget { Id = target }, Cutaway = keys };
    }

    /// <summary>
    /// The centreline of <paramref name="framing"/> as polylines seen from above: every leg of its
    /// graph, joined into runs between the stations where passages meet or end (and round each
    /// loop), thinned out to within <see cref="Tolerance"/>. Without a graph, its longest path.
    /// Empty when it has neither.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CaveVector3>> Footprint(CameraFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);
        if (framing.Centerline is { DistinctEdges.Count: > 0 } graph)
            return Runs(graph.DistinctEdges, graph.StationPosition).Select(Thinned).ToList();
        if (framing.CenterlinePath is { Count: >= 2 } path)
            return [Thinned(path)];
        return [];
    }

    /// <summary>The middle of a box seen from above (at its middle height), and half its
    /// diagonal across the ground.</summary>
    private static (CaveVector3 Centre, double HalfDiagonal) Circle(BoundingBox box)
    {
        if (box.IsEmpty) return (CaveVector3.Zero, 0);
        var centre = new CaveVector3((box.Min.X + box.Max.X) / 2, (box.Min.Y + box.Max.Y) / 2, (box.Min.Z + box.Max.Z) / 2);
        double dx = box.Max.X - box.Min.X, dy = box.Max.Y - box.Min.Y;
        return (Round(centre), Math.Sqrt(dx * dx + dy * dy) / 2);
    }

    /// <summary>The legs joined into runs of stations: each run goes from a station where the
    /// passage does not simply go on (an end, a junction) to the next such station; a loop with
    /// none is one run round it.</summary>
    private static List<List<CaveVector3>> Runs(IReadOnlyList<(int From, int To)> edges, Func<int, CaveVector3> position)
    {
        var next = new Dictionary<int, List<(int Station, int Edge)>>();
        for (int e = 0; e < edges.Count; e++)
        {
            var (a, b) = edges[e];
            if (a == b) continue;
            Add(a, b, e);
            Add(b, a, e);
        }
        void Add(int from, int to, int edge)
        {
            if (!next.TryGetValue(from, out var list)) next[from] = list = [];
            list.Add((to, edge));
        }

        var used = new bool[edges.Count];
        var runs = new List<List<CaveVector3>>();
        void Walk(int start, int station, int edge)
        {
            var run = new List<CaveVector3> { position(start) };
            int at = start;
            while (true)
            {
                used[edge] = true;
                at = station;
                run.Add(position(at));
                if (next[at].Count != 2) break;
                int way = next[at].FindIndex(n => !used[n.Edge]);
                if (way < 0) break; // round a loop and back
                (station, edge) = next[at][way];
            }
            runs.Add(run);
        }
        // From the ends and the junctions first, in the order of the stations, so the same cave
        // gives the same lines.
        foreach (int station in next.Keys.Order())
            if (next[station].Count != 2)
                foreach (var (to, edge) in next[station])
                    if (!used[edge]) Walk(station, to, edge);
        foreach (int station in next.Keys.Order())
            foreach (var (to, edge) in next[station])
                if (!used[edge]) Walk(station, to, edge);
        return runs;
    }

    /// <summary>A polyline with only the points that matter seen from above (Douglas–Peucker over
    /// X and Y), rounded to the millimetre.</summary>
    private static IReadOnlyList<CaveVector3> Thinned(IReadOnlyList<CaveVector3> points)
    {
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            double worst = -1;
            int at = -1;
            for (int i = first + 1; i < last; i++)
            {
                double d = Across(points[i], points[first], points[last]);
                if (d > worst) (worst, at) = (d, i);
            }
            if (at < 0 || worst <= Tolerance) continue;
            keep[at] = true;
            stack.Push((first, at));
            stack.Push((at, last));
        }
        var kept = new List<CaveVector3>();
        for (int i = 0; i < points.Count; i++)
        {
            if (!keep[i]) continue;
            var point = Round(points[i]);
            if (kept.Count == 0 || kept[^1].X != point.X || kept[^1].Y != point.Y) kept.Add(point);
        }
        return kept;
    }

    /// <summary>How far <paramref name="p"/> lies from the segment a–b, across the ground.</summary>
    private static double Across(CaveVector3 p, CaveVector3 a, CaveVector3 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
        double t = length > 0 ? Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1) : 0;
        double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static double Round(double value) => Math.Round(value, 3);

    private static CaveVector3 Round(CaveVector3 v) => new(Round(v.X), Round(v.Y), Round(v.Z));
}
