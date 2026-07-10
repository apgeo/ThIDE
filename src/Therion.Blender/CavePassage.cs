namespace Therion.Blender;

/// <summary>
/// A passage cross-section (XSECT) at a named station from a <c>.3d</c> file:
/// distances to the Left/Right/Up/Down walls in metres, stored exactly as the file
/// has them (negative = dimension omitted, encoded as 0xffff/0xffffffff on disk).
/// </summary>
public sealed record CavePassageStation(string StationName, double Left, double Right, double Up, double Down);

/// <summary>
/// An ordered run of cross-sections forming one passage tube (<c>.3d</c> only;
/// <c>.lox</c> carries its tube data per shot instead, see
/// <see cref="CaveShot.FromLrud"/>). A passage ends at the cross-section whose
/// "last one in this passage" flag is set in the file.
/// </summary>
public sealed record CavePassage
{
    /// <summary>The cross-sections in file order.</summary>
    public required IReadOnlyList<CavePassageStation> Stations { get; init; }
}
