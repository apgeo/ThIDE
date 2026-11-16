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

    /// <summary>Join the synthesized tubes of legs that meet at a station into one skin: two
    /// legs share one ring of vertices there, on the plane that halves the bend, and three or
    /// more end in open rims cut to fit each other. Off gives every leg a tube of its own,
    /// overlapping its neighbours.</summary>
    public bool JoinTubes { get; init; } = true;

    /// <summary>Close the synthesized tubes with end caps where a passage ends: at a station
    /// with one leg, or whose legs all leave the same way. With <see cref="JoinTubes"/> off,
    /// at both ends of every leg.</summary>
    public bool CapTubes { get; init; } = true;

    /// <summary>Add per-vertex depth-tint colours to the wall mesh (<see cref="DepthRamp"/>).</summary>
    public bool DepthTint { get; init; } = true;
}
