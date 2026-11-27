// The result of planning: stations, legs and splays placed on the model, in model coordinates, with
// what each is for (mainline, lateral, loop closure, side station, the user's own) and how each leg
// fared in its check. Writers turn it into Therion text, a preview model or a report.

using Therion.Cave3D;

namespace Therion.MeshSurvey.Plan;

public enum StationRole
{
    /// <summary>The start of the survey: the fixed station.</summary>
    Start,
    Mainline,
    Lateral,
    /// <summary>A station off the line into a niche, for its splays.</summary>
    Side,
    /// <summary>A station the user gave.</summary>
    Guide,
}

public enum LegRole
{
    Mainline,
    Lateral,
    /// <summary>A gallery joining two parts already surveyed: it closes a loop.</summary>
    LoopClosure,
    /// <summary>The single leg to a side station.</summary>
    Side,
    /// <summary>A leg the user gave.</summary>
    Guide,
}

public sealed record SurveyStation
{
    public required int Index { get; init; }

    /// <summary>The station's name in the Therion file (set when the survey is named).</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Model coordinates (metres, Z up, north applied).</summary>
    public required CaveVector3 Position { get; init; }

    public required StationRole Role { get; init; }

    /// <summary>0 = mainline, 1 = laterals off it, …</summary>
    public int Level { get; init; }

    /// <summary>The lateral (branch) the station belongs to: 0 = mainline.</summary>
    public int Branch { get; init; }

    /// <summary>The passage goes on beyond the model here.</summary>
    public bool Continuation { get; init; }

    /// <summary>Passage dimensions at the station, Therion's convention (left and right across the
    /// passage in plan, up and down vertical); negative = not measured.</summary>
    public CaveLrud? Lrud { get; internal set; }

    /// <summary>Name the user gave to a guide station.</summary>
    public string? GuideName { get; init; }
}

public sealed record SurveyLeg
{
    public required int From { get; init; }
    public required int To { get; init; }
    public required LegRole Role { get; init; }
    public int Level { get; init; }
    public int Branch { get; init; }

    /// <summary>The exact check of the leg against the model.</summary>
    public required LegCheck Check { get; init; }

    /// <summary>The user kept this leg although its check failed.</summary>
    public bool Forced { get; init; }

    /// <summary>Placed only after relaxing the settings (see the report).</summary>
    public bool Relaxed { get; init; }
}

public sealed record SurveySplay(int Station, CaveVector3 End)
{
    public double Length { get; init; }
}

/// <summary>A gallery of the survey: the mainline (branch 0) or a lateral.</summary>
public sealed record SurveyBranch
{
    public required int Index { get; init; }
    public required int Level { get; init; }

    /// <summary>The branch it starts from (-1 for the mainline).</summary>
    public int Parent { get; init; } = -1;

    /// <summary>Its stations in order, starting at the station it leaves from.</summary>
    public required IReadOnlyList<int> Stations { get; init; }

    public bool ClosesLoop { get; init; }

    public double Length { get; init; }

    /// <summary>Newly visible significant wall the branch added, m².</summary>
    public double Gain { get; init; }
}

public sealed class DerivedSurvey
{
    public required string Name { get; init; }
    public required IReadOnlyList<SurveyStation> Stations { get; init; }
    public required IReadOnlyList<SurveyLeg> Legs { get; init; }
    public required IReadOnlyList<SurveySplay> Splays { get; init; }
    public required IReadOnlyList<SurveyBranch> Branches { get; init; }

    /// <summary>The fixed station.</summary>
    public required int StartStation { get; init; }

    /// <summary>Therion coordinate system of the fix, or null for local coordinates.</summary>
    public string? CoordinateSystem { get; init; }

    public required MeshSurveyReport Report { get; init; }

    public double Length => Legs.Sum(l => l.Check.Length);
}
