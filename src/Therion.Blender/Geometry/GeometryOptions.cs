namespace Therion.Blender.Geometry;

/// <summary>Which wall geometry the stage produces when a model has both scraps and
/// centerline shots (nearly always just one applies).</summary>
public enum WallSource
{
    /// <summary>Real scrap meshes if present, otherwise synthesized LRUD tubes.</summary>
    Auto,
    /// <summary>Force synthesized tubes even when scrap meshes exist.</summary>
    Tubes,
    /// <summary>Use only scrap meshes (empty walls when a model has none).</summary>
    Scraps,
}

/// <summary>Options for <see cref="GeometryStage"/>: how to recenter, skin and colour.</summary>
public sealed record GeometryOptions
{
    /// <summary>Recenter geometry to a local origin (the bbox centre) so UTM-scale
    /// coordinates survive float32 (R-03/D-15). Off leaves world coordinates as-is.</summary>
    public bool Recenter { get; init; } = true;

    /// <summary>Where wall geometry comes from.</summary>
    public WallSource WallSource { get; init; } = WallSource.Auto;

    /// <summary>Cross-section sides for synthesized tubes (≥ 3). The four LRUD extremes
    /// are always hit exactly regardless of this count.</summary>
    public int TubeSides { get; init; } = 8;

    /// <summary>Passage radius (metres) for legs lacking LRUD data.</summary>
    public double DefaultTubeRadius { get; init; } = 1.0;

    /// <summary>Close each synthesized tube segment with end caps (watertight-ish; makes
    /// overlapping per-leg tubes read as solid).</summary>
    public bool CapTubes { get; init; } = true;

    /// <summary>Add per-vertex depth-tint colours to the wall mesh (<see cref="DepthRamp"/>).</summary>
    public bool DepthTint { get; init; } = true;
}
