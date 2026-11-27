// The skeleton stage in order: thin the void, build the graph, prune spurs, mark the ends left at
// holes in the model (the scan goes no further: a continuation), merge close junctions, turn chambers
// into their policy's structure, smooth. Reports what each step did, and the loop counts that must agree.

using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Skeleton;

/// <summary>What the skeleton stage found.</summary>
public sealed record SkeletonReport
{
    public int SkeletonVoxels { get; init; }
    public int RawNodes { get; init; }
    public int RawEdges { get; init; }
    /// <summary>Loops of the thinned skeleton; equals the void's loops when thinning is right.</summary>
    public int RawLoops { get; init; }
    public int SpursPruned { get; init; }
    public int JunctionsMerged { get; init; }
    public IReadOnlyList<ChamberInfo> Chambers { get; init; } = [];
    public int Nodes { get; init; }
    public int Edges { get; init; }
    public int Ends { get; init; }
    public int Junctions { get; init; }
    public int OpenEnds { get; init; }
    /// <summary>Loops after chambers replaced their insides.</summary>
    public int Loops { get; init; }
    public double Length { get; init; }
}

/// <summary>The skeleton stage's result.</summary>
public sealed record SkeletonResult(SkeletonGraph Graph, IReadOnlyList<Pocket> Pockets, SkeletonReport Report);

internal static class SkeletonBuilder
{
    public static SkeletonResult Build(VoidGrid grid, NormalizedModel model, SkeletonOptions o, IReadOnlyList<CaveVector3> protectedPoints,
        IProgress<MeshSurveyProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new MeshSurveyProgress("skeleton", 0));
        var alive = Thinning.Run(grid, ct);
        int voxels = alive.Count(a => a);
        progress?.Report(new MeshSurveyProgress("skeleton", 0.5));
        var g = SkeletonGraph.FromVoxels(grid, alive);
        int rawNodes = g.LiveNodes.Count(), rawEdges = g.LiveEdges.Count(), rawLoops = g.CycleRank;
        SkeletonCleaner.DissolveDegreeTwo(g);

        // Protect the end (or node) nearest each protected point (entrance, guide station).
        foreach (var p in protectedPoints)
        {
            var nearest = g.LiveNodes.Where(n => n.Kind == SkeletonNodeKind.End).OrderBy(n => (n.Position - p).Length).FirstOrDefault();
            if (nearest is not null && (nearest.Position - p).Length < Math.Max(2.0, 2 * nearest.Radius)) nearest.Protected = true;
        }
        var pockets = SkeletonCleaner.Prune(g, o);
        int openEnds = MarkOpenEnds(g, model, grid.VoxelSize);
        int merged = SkeletonCleaner.MergeJunctions(g, o);
        progress?.Report(new MeshSurveyProgress("skeleton", 0.7));
        var chambers = Chambers.Apply(g, grid, o, ct);
        SkeletonCleaner.Smooth(g, grid, o.Smoothing);
        progress?.Report(new MeshSurveyProgress("skeleton", 1));

        var nodes = g.LiveNodes.ToList();
        return new SkeletonResult(g, pockets, new SkeletonReport
        {
            SkeletonVoxels = voxels,
            RawNodes = rawNodes,
            RawEdges = rawEdges,
            RawLoops = rawLoops,
            SpursPruned = pockets.Count,
            JunctionsMerged = merged,
            Chambers = chambers,
            Nodes = nodes.Count,
            Edges = g.LiveEdges.Count(),
            Ends = nodes.Count(n => n.Degree == 1),
            Junctions = nodes.Count(n => n.Degree >= 3),
            OpenEnds = nodes.Count(n => n.OpenEnd && n.Degree == 1),
            Loops = g.CycleRank,
            Length = g.TotalLength,
        });
    }

    /// <summary>Ends within reach of a hole's rim are open: the scan stops, the passage may go on.</summary>
    private static int MarkOpenEnds(SkeletonGraph g, NormalizedModel model, double h)
    {
        if (model.Mesh is not { } mesh) return 0;
        var loops = MeshTopology.BoundaryLoops(mesh.Vertices, mesh.Triangles);
        if (loops.Count == 0) return 0;
        var rim = new List<CaveVector3>();
        foreach (var loop in loops) foreach (int v in loop.Vertices) rim.Add(mesh.Vertices[v]);
        var tree = new CaveGeometry.KdTree(rim);
        int count = 0;
        foreach (var n in g.LiveNodes)
        {
            if (n.Kind != SkeletonNodeKind.End || n.Degree != 1) continue;
            if (tree.NearestDistance(n.Position) <= n.Radius + 3 * h + 0.5) { n.OpenEnd = true; count++; }
        }
        return count;
    }
}
