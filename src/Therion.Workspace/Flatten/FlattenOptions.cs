// Options of the single-file export: a whole project — every file its configuration sources, every
// survey in them — written as one survey holding one centreline.

namespace Therion.Workspace.Flatten;

/// <summary>How the stations of the merged centreline are named.</summary>
public enum FlattenStationNaming
{
    /// <summary>
    /// Every station keeps its own name. Where two surveys use the same name for different stations,
    /// those stations get the name of their survey in front (<c>sump_12</c>), with as many enclosing
    /// surveys as it takes to tell them apart.
    /// </summary>
    Keep,

    /// <summary>Every station gets the name of its survey in front (<c>sump_12</c>), as many enclosing surveys as it takes to be unique.</summary>
    SurveyPrefix,

    /// <summary>The stations are numbered 1, 2, 3 … in the order the project first mentions them.</summary>
    Sequential,
}

/// <summary>Options of <see cref="SurveyFlattener"/>.</summary>
public sealed record FlattenOptions
{
    /// <summary>The id of the survey written; null takes the project's single top-level survey, else the entry file's name.</summary>
    public string? SurveyName { get; init; }

    /// <summary>The <c>-title</c> of the survey written; null takes the top-level survey's title, if any.</summary>
    public string? Title { get; init; }

    /// <summary>How stations are named (see <see cref="FlattenStationNaming"/>).</summary>
    public FlattenStationNaming Naming { get; init; } = FlattenStationNaming.Keep;

    /// <summary>Text put in front of every station name (after <see cref="Naming"/> chose it), e.g. <c>A</c> or <c>cave1_</c>.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>What joins a survey name to a station name when one is put in front of the other.</summary>
    public string Separator { get; init; } = "_";

    /// <summary>
    /// The share of each station's splays to keep, 0–100. Below 100 the splays kept are the ones that
    /// best cover the passage around the station (farthest from each other), not the first ones.
    /// </summary>
    public double SplayKeepPercent { get; init; } = 100.0;

    /// <summary>At most this many splays per station; null for no limit. Applied together with <see cref="SplayKeepPercent"/>.</summary>
    public int? MaxSplaysPerStation { get; init; }

    /// <summary>
    /// Write everything the file needs to stand alone: the coordinate system and fixed points, the
    /// entrances and the other station comments and flags, station marks, the dates and the team.
    /// Without it the file holds the measurements only (shots, splays, passage dimensions, shot flags
    /// and extended-elevation directions).
    /// </summary>
    public bool Standalone { get; init; }

    /// <summary>
    /// Add the magnetic declination of each shot to its compass reading, as Therion would — the
    /// declination the survey states, else the one of its date (IGRF) at the cave's position — and
    /// write <c>declination 0</c>, so the merged centreline needs no date to come out the same. Off
    /// leaves the compass readings as surveyed, with no declination at all.
    /// </summary>
    public bool ApplyDeclination { get; init; } = true;

    /// <summary>
    /// Where the declination of a dated survey is worked out (WGS84 degrees). Null takes the mean
    /// position of the project's fixed points, as Therion does; needed when their coordinate system is
    /// one ThIDE cannot place on the globe.
    /// </summary>
    public (double Latitude, double Longitude)? DeclinationLocation { get; init; }

    /// <summary>Put a comment before the shots of each source centreline: its survey, file, date and declination.</summary>
    public bool SourceComments { get; init; } = true;

    /// <summary>Keep the comments written on the shots themselves.</summary>
    public bool ShotComments { get; init; } = true;

    /// <summary>Decimals written for lengths (metres).</summary>
    public int LengthDecimals { get; init; } = 3;

    /// <summary>Decimals written for angles (degrees).</summary>
    public int AngleDecimals { get; init; } = 3;

    /// <summary>The "now" written in the file's header; null leaves the date out (tests).</summary>
    public DateTimeOffset? GeneratedAt { get; init; }
}
