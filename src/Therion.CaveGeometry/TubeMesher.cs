// LRUD tube synthesis for centerline sources (BA-B3).
//
// topodroid's ExportSTL is only a facet WRITER; its wall model lives in a heavyweight
// convex-hull package that isn't portable. Instead we synthesize the standard
// cave-survey "LRUD tube": each leg becomes a tube whose cross-section passes exactly
// through the Left/Right/Up/Down wall distances, in the leg's own frame (no twist
// propagation). Where legs meet at a station their tubes are joined into one skin
// (TubeJoints), and a tube is capped only where the passage ends, so nothing stands across
// a passage at a station. Robust and deterministic.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>Builds a triangle mesh by skinning survey legs with LRUD cross-sections.</summary>
public static class TubeMesher
{
    /// <summary>
    /// Skins the non-auxiliary legs of <paramref name="shots"/> into tubes, joined where
    /// they meet (<see cref="GeometryOptions.JoinTubes"/>). Positions come straight from the
    /// shots (already recentered by the caller when requested). Legs shorter than a
    /// millimetre are skipped.
    /// </summary>
    public static CaveMesh Build(IReadOnlyList<CaveShot> shots, GeometryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return BuildFromLegs(Legs(shots, options), options);
    }

    /// <summary>
    /// The tube each non-auxiliary leg of <paramref name="shots"/> is skinned into: the
    /// cross-sections at its two ends, with unmeasured wall distances already replaced by
    /// <see cref="GeometryOptions.DefaultTubeRadius"/>. In shot order; splay, surface and
    /// duplicate legs and legs shorter than a millimetre have none.
    /// </summary>
    public static IReadOnlyList<TubeLeg> Legs(IReadOnlyList<CaveShot> shots, GeometryOptions options)
    {
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(options);
        int sides = Math.Max(3, options.TubeSides);

        var legs = new List<TubeLeg>(shots.Count);
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
            var from = Resolve(shot.FromLrud, options.DefaultTubeRadius);
            var to = Resolve(shot.ToLrud, options.DefaultTubeRadius);
            legs.Add(new TubeLeg(
                new TubeSection(a, right, up, from.L, from.R, from.U, from.D),
                new TubeSection(b, right, up, to.L, to.R, to.U, to.D),
                sides));
        }
        return legs;
    }

    /// <summary>
    /// Meshes <paramref name="legs"/> (from <see cref="Legs"/>): for each, the walls between
    /// the ring of vertices at its start and the one at its end. With
    /// <see cref="GeometryOptions.JoinTubes"/> the rings at a station where legs meet are
    /// joined: two legs share one ring, three or more end in open rims cut to fit each other
    /// (drawn with more vertices than the cross-section has corners, as is then the ring at the
    /// other end of each of those legs). <see cref="GeometryOptions.CapTubes"/> closes a tube
    /// only where the passage ends. Without <see cref="GeometryOptions.JoinTubes"/> every leg
    /// is a tube of its own, capped at both ends. Legs meet where their stations are equal to
    /// the last digit. Triangles are in leg order: a leg's walls, then its caps.
    /// </summary>
    public static CaveMesh BuildFromLegs(IReadOnlyList<TubeLeg> legs, GeometryOptions options)
    {
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentNullException.ThrowIfNull(options);

        var vertices = new List<CaveVector3>();
        var triangles = new List<CaveTriangle>();
        var ends = options.JoinTubes ? TubeJoints.Plan(legs) : null;
        var written = new uint[legs.Count * 2][]; // the vertices of each ring, once it has them

        // The vertices of ring `end` (2 i for the start of leg i, 2 i + 1 for its end): its
        // own, or those of the ring it shares, which belongs to an earlier leg.
        uint[] Points(int end, TubeSection section, int sides)
        {
            uint[] points;
            if (ends is not null && ends[end].Corners is { } shared)
            {
                var owner = written[ends[end].Owner];
                points = new uint[sides];
                for (int s = 0; s < sides; s++) points[s] = owner[shared[s]];
            }
            else
            {
                uint first = (uint)vertices.Count;
                if (ends is not null && ends[end].Ring is { } ring) vertices.AddRange(ring);
                else AppendRing(vertices, section, sides);
                points = new uint[vertices.Count - (int)first];
                for (int p = 0; p < points.Length; p++) points[p] = first + (uint)p;
            }
            return written[end] = points;
        }

        // A ring drawn with `steps` points to a side: the points it has, or its corners with
        // new vertices on the straight sides between them.
        uint[] Stepped(uint[] ring, int sides, int steps)
        {
            if (ring.Length == sides * steps) return ring;
            var points = new uint[sides * steps];
            for (int s = 0; s < sides; s++)
            {
                var from = vertices[(int)ring[s]];
                var to = vertices[(int)ring[(s + 1) % sides]];
                points[s * steps] = ring[s];
                for (int step = 1; step < steps; step++)
                {
                    points[s * steps + step] = (uint)vertices.Count;
                    vertices.Add(from.Lerp(to, (double)step / steps));
                }
            }
            return points;
        }

        bool Closed(int end) => ends is null || ends[end].Kind == TubeEndKind.Closed;

        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            int sides = Math.Max(3, leg.Sides);
            var ringA = Points(2 * i, leg.From, sides);
            var ringB = Points(2 * i + 1, leg.To, sides);

            // An open rim has several points to a side; the ring at the leg's other end is
            // then drawn with as many, so that every wall line runs from point to point.
            int steps = Math.Max(ringA.Length, ringB.Length) / sides;
            ringA = Stepped(ringA, sides, steps);
            ringB = Stepped(ringB, sides, steps);

            // Side quads between the two rings (two triangles each).
            for (int p = 0; p < ringA.Length; p++)
            {
                int next = (p + 1) % ringA.Length;
                uint a0 = ringA[p], a1 = ringA[next];
                uint b0 = ringB[p], b1 = ringB[next];
                triangles.Add(new CaveTriangle(a0, b0, b1));
                triangles.Add(new CaveTriangle(a0, b1, a1));
            }

            if (options.CapTubes)
            {
                if (Closed(2 * i)) AppendCap(vertices, triangles, leg.From, sides, facingForward: false);
                if (Closed(2 * i + 1)) AppendCap(vertices, triangles, leg.To, sides, facingForward: true);
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

    /// <summary>The corners of a cross-section drawn with <paramref name="sides"/> sides:
    /// counter-clockwise seen from behind the leg, the first on the right wall.</summary>
    internal static CaveVector3[] Ring(TubeSection section, int sides)
    {
        var ring = new CaveVector3[sides];
        for (int s = 0; s < sides; s++)
        {
            var (horizontal, vertical) = section.OutlineAt(2.0 * Math.PI * s / sides);
            ring[s] = section.Station + section.RightAxis * horizontal + section.UpAxis * vertical;
        }
        return ring;
    }

    private static void AppendRing(List<CaveVector3> vertices, TubeSection section, int sides)
        => vertices.AddRange(Ring(section, sides));

    private static void AppendCap(
        List<CaveVector3> vertices, List<CaveTriangle> triangles, TubeSection section, int sides, bool facingForward)
    {
        uint centerIndex = (uint)vertices.Count;
        vertices.Add(section.Station);
        uint ring = (uint)vertices.Count;
        AppendRing(vertices, section, sides);
        for (int s = 0; s < sides; s++)
        {
            uint v0 = ring + (uint)s;
            uint v1 = ring + (uint)((s + 1) % sides);
            // Wind the two end caps oppositely so both face outward: the ring runs
            // counter-clockwise seen from behind the leg (right × up = −direction), so the
            // forward cap needs (centre, v1, v0) for a +direction normal and the back cap
            // (centre, v0, v1).
            triangles.Add(facingForward
                ? new CaveTriangle(centerIndex, v1, v0)
                : new CaveTriangle(centerIndex, v0, v1));
        }
    }
}
