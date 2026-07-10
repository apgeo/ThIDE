namespace Therion.Blender;

/// <summary>
/// Unified shot/leg flags. Bit values differ between formats
/// (<c>.lox</c>: 1 surface, 2 duplicate, 4 not-visible, 8 not-LRUD, 16 splay;
/// <c>.3d</c>: 1 surface, 2 duplicate, 4 splay); the file's exact bits are preserved
/// in <see cref="CaveShot.RawFlags"/>.
/// </summary>
[Flags]
public enum CaveShotFlags
{
    None = 0,
    /// <summary>Leg is above ground.</summary>
    Surface = 1,
    /// <summary>Leg duplicates data surveyed elsewhere.</summary>
    Duplicate = 2,
    /// <summary>Leg is a splay shot.</summary>
    Splay = 4,
    /// <summary>Leg is hidden in viewers (<c>.lox</c> only).</summary>
    NotVisible = 8,
    /// <summary>Leg has no usable LRUD data (<c>.lox</c> only).</summary>
    NotLrud = 16,
}

/// <summary>Cross-section shape used to skin a <c>.lox</c> shot into walls.</summary>
public enum CaveShotSection
{
    None = 0,
    Oval = 1,
    Square = 2,
    Diamond = 3,
    Tunnel = 4,
}

/// <summary>Survey style of a <c>.3d</c> leg (how the leg was measured).</summary>
public enum SurveyStyle
{
    Normal = 0,
    Diving = 1,
    Cartesian = 2,
    CylPolar = 3,
    NoSurvey = 4,
}

/// <summary>
/// Passage dimensions at a station: distances to the Left/Right/Up/Down walls in
/// metres. Values are stored exactly as the file has them; by both formats'
/// convention a negative value means "not measured".
/// </summary>
public readonly record struct CaveLrud(double Left, double Right, double Up, double Down);

/// <summary>
/// A centerline shot (survey leg). <c>.lox</c> shots reference stations by id and
/// carry LRUD/section data; <c>.3d</c> legs carry endpoint coordinates, the owning
/// survey label, style and survey dates. Fields not present in a format are null.
/// </summary>
public sealed record CaveShot
{
    /// <summary>From-station id (<c>.lox</c> only).</summary>
    public uint? FromStationId { get; init; }

    /// <summary>To-station id (<c>.lox</c> only).</summary>
    public uint? ToStationId { get; init; }

    /// <summary>Start position in world coordinates (for <c>.lox</c> resolved via the
    /// station table; default when the file references an unknown station id).</summary>
    public CaveVector3 FromPosition { get; init; }

    /// <summary>End position in world coordinates.</summary>
    public CaveVector3 ToPosition { get; init; }

    /// <summary>Owning survey id (<c>.lox</c> only).</summary>
    public uint? SurveyId { get; init; }

    /// <summary>Owning survey as a dot-separated label (<c>.3d</c> only; content of the
    /// label buffer when the leg was read).</summary>
    public string? SurveyName { get; init; }

    /// <summary>Unified typed flags (see <see cref="CaveShotFlags"/>).</summary>
    public CaveShotFlags Flags { get; init; }

    /// <summary>The flag bits exactly as stored in the source file.</summary>
    public uint RawFlags { get; init; }

    /// <summary>Wall cross-section shape (<c>.lox</c>; None for <c>.3d</c>).</summary>
    public CaveShotSection SectionType { get; init; }

    /// <summary>LRUD at the from-station (<c>.lox</c> only).</summary>
    public CaveLrud? FromLrud { get; init; }

    /// <summary>LRUD at the to-station (<c>.lox</c> only).</summary>
    public CaveLrud? ToLrud { get; init; }

    /// <summary>Vertical-threshold angle in degrees controlling LRUD orientation when
    /// walls are synthesized (<c>.lox</c> only; Therion default 60).</summary>
    public double? Threshold { get; init; }

    /// <summary>Survey style in effect for this leg (<c>.3d</c> only).</summary>
    public SurveyStyle? Style { get; init; }

    /// <summary>Survey date, or range start (<c>.3d</c> only).</summary>
    public DateOnly? DateFrom { get; init; }

    /// <summary>Survey date range end (<c>.3d</c> only; equals <see cref="DateFrom"/>
    /// for a single date).</summary>
    public DateOnly? DateTo { get; init; }
}
