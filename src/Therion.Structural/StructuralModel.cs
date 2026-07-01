// Phase 2 — domain records + options for structural-geology detection/extraction.
//
// These consume the existing object graph (ShotSymbol/StationSymbol/SemanticModel) and feed the
// pure PlaneFitter. Selection state (which measurements are included) lives in the UI; the core only
// carries an `IncludedByDefault` seed so a headless Analyze() has a sensible first result.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;

namespace Therion.Structural;

/// <summary>How splay shots are treated when building plane batches (decision 1).</summary>
public enum SplayPolicy
{
    /// <summary>Splays are detected and listed but excluded from the fit by default.</summary>
    Exclude,
    /// <summary>Splays are included in the fit by default.</summary>
    Include,
    /// <summary>Only splay shots are considered structural; legs are ignored.</summary>
    OnlySplays,
}

/// <summary>How structural shots are grouped into one candidate plane (decision 1).</summary>
public enum GroupingMode
{
    /// <summary>Consecutive shots sharing the same <c>from</c> station (default).</summary>
    ByFromStation,
    /// <summary>All shots sharing the same detection station-flag, regardless of station.</summary>
    ByFlagParameter,
    /// <summary>All shots sharing the same comment-marker parameter (e.g. <c># plane fault-A</c>).</summary>
    ByCommentParameter,
}

/// <summary>Which signal(s) flagged a shot as structural.</summary>
[System.Flags]
public enum DetectionSignal
{
    None = 0,
    NameKeyword = 1 << 0,
    CommentMarker = 1 << 1,
    StationFlag = 1 << 2,
}

/// <summary>Where the magnetic declination value comes from (decision 6).</summary>
public enum DeclinationSource
{
    None,
    SurveyDeclared,
    WmmAuto,
    Manual,
}

/// <summary>Configuration for <see cref="GeoStructureDetector"/>.</summary>
public sealed record DetectionOptions
{
    /// <summary>Keywords matched (case-insensitive substring) against the <c>from</c>-station name. Empty disables the name signal.</summary>
    public ImmutableArray<string> NameKeywords { get; init; } = ImmutableArray.Create("geo");

    /// <summary>Enable the comment-marker signal.</summary>
    public bool MatchComment { get; init; }

    /// <summary>Comment markers (case-insensitive) whose presence flags a shot, e.g. <c>geo</c>, <c>plane</c>.</summary>
    public ImmutableArray<string> CommentMarkers { get; init; } = ImmutableArray.Create("plane", "geo");

    /// <summary>Enable the station-flag signal.</summary>
    public bool MatchStationFlag { get; init; }

    /// <summary>Station flags (case-insensitive) that designate a structural station.</summary>
    public ImmutableArray<string> StationFlags { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Splay treatment (decision 1).</summary>
    public SplayPolicy Splays { get; init; } = SplayPolicy.Exclude;

    /// <summary>Grouping strategy (decision 1).</summary>
    public GroupingMode Grouping { get; init; } = GroupingMode.ByFromStation;

    /// <summary>Default-include the synthetic <c>from</c>-station (origin) point in the fit (decision 5).</summary>
    public bool IncludeOriginPoint { get; init; }
}

/// <summary>Declination preference (decision 6).</summary>
public sealed record DeclinationOptions
{
    public DeclinationSource Source { get; init; } = DeclinationSource.None;

    /// <summary>The δ used when <see cref="Source"/> is <see cref="DeclinationSource.Manual"/> (degrees, east positive).</summary>
    public double ManualDegrees { get; init; }
}

/// <summary>
/// Pre-resolved declination values supplied by the caller (the UI/CLI computes these from the AST
/// <c>declination</c> command and from <c>GeoMagneticModel</c> + the fix point + survey date — the
/// core stays free of file IO and coordinate-system parsing).
/// </summary>
public readonly record struct DeclinationInputs(
    double? SurveyDeclaredDegrees = null,
    double? WmmAutoDegrees = null,
    string? WmmNote = null);

/// <summary>The declination actually applied, after resolving <see cref="DeclinationOptions"/>.</summary>
public readonly record struct DeclinationResolution(double Delta, DeclinationSource Effective, string? Note);

/// <summary>The full option bag for <see cref="StructuralAnalysis.Analyze"/>.</summary>
public sealed record StructuralOptions
{
    public DetectionOptions Detection { get; init; } = new();
    public DeclinationOptions Declination { get; init; } = new();
    public DeclinationInputs DeclinationInputs { get; init; } = default;
}

/// <summary>One detected measurement point feeding a plane fit.</summary>
public sealed record StructuralMeasurement
{
    /// <summary>The source shot, or null for the synthetic origin row.</summary>
    public ShotSymbol? Shot { get; init; }

    public QualifiedName From { get; init; }
    public QualifiedName To { get; init; }
    public double? Length { get; init; }
    public double? Compass { get; init; }
    public double? Clino { get; init; }

    /// <summary>The point relative to the batch's <c>from</c>-station origin (the shot vector).</summary>
    public Vec3 Local { get; init; }

    /// <summary>Absolute world position (from the centreline solve), when the <c>from</c>-station is placed.</summary>
    public Vec3? World { get; init; }

    public bool IsSplay { get; init; }

    /// <summary>True for the synthetic from-station point (decision 5).</summary>
    public bool IsOrigin { get; init; }

    /// <summary>Seed for the UI include checkbox: legs on, splays per policy, origin per option.</summary>
    public bool IncludedByDefault { get; init; } = true;

    public DetectionSignal MatchedBy { get; init; }
    public string? Comment { get; init; }
    public string? SourceFile { get; init; }
    public int Line { get; init; }
    public SourceSpan Span { get; init; } = SourceSpan.None;
}

/// <summary>A group of measurements forming one candidate geological plane.</summary>
public sealed record StructuralBatch
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? SourceFile { get; init; }
    public GroupingMode Grouping { get; init; }
    public ImmutableArray<StructuralMeasurement> Measurements { get; init; } = ImmutableArray<StructuralMeasurement>.Empty;

    /// <summary>The measurements whose <see cref="StructuralMeasurement.IncludedByDefault"/> is set.</summary>
    public ImmutableArray<StructuralMeasurement> DefaultIncluded()
    {
        var b = ImmutableArray.CreateBuilder<StructuralMeasurement>();
        foreach (var m in Measurements) if (m.IncludedByDefault) b.Add(m);
        return b.ToImmutable();
    }
}

/// <summary>Result of a full structural analysis run.</summary>
public sealed record AnalysisResult(
    ImmutableArray<StructuralBatch> Batches,
    ImmutableArray<FittedPlane> Planes,
    ImmutableArray<(Vec3 A, Vec3 B)> CaveLegs,
    DeclinationResolution Declination);
