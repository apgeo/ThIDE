// LRUD tube synthesis for centerline sources (BA-B3).
//
// topodroid's ExportSTL is only a facet WRITER; its wall model lives in a heavyweight
// convex-hull package that isn't portable. Instead we synthesize the standard
// cave-survey "LRUD tube": each leg becomes an independently capped tube whose
// cross-section passes exactly through the Left/Right/Up/Down wall distances. Legs are
// meshed independently (no miter joints or twist propagation) — robust, deterministic,
// and visually equivalent to what Aven/Loch render; the small overlaps at junctions are
// invisible in a solid render.

namespace Therion.Blender.Geometry;

/// <summary>Builds a triangle mesh by skinning survey legs with LRUD cross-sections.</summary>
public static class TubeMesher
{
    /// <summary>
    /// Skins each non-auxiliary leg of <paramref name="shots"/> into a capped tube.
    /// Positions come straight from the shots (already recentered by the caller when
    /// requested). Legs shorter than a millimetre are skipped.
    /// </summary>
    public static CaveMesh Build(IReadOnlyList<CaveShot> shots, GeometryOptions options)
    {
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(options);
        int sides = Math.Max(3, options.TubeSides);

        var vertices = new List<CaveVector3>();
        var triangles = new List<CaveTriangle>();
        const CaveShotFlags skip = CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;

        foreach (var shot in shots)
        {
            if ((shot.Flags & skip) != 0) continue;
            var a = shot.FromPosition;
            var b = shot.ToPosition;
            var axis = b - a;
            double legLength = axis.Length;
            if (legLength < 1e-3) continue;

            var (right, up) = CrossSectionFrame(axis / legLength);
            var fromLrud = Resolve(shot.FromLrud, options.DefaultTubeRadius);
            var toLrud = Resolve(shot.ToLrud, options.DefaultTubeRadius);

            uint ringA = (uint)vertices.Count;
            AppendRing(vertices, a, right, up, fromLrud, sides);
            uint ringB = (uint)vertices.Count;
            AppendRing(vertices, b, right, up, toLrud, sides);

            // Side quads between the two rings (two triangles each).
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                uint a0 = ringA + (uint)s, a1 = ringA + (uint)next;
                uint b0 = ringB + (uint)s, b1 = ringB + (uint)next;
                triangles.Add(new CaveTriangle(a0, b0, b1));
                triangles.Add(new CaveTriangle(a0, b1, a1));
            }

            if (options.CapTubes)
            {
                AppendCap(vertices, triangles, a, right, up, fromLrud, sides, facingForward: false);
                AppendCap(vertices, triangles, b, right, up, toLrud, sides, facingForward: true);
            }
        }

        return new CaveMesh { Vertices = vertices, Triangles = triangles };
    }

    /// <summary>
    /// A right-handed frame perpendicular to the leg: <c>right</c> is horizontal
    /// (LRUD L/R axis), <c>up</c> lies in the cross-section plane (LRUD U/D axis).
    /// Falls back gracefully for vertical legs where the horizontal frame degenerates.
    /// </summary>
    internal static (CaveVector3 Right, CaveVector3 Up) CrossSectionFrame(CaveVector3 direction)
    {
        var right = direction.Cross(CaveVector3.UnitZ);
        if (right.LengthSquared < 1e-12)
            right = direction.Cross(new CaveVector3(1, 0, 0));
        if (right.LengthSquared < 1e-12)
            right = direction.Cross(new CaveVector3(0, 1, 0));
        right = right.Normalized();
        var up = right.Cross(direction).Normalized();
        return (right, up);
    }

    private static (double L, double R, double U, double D) Resolve(CaveLrud? lrud, double fallback)
    {
        if (lrud is not { } v) return (fallback, fallback, fallback, fallback);
        // A negative value means "not measured" in both formats — fall back per-axis.
        return (
            v.Left > 0 ? v.Left : fallback,
            v.Right > 0 ? v.Right : fallback,
            v.Up > 0 ? v.Up : fallback,
            v.Down > 0 ? v.Down : fallback);
    }

    private static void AppendRing(
        List<CaveVector3> vertices, CaveVector3 center, CaveVector3 right, CaveVector3 up,
        (double L, double R, double U, double D) lrud, int sides)
    {
        for (int s = 0; s < sides; s++)
        {
            double theta = 2.0 * Math.PI * s / sides;
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            double horizontal = cos >= 0 ? lrud.R * cos : lrud.L * cos; // cos<0 → toward left wall
            double vertical = sin >= 0 ? lrud.U * sin : lrud.D * sin;   // sin<0 → toward floor
            vertices.Add(center + right * horizontal + up * vertical);
        }
    }

    private static void AppendCap(
        List<CaveVector3> vertices, List<CaveTriangle> triangles, CaveVector3 center,
        CaveVector3 right, CaveVector3 up, (double L, double R, double U, double D) lrud,
        int sides, bool facingForward)
    {
        uint centerIndex = (uint)vertices.Count;
        vertices.Add(center);
        uint ring = (uint)vertices.Count;
        AppendRing(vertices, center, right, up, lrud, sides);
        for (int s = 0; s < sides; s++)
        {
            uint v0 = ring + (uint)s;
            uint v1 = ring + (uint)((s + 1) % sides);
            // Wind the two end caps oppositely so both face outward.
            triangles.Add(facingForward
                ? new CaveTriangle(centerIndex, v0, v1)
                : new CaveTriangle(centerIndex, v1, v0));
        }
    }
}
