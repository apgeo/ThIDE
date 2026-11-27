// Everything a run found and decided, as numbers and short texts: what was guessed about the file,
// the void and skeleton, what was surveyed and why it stopped, how much is covered, which settings
// had to be relaxed where. Serialised next to the .th file; the CLI and MCP tools print it.

using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Skeleton;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey;

/// <summary>Why the planner stopped adding laterals and side stations.</summary>
public enum StopReason
{
    /// <summary>The coverage target was reached.</summary>
    TargetReached,
    /// <summary><see cref="LateralOptions.MaxCount"/> laterals were placed.</summary>
    MaxCount,
    /// <summary><see cref="LateralOptions.MaxTotalLength"/> would be exceeded.</summary>
    MaxTotalLength,
    /// <summary>The best remaining gallery was below <see cref="LateralOptions.MinEfficiency"/>.</summary>
    BelowEfficiency,
    /// <summary>No gallery passed the depth, length and width limits.</summary>
    NoCandidates,
    /// <summary>Only the guide's legs were wanted.</summary>
    GuideOnly,
}

/// <summary>A place where the settings had to be relaxed to place legs.</summary>
/// <param name="Position">Middle of the passage concerned (model coordinates).</param>
/// <param name="What">Which limit was relaxed.</param>
public sealed record RelaxationNote(CaveVector3 Position, string What);

/// <summary>A region no station sees (model coordinates), with its wall area (m²) or volume (m³).</summary>
public sealed record UncoveredRegion(CaveVector3 Position, double Amount);

public sealed record PlanReport
{
    public int Components { get; init; }
    public int Stations { get; init; }
    public int Legs { get; init; }
    public double Length { get; init; }
    public double MainlineLength { get; init; }
    public int Laterals { get; init; }
    public int LoopClosures { get; init; }
    public int SideStations { get; init; }
    public int GuideStations { get; init; }
    public int GuideLegsKept { get; init; }
    public double LegMin { get; init; }
    public double LegMedian { get; init; }
    public double LegMax { get; init; }
    public double LegMean { get; init; }

    /// <summary>Leg counts per metre of length: index i counts legs of i … i+1 m.</summary>
    public IReadOnlyList<int> LegHistogram { get; init; } = [];

    public CoverageMeasure CoverageMeasure { get; init; }
    public double CoverageTarget { get; init; }
    public double CoverageReached { get; init; }
    public double SignificantTotal { get; init; }
    public double SignificantSeen { get; init; }
    public double InsignificantTotal { get; init; }
    public double InsignificantSeen { get; init; }
    public StopReason StopReason { get; init; }
    public int BlockedLegs { get; init; }
    public int TightLegs { get; init; }
    public int Continuations { get; init; }
    public IReadOnlyList<RelaxationNote> Relaxations { get; init; } = [];
    public IReadOnlyList<GuideLegResult> GuideLegs { get; init; } = [];
    public IReadOnlyList<UncoveredRegion> Uncovered { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record SplayReport
{
    public SplayMode Mode { get; init; }
    public int Splays { get; init; }
    public int Missed { get; init; }
    public int OutOfRange { get; init; }
    public int Grazing { get; init; }
    public int Capped { get; init; }

    /// <summary>Fraction of the significant wall within two sample spacings of a splay end.</summary>
    public double WallSampled { get; init; }
}

public sealed record MeshSurveyReport
{
    public string Tool { get; init; } = "ThIDE mesh survey";
    public string Version { get; init; } = MeshSurveyInfo.Version;
    public string? Source { get; init; }
    public string? SourceSha256 { get; init; }
    public required NormalizationReport Normalization { get; init; }
    public VoidReport? Void { get; init; }
    public SkeletonReport? Skeleton { get; init; }
    public required PlanReport Plan { get; init; }
    public SplayReport? Splays { get; init; }
    public IReadOnlyDictionary<string, double> TimingsMs { get; init; } = new Dictionary<string, double>();
}

public static class MeshSurveyInfo
{
    /// <summary>The library's version, as written into every derived file.</summary>
    public static string Version { get; } =
        typeof(MeshSurveyInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
