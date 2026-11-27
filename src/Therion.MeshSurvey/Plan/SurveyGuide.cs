// What the user gives the planner: optionally a start point, optionally named points the survey must
// pass through, optionally legs between them — anything from nothing (fully automatic) to every leg
// (the planner then only checks them and adds LRUD and splays). JSON-readable as is.

using Therion.Cave3D;

namespace Therion.MeshSurvey.Plan;

/// <summary>How a guide point is placed.</summary>
public enum GuideSnap
{
    /// <summary>As given; it must be in the void.</summary>
    None,
    /// <summary>Moved to the nearest point of the passage axis.</summary>
    Axis,
    /// <summary>Moved to the floor below its passage axis.</summary>
    Floor,
    /// <summary>Moved to the nearest wall.</summary>
    Wall,
    /// <summary>A point picked on the wall, moved into the void along the wall's normal.</summary>
    Surface,
}

/// <summary>The coordinates guide points are given in.</summary>
public enum GuideFrame
{
    /// <summary>Model coordinates: metres, Z up, north applied (what the outputs use).</summary>
    Model,
    /// <summary>The model file's own coordinates (as another viewer shows them).</summary>
    Source,
}

/// <summary>How much the planner adds to the guide.</summary>
public enum GuideMode
{
    /// <summary>Everything the guide does not give is placed automatically.</summary>
    Complete,
    /// <summary>Only the guide's legs: checked, with LRUD and splays, nothing added.</summary>
    OnlyGiven,
}

/// <summary>What happens to a guide leg that fails its check.</summary>
public enum GuideBlockedPolicy
{
    /// <summary>Stop and report every failing leg.</summary>
    Error,
    /// <summary>Keep it, flagged in the report and in the file.</summary>
    Warn,
    /// <summary>Leave it out.</summary>
    Drop,
}

public sealed record GuidePoint
{
    public required string Name { get; init; }
    public required CaveVector3 Position { get; init; }
    public GuideSnap Snap { get; init; } = GuideSnap.None;
}

public sealed record GuideLeg
{
    public required string From { get; init; }
    public required string To { get; init; }

    /// <summary>Keep the leg even if it fails its check (a model artefact the user knows about).</summary>
    public bool Force { get; init; }
}

public sealed record SurveyGuide
{
    public static readonly SurveyGuide None = new();

    /// <summary>The start (fixed) station; null = found automatically.</summary>
    public GuidePoint? Start { get; init; }

    public IReadOnlyList<GuidePoint> Points { get; init; } = [];

    public IReadOnlyList<GuideLeg> Legs { get; init; } = [];

    public GuideMode Mode { get; init; } = GuideMode.Complete;

    public GuideBlockedPolicy OnBlocked { get; init; } = GuideBlockedPolicy.Error;

    public GuideFrame Frame { get; init; } = GuideFrame.Model;

    public bool IsEmpty => Start is null && Points.Count == 0 && Legs.Count == 0;

    /// <summary>Every point: the start first, then the others in order.</summary>
    public IEnumerable<GuidePoint> AllPoints => Start is null ? Points : Points.Prepend(Start);

    /// <summary>Problems in the guide itself (names), as messages; empty when it is consistent.</summary>
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in AllPoints)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) issues.Add("A guide point has no name.");
            else if (!names.Add(p.Name)) issues.Add($"Guide point '{p.Name}' is given twice.");
        }
        foreach (var l in Legs)
        {
            if (!names.Contains(l.From)) issues.Add($"Guide leg {l.From}–{l.To}: no point '{l.From}'.");
            if (!names.Contains(l.To)) issues.Add($"Guide leg {l.From}–{l.To}: no point '{l.To}'.");
            if (l.From == l.To) issues.Add($"Guide leg {l.From}–{l.To} goes nowhere.");
        }
        if (Mode == GuideMode.OnlyGiven && Legs.Count == 0) issues.Add("Mode onlyGiven needs guide legs.");
        return issues;
    }
}

/// <summary>A guide leg's verdict, for the report.</summary>
public sealed record GuideLegResult(string From, string To, LegCheck Check, bool Kept, bool Forced);
