// Materials & lighting spec (BA-B7, FR-06) — the knobs the emitter turns into the
// generated scene's shader nodes and light rig, replacing the BA-B5 baseline-lookdev
// placeholder. Plain, JSON-serializable data added to SceneSpec. Both work on Cycles and
// EEVEE (engine-specific tuning lives in the engine section).

namespace Therion.Blender;

/// <summary>A linear RGB colour, each channel 0–1 (Blender node colours are linear).</summary>
public readonly record struct ColorRgb(double R, double G, double B)
{
    public static readonly ColorRgb RockBrown = new(0.55, 0.45, 0.38);
}

/// <summary>How the wall mesh is shaded (FR-06).</summary>
public enum RockMaterial
{
    /// <summary>Procedural noise → bump + a rock colour ramp (no data needed).</summary>
    Procedural,
    /// <summary>Classic cave depth tint: a colour ramp driven by world-Z (shader-side,
    /// normalized to the runtime bounds — needs no vertex data).</summary>
    DepthGradient,
    /// <summary>Show the mesh's baked per-vertex colours (written by the converter — the
    /// depth tint today; per-survey hues when that baking lands). Falls back to
    /// <see cref="MaterialsSpec.BaseColor"/> when the mesh has no colour attribute.</summary>
    PerSurvey,
    /// <summary>A single flat base colour.</summary>
    Flat,
}

/// <summary>Which light rig the scene uses (FR-06).</summary>
public enum LightingRig
{
    /// <summary>An area light parented to the camera — the cave-authentic head-torch look.</summary>
    Headlamp,
    /// <summary>A sun plus a Nishita procedural sky world (no assets needed).</summary>
    SunSky,
    /// <summary>Key/fill/rim sun lights — a size-independent studio setup.</summary>
    ThreePoint,
    /// <summary>An HDRI environment loaded from a user file (no bundled HDRIs).</summary>
    HdriFile,
}

/// <summary>Wall-material configuration (FR-06).</summary>
public sealed record MaterialsSpec
{
    public RockMaterial Rock { get; init; } = RockMaterial.DepthGradient;

    /// <summary>Base/flat colour and the tint procedural rock varies around.</summary>
    public ColorRgb BaseColor { get; init; } = ColorRgb.RockBrown;

    /// <summary>Principled BSDF roughness (0 = mirror, 1 = fully rough).</summary>
    public double Roughness { get; init; } = 0.9;

    /// <summary>Noise scale for <see cref="RockMaterial.Procedural"/> (larger = finer).</summary>
    public double ProceduralScale { get; init; } = 8.0;

    /// <summary>Bump strength for <see cref="RockMaterial.Procedural"/>.</summary>
    public double BumpStrength { get; init; } = 0.15;
}

/// <summary>Light-rig configuration (FR-06).</summary>
public sealed record LightingSpec
{
    public LightingRig Rig { get; init; } = LightingRig.SunSky;

    /// <summary>Multiplier applied to the rig's light energies / world strength.</summary>
    public double Strength { get; init; } = 1.0;

    /// <summary>HDRI file path; required for <see cref="LightingRig.HdriFile"/>.</summary>
    public string? HdriPath { get; init; }
}
