// The reveal of a SceneSpec: the cave is not all there from the first frame, but appears part by
// part, in the order of the survey dates (the history of its exploration), of the survey itself,
// or of the distance from an entrance. The conversion gives every vertex of the wall mesh a
// reveal value, and the plan animates one threshold over them (ScenePlanBuilder). Drawn by the
// player only.

namespace Therion.Blender;

/// <summary>What a cave is revealed by.</summary>
public enum RevealBy
{
    /// <summary>The day each leg was surveyed. Needs survey dates, which a <c>.3d</c> file has
    /// and a <c>.lox</c> file has not (see <see cref="RevealSpec.DatesPath"/>).</summary>
    Date,

    /// <summary>The order the survey has its legs in.</summary>
    Order,

    /// <summary>The distance along the passages from the entrances, or from
    /// <see cref="RevealSpec.From"/>.</summary>
    Distance,
}

/// <summary>How a cave appears part by part while the clip runs.</summary>
public sealed record RevealSpec
{
    public RevealBy By { get; init; } = RevealBy.Distance;

    /// <summary>For <see cref="RevealBy.Distance"/>: the station the distances are measured
    /// from. Null measures from the entrances; a cave without one, from the first station of its
    /// first leg.</summary>
    public string? From { get; init; }

    /// <summary>For <see cref="RevealBy.Date"/>: a model file of the same cave that has the
    /// survey dates (a <c>.3d</c>), for a model that has none (a <c>.lox</c>). Null looks for a
    /// <c>.3d</c> of the same name beside the model.</summary>
    public string? DatesPath { get; init; }

    /// <summary>When the reveal starts, in seconds: until then nothing of the cave is there.</summary>
    public double Start { get; init; }

    /// <summary>When all of the cave is there, in seconds; null is the end of the clip.</summary>
    public double? End { get; init; }

    /// <summary>How soft the edge is, as a share of the whole range of the reveal values: a part
    /// takes this much of the reveal to appear. 0 is a cut.</summary>
    public double Edge { get; init; } = 0.03;

    /// <summary>Whether what has just appeared glows.</summary>
    public bool Glow { get; init; } = true;

    public ColorRgb GlowColor { get; init; } = new(1.0, 0.55, 0.15);

    /// <summary>For <see cref="RevealBy.Date"/>: write the year the reveal has got to over the
    /// picture.</summary>
    public bool Counter { get; init; } = true;

    /// <summary>What the counter says, with <c>{}</c> where its number goes (<c>"{} m"</c>). Set,
    /// the reveal has a counter whatever it is by: the metres from the entrance for
    /// <see cref="RevealBy.Distance"/>, the number of the leg for <see cref="RevealBy.Order"/>,
    /// the year for <see cref="RevealBy.Date"/>. Null counts the years of a reveal by date and
    /// nothing else, as before (<c>"{}"</c>).</summary>
    public string? CounterFormat { get; init; }

    /// <summary>How many decimals the counter's number has, from 0 to 6.</summary>
    public int CounterDecimals { get; init; }
}
