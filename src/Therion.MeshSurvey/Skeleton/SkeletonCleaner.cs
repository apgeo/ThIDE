// Cleaning the raw skeleton: every bump in a wall leaves a short spur, junctions in a wide passage
// come in pairs a few decimetres apart, and nodes with two edges are just points on a passage. This
// prunes the spurs that do not reach far enough beyond the passage they leave (relative to their own
// width), merges close junctions, dissolves degree-two nodes, and smooths the axes.

using Therion.Cave3D;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Skeleton;

/// <summary>A side spur pruned from the skeleton: a pocket or alcove, kept for the coverage stage.</summary>
/// <param name="Position">The end of the spur (internal frame).</param>
/// <param name="Depth">How far it reached beyond the passage it leaves.</param>
/// <param name="Radius">Its median radius.</param>
public readonly record struct Pocket(CaveVector3 Position, double Depth, double Radius);

internal static class SkeletonCleaner
{
    public static List<Pocket> Prune(SkeletonGraph g, SkeletonOptions o)
    {
        var pockets = new List<Pocket>();
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 1000)
        {
            changed = false;
            var spurs = new List<(SkeletonEdge Edge, SkeletonNode End, double Protrusion, double Radius)>();
            foreach (var e in g.LiveEdges)
            {
                if (e.A == e.B) continue;
                SkeletonNode? end = null, junction = null;
                if (IsEnd(e.A) && e.B.Degree >= 3) { end = e.A; junction = e.B; }
                else if (IsEnd(e.B) && e.A.Degree >= 3) { end = e.B; junction = e.A; }
                if (end is null || junction is null || end.Protected) continue;
                double protrusion = e.Length - junction.Radius;
                double radius = BeyondRadius(e, junction);
                spurs.Add((e, end, protrusion, radius));
            }
            foreach (var (e, end, protrusion, radius) in spurs.OrderBy(s => s.Protrusion).ThenBy(s => s.Edge.Id))
            {
                if (e.Removed) continue;
                var junction = e.Other(end);
                if (junction.Degree < 3) continue; // an earlier removal made this the passage itself
                bool keep = protrusion >= o.MinBranchLength && protrusion >= o.BranchSignificance * radius;
                if (keep) continue;
                g.RemoveEdge(e);
                end.Removed = true;
                if (protrusion > 0) pockets.Add(new Pocket(end.Position, protrusion, radius));
                changed = true;
            }
            if (DissolveDegreeTwo(g)) changed = true;
        }
        return pockets;
    }

    private static bool IsEnd(SkeletonNode n) => n.Kind == SkeletonNodeKind.End && n.Degree == 1;

    /// <summary>Median radius of the part of the edge beyond the junction's own radius.</summary>
    private static double BeyondRadius(SkeletonEdge e, SkeletonNode junction)
    {
        var values = new List<double>();
        for (int i = 0; i < e.Points.Count; i++)
            if ((e.Points[i] - junction.Position).Length > junction.Radius) values.Add(e.Radii[i]);
        if (values.Count == 0) values.AddRange(e.Radii);
        values.Sort();
        return values.Count == 0 ? 0 : values[values.Count / 2];
    }

    /// <summary>Contracts edges shorter than the merge distance between two junctions.</summary>
    public static int MergeJunctions(SkeletonGraph g, SkeletonOptions o)
    {
        int merged = 0;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var e in g.LiveEdges.OrderBy(x => x.Length).ThenBy(x => x.Id).ToList())
            {
                if (e.Removed || e.A == e.B) continue;
                if (e.A.Degree < 3 || e.B.Degree < 3) continue;
                if (e.A.Kind != SkeletonNodeKind.Junction || e.B.Kind != SkeletonNodeKind.Junction) continue;
                if (e.A.Protected && e.B.Protected) continue;
                double limit = o.JunctionMergeDistance > 0 ? o.JunctionMergeDistance : 0.75 * Math.Max(e.A.Radius, e.B.Radius);
                if (e.Length >= limit) continue;
                Contract(g, e);
                merged++;
                changed = true;
                break;
            }
        }
        if (merged > 0) DissolveDegreeTwo(g);
        return merged;
    }

    private static void Contract(SkeletonGraph g, SkeletonEdge e)
    {
        var keep = e.A.Protected || (!e.B.Protected && e.A.Id < e.B.Id) ? e.A : e.B;
        var gone = e.Other(keep);
        g.RemoveEdge(e);
        if (!keep.Protected)
        {
            double wa = keep.Radius, wb = gone.Radius;
            keep.Position = (keep.Position * wa + gone.Position * wb) / Math.Max(1e-9, wa + wb);
        }
        keep.Radius = Math.Max(keep.Radius, gone.Radius);
        foreach (var x in gone.Edges.ToList())
        {
            if (x.Removed) continue;
            if (x.A == gone) x.A = keep;
            if (x.B == gone) x.B = keep;
            if (!keep.Edges.Contains(x)) keep.Edges.Add(x);
        }
        gone.Edges.Clear();
        gone.Removed = true;
        foreach (var x in keep.Edges)
        {
            if (x.A == keep) x.Points[0] = keep.Position;
            if (x.B == keep) x.Points[^1] = keep.Position;
        }
    }

    /// <summary>Joins the two edges at every unprotected node of degree two into one.</summary>
    public static bool DissolveDegreeTwo(SkeletonGraph g)
    {
        bool any = false;
        foreach (var n in g.Nodes.ToList())
        {
            if (n.Removed || n.Protected) continue;
            if (n.Kind is SkeletonNodeKind.Chamber or SkeletonNodeKind.Portal or SkeletonNodeKind.Guide) continue;
            var live = n.Edges.Where(e => !e.Removed).ToList();
            if (live.Count != 2 || live[0].A == live[0].B || live[1].A == live[1].B) continue;
            var e1 = live[0];
            var e2 = live[1];
            // e1: other1 → n, e2: n → other2.
            var p1 = e1.B == n ? e1.Points : Enumerable.Reverse(e1.Points).ToList();
            var r1 = e1.B == n ? e1.Radii : Enumerable.Reverse(e1.Radii).ToList();
            var p2 = e2.A == n ? e2.Points : Enumerable.Reverse(e2.Points).ToList();
            var r2 = e2.A == n ? e2.Radii : Enumerable.Reverse(e2.Radii).ToList();
            var a = e1.Other(n);
            var b = e2.Other(n);
            var points = new List<CaveVector3>(p1);
            points.AddRange(p2.Skip(1));
            var radii = new List<double>(r1);
            radii.AddRange(r2.Skip(1));
            g.RemoveEdge(e1);
            g.RemoveEdge(e2);
            n.Removed = true;
            g.AddEdge(a, b, points, radii, e1.Synthetic && e2.Synthetic);
            any = true;
        }
        return any;
    }

    /// <summary>Smooths each edge's axis (endpoints fixed), keeping every point where the void is at
    /// least as roomy as it was, then re-reads the radii from the void.</summary>
    public static void Smooth(SkeletonGraph g, VoidGrid grid, double strength)
    {
        int iterations = (int)Math.Round(3 * strength);
        foreach (var e in g.LiveEdges)
        {
            var pts = e.Points;
            for (int it = 0; it < iterations; it++)
            {
                var next = new List<CaveVector3>(pts);
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    var p = (pts[i - 1] + pts[i] * 2 + pts[i + 1]) / 4.0;
                    double before = grid.DistanceAt(pts[i]);
                    if (grid.DistanceAt(p) >= Math.Min(before, 0.8 * before + 0.1)) next[i] = p;
                }
                pts = next;
            }
            e.Points = pts;
            e.Radii = pts.Select(p => Math.Max(grid.DistanceAt(p), 0.5 * grid.VoxelSize)).ToList();
            e.Radii[0] = Math.Max(e.Radii[0], 0.5 * grid.VoxelSize);
        }
    }
}
