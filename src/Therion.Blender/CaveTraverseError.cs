namespace Therion.Blender;

/// <summary>
/// Loop-closure error information for one traverse (a MOVE + LINE… run) from a
/// <c>.3d</c> file. E/H/V are the ratios of observed to theoretical misclosure
/// (overall / horizontal / vertical components).
/// </summary>
public sealed record CaveTraverseError
{
    /// <summary>Index into <see cref="CaveModel.Shots"/> of the first shot of the
    /// traverse this error information applies to.</summary>
    public required int ShotStartIndex { get; init; }

    /// <summary>Number of shots of that traverse actually present in
    /// <see cref="CaveModel.Shots"/>.</summary>
    public required int ShotCount { get; init; }

    /// <summary>The leg count as declared in the file.</summary>
    public required int LegCount { get; init; }

    /// <summary>Total traverse length in metres.</summary>
    public required double Length { get; init; }

    /// <summary>Overall misclosure ratio.</summary>
    public required double Error { get; init; }

    /// <summary>Horizontal misclosure ratio.</summary>
    public required double HorizontalError { get; init; }

    /// <summary>Vertical misclosure ratio.</summary>
    public required double VerticalError { get; init; }
}
