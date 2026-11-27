// Whether a straight leg between two points stays in the passage: no crossing of the model's walls,
// enough room around it, both ends in the void, a length in range. Exact against the mesh (or, for a
// point cloud, against the scan points), so it serves both the automatic placement's final check and
// every pair of consecutive points a user chooses.

using Therion.Cave3D;

namespace Therion.MeshSurvey;

/// <summary>The verdict on a leg, worst first.</summary>
public enum LegCheckStatus
{
    /// <summary>An end lies outside the model altogether.</summary>
    EndpointOutside,
    /// <summary>An end is not in the void: in rock, or in the open air beyond an entrance.</summary>
    EndpointInRock,
    /// <summary>The leg crosses the model's walls.</summary>
    Blocked,
    /// <summary>The leg stays in the void but passes closer to a wall than the clearance.</summary>
    Tight,
    /// <summary>The leg is clear but shorter or longer than the allowed range.</summary>
    OutOfRange,
    /// <summary>The leg is clear.</summary>
    Clear,
}

/// <summary>What a leg check measures against.</summary>
public sealed record LegCheckOptions
{
    /// <summary>Least distance from the leg to a wall.</summary>
    public double Clearance { get; init; } = 0.15;

    public double MinLength { get; init; }

    public double MaxLength { get; init; } = double.PositiveInfinity;

    /// <summary>Near the start, within this distance, the clearance is not required (a station standing
    /// on the rock); null = the clearance itself.</summary>
    public double? FromAllowance { get; init; }

    /// <summary>Same at the end.</summary>
    public double? ToAllowance { get; init; }

    /// <summary>Point clouds only: a scan point closer than this to the leg blocks it.</summary>
    public double PointRadius { get; init; } = 0.05;

    public static LegCheckOptions From(LegOptions legs) => new()
    {
        Clearance = legs.Clearance,
        MinLength = legs.Min,
        MaxLength = legs.Max,
    };
}

/// <summary>The result of checking one leg. Distances along the leg are from its start.</summary>
public sealed record LegCheck
{
    public required LegCheckStatus Status { get; init; }

    public required double Length { get; init; }

    /// <summary>Least distance from the leg to a wall (outside the end allowances); +∞ when nothing was near.</summary>
    public double MinClearance { get; init; } = double.PositiveInfinity;

    /// <summary>Where along the leg the least clearance is.</summary>
    public double MinClearanceAt { get; init; }

    /// <summary>Distance to the first wall crossing, when the leg is blocked.</summary>
    public double? FirstHit { get; init; }

    /// <summary>Where the first crossing is (model coordinates).</summary>
    public CaveVector3? FirstHitPoint { get; init; }

    /// <summary>How many times the leg crosses a wall.</summary>
    public int Crossings { get; init; }

    /// <summary>Total length of the leg that is not in the void (rock it would pass through).</summary>
    public double RockThickness { get; init; }

    public bool FromInVoid { get; init; } = true;

    public bool ToInVoid { get; init; } = true;

    public bool TooShort { get; init; }

    public bool TooLong { get; init; }

    /// <summary>The leg can be surveyed (possibly with a warning): clear, tight or out of range.</summary>
    public bool IsUsable => Status is LegCheckStatus.Clear or LegCheckStatus.Tight or LegCheckStatus.OutOfRange;

    /// <summary>One line for a person: "clear", "blocked 3.20 m from the start (0.60 m of rock)" …</summary>
    public string Describe()
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return Status switch
        {
            LegCheckStatus.EndpointOutside => string.Format(c, "{0} is outside the model", !FromInVoid ? "the start" : "the end"),
            LegCheckStatus.EndpointInRock => string.Format(c, "{0} is not in the passage", !FromInVoid ? "the start" : "the end"),
            LegCheckStatus.Blocked => string.Format(c, "blocked {0:0.00} m from the start ({1} wall crossing{2}, {3:0.00} m of rock)",
                FirstHit ?? 0, Crossings, Crossings == 1 ? "" : "s", RockThickness),
            LegCheckStatus.Tight => string.Format(c, "tight: {0:0.00} m from a wall at {1:0.00} m", MinClearance, MinClearanceAt),
            LegCheckStatus.OutOfRange => string.Format(c, "clear, but {0:0.00} m is {1}", Length, TooShort ? "too short" : "too long"),
            _ => "clear",
        };
    }
}
