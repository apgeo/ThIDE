// A set of 3-D points read from a model file that has no faces (an XYZ/PTS list, a vertex-only
// PLY or OBJ, a points-only glb), with optional per-point normals and colours.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>A cloud of points with optional per-point normals and colours, in the file's own
/// coordinates and units.</summary>
public sealed record CavePointCloud
{
    /// <summary>The points.</summary>
    public required IReadOnlyList<CaveVector3> Points { get; init; }

    /// <summary>Per-point normals, one per point, or null when the file has none.</summary>
    public IReadOnlyList<CaveVector3>? Normals { get; init; }

    /// <summary>Per-point colours, one per point, or null when the file has none.</summary>
    public IReadOnlyList<CaveColor>? Colors { get; init; }

    /// <summary>The empty cloud.</summary>
    public static readonly CavePointCloud Empty = new() { Points = [] };

    /// <summary>True when there are no points.</summary>
    public bool IsEmpty => Points.Count == 0;

    /// <summary>The axis-aligned bounds of the points.</summary>
    public BoundingBox Bounds => BoundingBox.FromPoints(Points);
}
