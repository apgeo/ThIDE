using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>
/// The output of <see cref="GeometryStage"/>: the wall mesh plus everything the scene
/// and its metadata need. Coordinates in <see cref="Walls"/> and
/// <see cref="RecenteredModel"/> are local (world = local + <see cref="Offset"/>).
/// </summary>
public sealed record GeometryResult
{
    /// <summary>The triangle mesh of the cave walls (scraps or synthesized tubes), in
    /// local coordinates, optionally depth-tinted.</summary>
    public required CaveMesh Walls { get; init; }

    /// <summary>The tubes <see cref="Walls"/> was synthesized from, one per meshed leg, in
    /// local coordinates: where each leg's passage is, as cross-sections rather than
    /// triangles. Empty when the walls are real scrap meshes or there are none.</summary>
    public IReadOnlyList<TubeLeg> TubeLegs { get; init; } = [];

    /// <summary>The vector subtracted from world coordinates to recenter
    /// (world = local + Offset); the original bbox centre, or zero when recentering was
    /// disabled.</summary>
    public required CaveVector3 Offset { get; init; }

    /// <summary>Bounds of all model geometry in the original world coordinates.</summary>
    public required BoundingBox OriginalBounds { get; init; }

    /// <summary>Bounds of all model geometry after recentering (local coordinates).</summary>
    public required BoundingBox LocalBounds { get; init; }

    /// <summary>The model with station/shot/scrap coordinates recentered — the
    /// coordinate-consistent companion to <see cref="Walls"/> for labels and metadata.</summary>
    public required CaveModel RecenteredModel { get; init; }

    /// <summary>Centerline connectivity of the (recentered) model.</summary>
    public required CenterlineGraph Centerline { get; init; }

    /// <summary>The reveal values of the cave's legs, which <see cref="Walls"/> carries on its
    /// vertices; null when none were asked for (<see cref="GeometryOptions.Reveal"/>) or the
    /// model has nothing to work them out from (no legs; no survey dates).</summary>
    public RevealField? Reveal { get; init; }

    /// <summary>True when <see cref="Walls"/> carries real triangles.</summary>
    public bool HasWalls => !Walls.IsEmpty;
}
