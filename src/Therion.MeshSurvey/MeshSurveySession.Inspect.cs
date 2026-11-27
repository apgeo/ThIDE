// A quick look at a model before the slow stage: its size in metres, what normalisation guessed, how
// many holes it has (and the biggest, the likely entrance), its loops when it is closed (from the
// Euler characteristic), and the void stage's voxel size, voxel count and memory, estimated.

using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey;

public sealed record InspectReport
{
    public required NormalizationReport Normalization { get; init; }
    public bool IsPointCloud { get; init; }
    public int Points { get; init; }
    public int Triangles { get; init; }

    /// <summary>Size of the model (metres): east, north, up.</summary>
    public required CaveVector3 Size { get; init; }

    public double SurfaceArea { get; init; }
    public double EnclosedVolume { get; init; }
    public int Holes { get; init; }
    public double LargestHoleArea { get; init; }

    /// <summary>Independent loops of the surface's parts, when the mesh is closed (no holes); null otherwise.</summary>
    public int? ClosedSurfaceLoops { get; init; }

    public double VoxelSize { get; init; }
    public long EstimatedVoxels { get; init; }
    public long EstimatedBytes { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed partial class MeshSurveySession
{
    public InspectReport Inspect(MeshSurveyOptions? options = null)
    {
        options ??= new MeshSurveyOptions();
        var b = Model.Bounds;
        double volume = VoidBuilder.EstimateVolume(Model.Mesh, b);
        double h = VoidBuilder.ChooseVoxelSize(options.Void, volume, out long voxels);
        double area = 0;
        int holes = 0;
        double largest = 0;
        int? loops = null;
        if (Model.Mesh is { } mesh)
        {
            foreach (var t in mesh.Triangles)
                area += 0.5 * (mesh.Vertices[(int)t.B] - mesh.Vertices[(int)t.A]).Cross(mesh.Vertices[(int)t.C] - mesh.Vertices[(int)t.A]).Length;
            var rims = MeshTopology.BoundaryLoops(mesh.Vertices, mesh.Triangles);
            holes = rims.Count;
            largest = rims.Count > 0 ? rims[0].Area : 0;
            if (holes == 0 && Model.Report.NonManifoldEdges == 0)
            {
                // χ = V − E + F per closed part = 2 − 2g; loops of the enclosed void = Σ g.
                var edges = new HashSet<long>(Spatial.MixedLongComparer.Instance);
                foreach (var t in mesh.Triangles)
                {
                    edges.Add(Key(t.A, t.B)); edges.Add(Key(t.B, t.C)); edges.Add(Key(t.C, t.A));
                }
                MeshTopology.TriangleComponents(mesh.Vertices.Count, mesh.Triangles, out int parts);
                long chi = mesh.Vertices.Count - edges.Count + mesh.Triangles.Count;
                loops = (int)Math.Max(0, (2L * parts - chi) / 2);
            }
        }
        return new InspectReport
        {
            Normalization = Model.Report with { Warnings = [.. ReadWarnings, .. Model.Report.Warnings] },
            IsPointCloud = Model.IsPointCloud,
            Points = Model.Mesh?.Vertices.Count ?? Model.Points?.Count ?? 0,
            Triangles = Model.Mesh?.Triangles.Count ?? 0,
            Size = b.Size,
            SurfaceArea = area,
            EnclosedVolume = volume,
            Holes = holes,
            LargestHoleArea = largest,
            ClosedSurfaceLoops = loops,
            VoxelSize = h,
            EstimatedVoxels = voxels,
            EstimatedBytes = voxels * VoidBuilder.BytesPerVoxel,
            Warnings = voxels > options.Void.MaxVoxels ? [$"The void would need about {voxels:N0} voxels, more than void.maxVoxels."] : [],
        };
    }

    private static long Key(uint a, uint b) => a < b ? ((long)a << 32) | b : ((long)b << 32) | a;
}

/// <summary>Guides made from other sources.</summary>
public static class SurveyGuides
{
    /// <summary>A guide with every station and leg of a surveyed model (.lox/.3d): its stations become
    /// guide points, its legs guide legs (splays, surface and duplicate legs left out), so the planner
    /// checks them against the scan and adds LRUD and splays. The model's coordinates must be in the
    /// same frame as the scan's model coordinates.</summary>
    public static SurveyGuide FromCaveModel(CaveModel model, GuideMode mode = GuideMode.OnlyGiven)
    {
        ArgumentNullException.ThrowIfNull(model);
        var names = new Dictionary<uint, string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var points = new List<GuidePoint>();
        GuidePoint? start = null;
        foreach (var s in model.Stations.OrderBy(s => s.Id))
        {
            string name = string.IsNullOrEmpty(s.Name) ? "s" + s.Id : s.Name;
            if (!used.Add(name)) { name = name + "#" + s.Id; used.Add(name); }
            names[s.Id] = name;
        }
        var legs = new List<GuideLeg>();
        var inLegs = new HashSet<uint>();
        const CaveShotFlags skip = CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;
        // .3d legs carry positions, not station ids: find their stations by position.
        var byPosition = new Dictionary<(long, long, long), uint>();
        foreach (var s in model.Stations.OrderBy(s => s.Id)) byPosition.TryAdd(Round(s.Position), s.Id);
        uint? StationAt(CaveVector3 p) => byPosition.TryGetValue(Round(p), out uint id) ? id : null;
        foreach (var shot in model.Shots)
        {
            if ((shot.Flags & skip) != 0) continue;
            if ((shot.FromStationId ?? StationAt(shot.FromPosition)) is not { } a || (shot.ToStationId ?? StationAt(shot.ToPosition)) is not { } b || a == b) continue;
            legs.Add(new GuideLeg { From = names[a], To = names[b] });
            inLegs.Add(a);
            inLegs.Add(b);
        }
        foreach (var s in model.Stations.OrderBy(s => s.Id))
        {
            if (!inLegs.Contains(s.Id)) continue;
            var p = new GuidePoint { Name = names[s.Id], Position = s.Position };
            if (start is null && (s.IsFixed || s.IsEntrance)) start = p;
            else points.Add(p);
        }
        if (start is null && points.Count > 0) { start = points[0]; points.RemoveAt(0); }
        return new SurveyGuide { Start = start, Points = points, Legs = legs, Mode = mode, OnBlocked = GuideBlockedPolicy.Warn };
    }

    private static (long, long, long) Round(CaveVector3 p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
}
