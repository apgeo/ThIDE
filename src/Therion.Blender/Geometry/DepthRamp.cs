namespace Therion.Blender.Geometry;

/// <summary>
/// The classic cave depth-tint gradient: warm near the top of the cave fading to cool
/// at depth. Used for optional per-vertex mesh colours so the raw geometry already
/// reads as a cave before any material work (BA-B7 adds shader-driven versions).
/// </summary>
public static class DepthRamp
{
    // Five warm→cool stops (top → bottom). Evenly spaced over the normalized depth.
    private static readonly CaveColor[] Stops =
    [
        new(0xF2, 0xE0, 0xB0), // near surface — pale sand
        new(0xD9, 0xA8, 0x6C), // ochre
        new(0xA8, 0x6E, 0x5A), // brown rock
        new(0x5C, 0x6E, 0x9E), // slate blue
        new(0x25, 0x35, 0x66), // deep blue
    ];

    /// <summary>The depth stops top → bottom (index 0 = cave top). The shader
    /// depth-gradient material (BA-B7) reproduces these as a ColorRamp so it matches the
    /// per-vertex tint palette.</summary>
    public static IReadOnlyList<CaveColor> GradientStops => Stops;

    /// <summary>
    /// Colour for depth fraction <paramref name="t"/> in [0,1], where 1 = top of the
    /// cave (<c>maxZ</c>) and 0 = bottom (<c>minZ</c>). Out-of-range values clamp.
    /// </summary>
    public static CaveColor Sample(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        // t=1 → first stop (top), t=0 → last stop (bottom).
        double u = (1.0 - t) * (Stops.Length - 1);
        int i = (int)Math.Floor(u);
        if (i >= Stops.Length - 1) return Stops[^1];
        double f = u - i;
        return Mix(Stops[i], Stops[i + 1], f);
    }

    /// <summary>Colour for a Z value between <paramref name="minZ"/> and
    /// <paramref name="maxZ"/> (a flat cave — zero span — maps everything to mid-ramp).</summary>
    public static CaveColor SampleZ(double z, double minZ, double maxZ)
    {
        double span = maxZ - minZ;
        double t = span < 1e-9 ? 0.5 : (z - minZ) / span;
        return Sample(t);
    }

    private static CaveColor Mix(CaveColor a, CaveColor b, double f) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * f),
        (byte)Math.Round(a.G + (b.G - a.G) * f),
        (byte)Math.Round(a.B + (b.B - a.B) * f));
}
