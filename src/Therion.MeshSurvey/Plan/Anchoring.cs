// Where a station stands, given the point of the passage axis it belongs to: on the axis, on the
// floor below it, on the roof above it, or on the nearest wall — always a couple of centimetres
// inside the void, so the station is unambiguously in the passage.

using Therion.Cave3D;

namespace Therion.MeshSurvey.Plan;

internal static class Anchoring
{
    /// <summary>The least distance an anchored station keeps from the rock.</summary>
    public const double Nudge = 0.02;

    public static CaveVector3 Place(MeshSurveySession session, CaveVector3 axis, CaveVector3 tangent, double radius, AnchorOptions o)
    {
        if (o.Mode == AnchorMode.Axial) return axis;
        var mode = o.Mode;
        double steep = Math.Sin(o.MaxFloorSlope * Math.PI / 180);
        if (mode is AnchorMode.Floor or AnchorMode.Ceiling && Math.Abs(tangent.Z) > steep) mode = AnchorMode.Wall;
        double reach = 3 * radius + 2;
        switch (mode)
        {
            case AnchorMode.Floor:
                if (Cast(session, axis, new CaveVector3(0, 0, -1), reach, out var floor))
                    return floor + new CaveVector3(0, 0, Math.Max(o.FloorOffset, Nudge));
                break;
            case AnchorMode.Ceiling:
                if (Cast(session, axis, new CaveVector3(0, 0, 1), reach, out var roof))
                    return roof - new CaveVector3(0, 0, Math.Max(o.FloorOffset, Nudge));
                break;
            case AnchorMode.Wall:
                if (NearestWall(session, axis, reach, out var wall))
                {
                    var inward = (axis - wall).Normalized();
                    return wall + inward * Math.Max(o.WallOffset, Nudge);
                }
                break;
        }
        return axis;
    }

    /// <summary>First wall hit from <paramref name="from"/> along <paramref name="direction"/>
    /// (internal frame): the mesh, or the void's edge for a point cloud.</summary>
    public static bool Cast(MeshSurveySession session, CaveVector3 from, CaveVector3 direction, double reach, out CaveVector3 hit)
    {
        if (session.Bvh is { } bvh)
        {
            if (bvh.Raycast(from, direction, reach, out var h)) { hit = h.Point; return true; }
            hit = default;
            return false;
        }
        var grid = session.PreparedVoid;
        hit = default;
        if (grid is null) return false;
        var d = direction.Normalized();
        double step = 0.5 * grid.VoxelSize;
        for (double s = step; s <= reach; s += step)
        {
            var p = from + d * s;
            if (!grid.Contains(p)) { hit = from + d * (s - step / 2); return true; }
        }
        return false;
    }

    private static bool NearestWall(MeshSurveySession session, CaveVector3 p, double reach, out CaveVector3 wall)
    {
        wall = default;
        if (session.Bvh is { } bvh)
        {
            double d = bvh.Nearest(p, reach, out wall, out _);
            return !double.IsInfinity(d);
        }
        // Point cloud: the nearest of six axis casts.
        double best = double.PositiveInfinity;
        foreach (var dir in new[] { new CaveVector3(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) })
            if (Cast(session, p, dir, reach, out var h) && (h - p).Length < best) { best = (h - p).Length; wall = h; }
        return !double.IsInfinity(best);
    }
}
