namespace Therion.Blender;

/// <summary>
/// Unified station flags. <c>.lox</c> and <c>.3d</c> assign different bit values to
/// overlapping concepts, so parsers map both onto this shared enum; the file's exact
/// bits are preserved in <see cref="CaveStation.RawFlags"/>.
/// </summary>
[Flags]
public enum CaveStationFlags
{
    None = 0,
    /// <summary>Station is on the surface (lox bit 1, 3d bit 1).</summary>
    Surface = 1,
    /// <summary>Station is on an underground leg (3d bit 2; may combine with Surface at an entrance).</summary>
    Underground = 2,
    /// <summary>Station is a cave entrance (lox bit 2, 3d bit 4).</summary>
    Entrance = 4,
    /// <summary>Station is exported / usable as a connection point (3d bit 8).</summary>
    Exported = 8,
    /// <summary>Station is a fixed (control) point (lox bit 4, 3d bit 16).</summary>
    Fixed = 16,
    /// <summary>Station is a continuation / lead (lox bit 8).</summary>
    Continuation = 32,
    /// <summary>Station has wall (LRUD-derived) geometry attached (lox bit 16).</summary>
    HasWalls = 64,
    /// <summary>Station is anonymous (3d bit 32).</summary>
    Anonymous = 128,
    /// <summary>Station lies on the passage wall (3d bit 64).</summary>
    Wall = 256,
}

/// <summary>A survey station with position, flags and metadata.</summary>
public sealed record CaveStation
{
    /// <summary>Station id. From the file for <c>.lox</c>; a sequential index assigned in
    /// file order for <c>.3d</c> (which has no station ids).</summary>
    public required uint Id { get; init; }

    /// <summary>Owning survey id (<c>.lox</c> only; null for <c>.3d</c>).</summary>
    public uint? SurveyId { get; init; }

    /// <summary>Station name: the survey-local name for <c>.lox</c>, the full
    /// dot-separated label for <c>.3d</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Free-text comment (<c>.lox</c> only, rarely present).</summary>
    public string? Comment { get; init; }

    /// <summary>Position in the file's world coordinate system (metres).</summary>
    public CaveVector3 Position { get; init; }

    /// <summary>Unified typed flags (see <see cref="CaveStationFlags"/>).</summary>
    public CaveStationFlags Flags { get; init; }

    /// <summary>The flag bits exactly as stored in the source file (format-specific
    /// bit assignment — kept so no information is lost in the unified mapping).</summary>
    public uint RawFlags { get; init; }

    public bool IsEntrance => (Flags & CaveStationFlags.Entrance) != 0;
    public bool IsFixed => (Flags & CaveStationFlags.Fixed) != 0;
}
