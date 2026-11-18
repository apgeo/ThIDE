// The terrain surface a .lox file can carry (a height grid, optionally with an aerial or map
// image draped over it), drawn above the cave. Off by default; plain, JSON-serializable data.

namespace Therion.Blender;

/// <summary>Whether and how the terrain surface is drawn.</summary>
public enum SurfaceMode
{
    /// <summary>No terrain (the default).</summary>
    Off,
    /// <summary>The terrain in one muted earth colour.</summary>
    Solid,
    /// <summary>The terrain with the model's surface image draped over it; drawn
    /// <see cref="Solid"/>, with a warning, when the model has no surface image.</summary>
    Textured,
}

/// <summary>
/// Terrain-surface configuration. The terrain is see-through from above (its
/// <see cref="Opacity"/>) and invisible from below, so an orbit still shows the cave under it and
/// a flythrough never looks at its underside; it casts no shadows, so the sun still lights the
/// cave; and it never changes the camera framing, which stays on the cave.
/// </summary>
public sealed record SurfaceSpec
{
    public SurfaceMode Mode { get; init; } = SurfaceMode.Off;

    /// <summary>How opaque the terrain is seen from above, 0–1 (1 hides the cave under it).</summary>
    public double Opacity { get; init; } = 0.4;
}
