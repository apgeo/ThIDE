// The terrain surface a .lox file can carry (a height grid, optionally with an aerial or map
// image draped over it), drawn above the cave. Off by default; plain, JSON-serializable data.
// The terrain may be opened by a cut-away over the cave, so that the cave is seen beneath it.

using System.Text.Json.Serialization;

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

    /// <summary>A hole through the terrain over the cave, so that the cave is seen beneath it;
    /// null is none (the default). Written only when set, so a spec without it is the JSON it
    /// was. Drawn by the player only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CutawaySpec? Cutaway { get; init; }
}

/// <summary>The shape of a terrain cut-away.</summary>
public enum CutawayShape
{
    /// <summary>The cave's plan outline: everything within <see cref="CutawaySpec.Margin"/> of
    /// its centreline, seen from above (the default).</summary>
    Footprint,
    /// <summary>A circle round the middle of the cave that holds all of it, and
    /// <see cref="CutawaySpec.Margin"/> more.</summary>
    Circle,
}

/// <summary>
/// A cut-away of the terrain: a hole straight down through it over the cave, soft at its edge,
/// with a darker rim on the ground round it so that the opening reads. The terrain's mesh stays
/// as it is; its material lets the cave through. The hole may open while the clip runs.
/// </summary>
public sealed record CutawaySpec
{
    public CutawayShape Shape { get; init; } = CutawayShape.Footprint;

    /// <summary>How far the hole reaches past the cave, in metres: from the centreline for a
    /// footprint, beyond the cave's extent for a circle.</summary>
    public double Margin { get; init; } = 15;

    /// <summary>How wide the soft edge of the hole is, in metres, inside its margin; 0 is a
    /// clean cut.</summary>
    public double Edge { get; init; } = 4;

    /// <summary>How dark the rim of the hole is, 0–1: a band of the ground round the hole, as wide
    /// as the soft edge and at least 2 m, darkened by this share. 0 is no rim.</summary>
    public double Rim { get; init; } = 0.6;

    /// <summary>When the hole starts to open, in seconds from the start of the clip.</summary>
    public double OpenAt { get; init; }

    /// <summary>How long the hole takes to open, in seconds, eased in and out, from nothing to
    /// its whole margin; 0 has it open from the start (with <see cref="OpenAt"/> 0) or at once
    /// at <see cref="OpenAt"/>.</summary>
    public double OpenSeconds { get; init; }
}
