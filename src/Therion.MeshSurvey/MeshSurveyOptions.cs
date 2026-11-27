// Every setting of a derived survey, as one tree of records: how the model is read and normalised,
// how the void and its skeleton are built, how the mainline and laterals are chosen, how far
// coverage goes, how legs are placed, where stations stand, how splays are cast, how readings are
// written. Defaults suit "a handheld scan as a survey backbone"; MeshSurveyPresets holds the others.
// All lengths are metres, all angles degrees.

namespace Therion.MeshSurvey;

/// <summary>The unit the model's coordinates are in. <see cref="Auto"/> guesses from the model's size.</summary>
public enum ModelLengthUnit { Auto, Metres, Centimetres, Millimetres, Feet }

/// <summary>Which model axis points up. <see cref="Auto"/> takes the file format's convention
/// (glTF: +Y, all others: +Z).</summary>
public enum ModelUpAxisChoice { Auto, Z, Y }

/// <summary>How the inside of the model is told from the outside.</summary>
public enum InsideTest
{
    /// <summary>Winding number for a mesh, flood fill for a point cloud.</summary>
    Auto,
    /// <summary>Generalised winding number of the mesh: robust to holes and to the normals' direction.</summary>
    Winding,
    /// <summary>Flood fill from inside, with walls thickened by <see cref="VoidOptions.HoleClosing"/>.</summary>
    Flood,
}

/// <summary>What becomes of a large chamber, where any skeleton is a tangle.</summary>
public enum ChamberPolicy
{
    /// <summary>One station in the middle, joined to a station at each passage entering it.</summary>
    Hub,
    /// <summary>Legs along the chamber's long axis; the passages join the nearest station on it.</summary>
    Axis,
    /// <summary>A loop round the chamber at <see cref="SkeletonOptions.PerimeterOffset"/> from the walls.</summary>
    Perimeter,
    /// <summary>The skeleton as it is (for checking what the other policies replace).</summary>
    Keep,
}

/// <summary>How the mainline is chosen.</summary>
public enum MainlineStrategy
{
    /// <summary><see cref="ThroughGuide"/> when the guide has points, else <see cref="Longest"/>.</summary>
    Auto,
    /// <summary>From the start to the end farthest along the passages.</summary>
    Longest,
    /// <summary>From the start to the end that gathers the most passage volume on the way.</summary>
    Largest,
    /// <summary>Through the guide's points in their order.</summary>
    ThroughGuide,
}

/// <summary>What is done with galleries that close a loop.</summary>
public enum LoopPolicy
{
    /// <summary>Survey them, with their gain raised by <see cref="LateralOptions.LoopBonus"/>: a loop makes a survey checkable.</summary>
    Close,
    /// <summary>Survey them only when they pay off like any other lateral.</summary>
    IfCheap,
    /// <summary>Never survey a gallery that would close a loop.</summary>
    Never,
}

/// <summary>What coverage is measured on.</summary>
public enum CoverageMeasure
{
    /// <summary>The fraction of the significant wall area visible from some station.</summary>
    WallArea,
    /// <summary>The fraction of the void visible from some station.</summary>
    Volume,
}

/// <summary>How the stations along a passage are chosen.</summary>
public enum LegStrategy
{
    /// <summary>Legs as close to <see cref="LegOptions.Preferred"/> as the passage allows.</summary>
    Preferred,
    /// <summary>As few legs as possible within <see cref="LegOptions.Max"/>.</summary>
    Fewest,
    /// <summary>Equal spacing near the preferred length; a blocked leg is split.</summary>
    Uniform,
    /// <summary>Stations only at junctions, bends, squeezes and chamber entrances; long legs split evenly.</summary>
    Features,
}

/// <summary>Where a station stands relative to its passage.</summary>
public enum AnchorMode
{
    /// <summary>On the passage axis (in the middle of the passage).</summary>
    Axial,
    /// <summary>On the floor below the axis.</summary>
    Floor,
    /// <summary>On the roof above the axis.</summary>
    Ceiling,
    /// <summary>On the nearest wall.</summary>
    Wall,
}

/// <summary>The directions splays are shot in.</summary>
public enum SplayMode
{
    None,
    /// <summary>Four shots: left and right across the passage, up and down.</summary>
    Lrud,
    /// <summary>A ring of shots across the passage, optionally one forward and one back.</summary>
    Ring,
    /// <summary>Shots spread evenly over the sphere.</summary>
    Sphere,
    /// <summary>A coarse sphere refined where neighbouring shots differ a lot (alcoves, side passages).</summary>
    Adaptive,
    /// <summary>Shots chosen so their ends are spread evenly on the visible wall.</summary>
    WallUniform,
}

/// <summary>How the legs are written in Therion.</summary>
public enum ReadingStyle
{
    /// <summary><c>data normal from to length compass clino</c>.</summary>
    Normal,
    /// <summary><c>data cartesian from to easting northing altitude</c>.</summary>
    Cartesian,
}

/// <summary>How stations are named.</summary>
public enum StationNaming
{
    /// <summary>1, 2, 3 … in survey order.</summary>
    Sequential,
    /// <summary>The mainline 1, 2, 3 …; each lateral its own sub-survey.</summary>
    Branch,
}

/// <summary>An axis-aligned box to keep (normalised model coordinates: metres, Z up, north applied).</summary>
public sealed record CropBox(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

/// <summary>How the model is read and normalised.</summary>
public sealed record InputOptions
{
    public ModelLengthUnit Units { get; init; } = ModelLengthUnit.Auto;

    public ModelUpAxisChoice UpAxis { get; init; } = ModelUpAxisChoice.Auto;

    /// <summary>Azimuth (degrees clockwise from north) the model's +Y axis points to. 0 = +Y is north.</summary>
    public double NorthRotation { get; init; }

    /// <summary>Only this part of the model is kept (after units, up axis and north are applied).</summary>
    public CropBox? Crop { get; init; }

    /// <summary>Disconnected parts of the void smaller than this (m³) are dropped.</summary>
    public double MinComponentVolume { get; init; } = 1.0;

    /// <summary>Vertices closer than this are merged; 0 = a millionth of the model's size.</summary>
    public double WeldTolerance { get; init; }

    /// <summary>Therion coordinate system of the model coordinates (e.g. "UTM34N"), written with the
    /// fix; null = local coordinates.</summary>
    public string? CoordinateSystem { get; init; }
}

/// <summary>How the void (the air inside the model) is built.</summary>
public sealed record VoidOptions
{
    /// <summary>Voxel edge; 0 = <see cref="MinPassageWidth"/>/4, clamped to 0.03–0.5 m, coarsened to fit
    /// <see cref="MaxVoxels"/>.</summary>
    public double VoxelSize { get; init; }

    /// <summary>The most voxels the void may have; the voxel size is coarsened until the estimate fits.
    /// The default keeps the void stage near 4 GB.</summary>
    public long MaxVoxels { get; init; } = 150_000_000;

    public InsideTest InsideTest { get; init; } = InsideTest.Auto;

    /// <summary>Holes in a scan narrower than twice this are sealed (flood fill only).</summary>
    public double HoleClosing { get; init; } = 0.2;

    /// <summary>Connections narrower than this are treated as rock. Every place where that cuts the void
    /// is reported.</summary>
    public double MinPassageWidth { get; init; } = 0.25;

    /// <summary>Rock thinner than this inside the void (scanned people, ropes, thin columns) is removed,
    /// so no tiny loops form round it.</summary>
    public double MinPillarSize { get; init; } = 0.4;
}

/// <summary>How the skeleton (the passages' axes) is cleaned.</summary>
public sealed record SkeletonOptions
{
    /// <summary>A side branch must reach at least this far beyond the passage it leaves.</summary>
    public double MinBranchLength { get; init; } = 1.5;

    /// <summary>…and at least this many times its own radius.</summary>
    public double BranchSignificance { get; init; } = 2.0;

    /// <summary>Junctions closer than this merge; 0 = three quarters of the larger radius.</summary>
    public double JunctionMergeDistance { get; init; }

    /// <summary>A chamber is a place whose radius is at least this…</summary>
    public double ChamberMinRadius { get; init; } = 4.0;

    /// <summary>…and at least this many times the median radius of the passages.</summary>
    public double ChamberRatio { get; init; } = 3.0;

    public ChamberPolicy ChamberPolicy { get; init; } = ChamberPolicy.Hub;

    /// <summary>Distance of the <see cref="ChamberPolicy.Perimeter"/> loop from the walls.</summary>
    public double PerimeterOffset { get; init; } = 1.0;

    /// <summary>Strength of the axis smoothing (0 = none).</summary>
    public double Smoothing { get; init; } = 1.0;
}

public sealed record MainlineOptions
{
    public MainlineStrategy Strategy { get; init; } = MainlineStrategy.Auto;
}

/// <summary>How many side galleries are surveyed (beside the mainline).</summary>
public sealed record LateralOptions
{
    /// <summary>0 = mainline only; 1 = galleries off the mainline; 2 = their galleries … null = no limit.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Galleries shorter than this are not surveyed (splays or a side station may still cover them).</summary>
    public double MinLength { get; init; } = 3.0;

    /// <summary>Galleries narrower than this are not surveyed.</summary>
    public double MinWidth { get; init; } = 0.5;

    /// <summary>The most laterals; null = no limit.</summary>
    public int? MaxCount { get; init; }

    /// <summary>The most metres of lateral legs; null = no limit.</summary>
    public double? MaxTotalLength { get; init; }

    /// <summary>Stop when the best remaining gallery adds less newly visible wall (m²) per metre surveyed.</summary>
    public double MinEfficiency { get; init; } = 0.5;

    /// <summary>Drop the last stations of a lateral that add no coverage.</summary>
    public bool TrimTail { get; init; } = true;

    public LoopPolicy Loops { get; init; } = LoopPolicy.Close;

    public double LoopBonus { get; init; } = 2.0;

    /// <summary>Allow single-leg side stations into niches, cheaper than a lateral.</summary>
    public bool SideStations { get; init; } = true;

    public int MaxSideStationsPerStation { get; init; } = 1;

    /// <summary>What one station costs, in metres of survey (the price of a side station).</summary>
    public double StationCost { get; init; } = 2.0;
}

/// <summary>How much of the cave must be seen from the stations.</summary>
public sealed record CoverageOptions
{
    /// <summary>Fraction of the significant wall (or void) that must be visible; reaching it stops adding laterals.</summary>
    public double Target { get; init; } = 0.9;

    public CoverageMeasure Measure { get; init; } = CoverageMeasure.WallArea;

    /// <summary>Pockets shallower than this are not significant: they do not count towards the target.</summary>
    public double MinFeature { get; init; } = 1.0;

    /// <summary>A wall point is visible from a station within this distance.</summary>
    public double Range { get; init; } = 25.0;

    /// <summary>Views more grazing than this do not count.</summary>
    public double MaxIncidence { get; init; } = 80.0;

    /// <summary>Spacing of the wall samples; 0 = 0.5 m, coarsened to fit <see cref="MaxSamples"/>.</summary>
    public double SampleSpacing { get; init; }

    public int MaxSamples { get; init; } = 2_000_000;
}

/// <summary>How legs are placed along the passages.</summary>
public sealed record LegOptions
{
    public LegStrategy Strategy { get; init; } = LegStrategy.Preferred;

    public double Preferred { get; init; } = 5.0;

    public double Min { get; init; } = 1.0;

    public double Max { get; init; } = 15.0;

    /// <summary>Longest leg steeper than <see cref="VerticalAngle"/> (pitches); null = no limit.</summary>
    public double? MaxVertical { get; init; }

    public double VerticalAngle { get; init; } = 70.0;

    /// <summary>The least distance between a leg and the wall.</summary>
    public double Clearance { get; init; } = 0.15;

    /// <summary>The farthest the passage may stray from a leg, as a fraction of the passage radius…</summary>
    public double MaxDeviation { get; init; } = 0.75;

    /// <summary>…but never less than this.</summary>
    public double MaxDeviationFloor { get; init; } = 0.3;

    /// <summary>Pull of stations towards bends, squeezes, junctions and chamber entrances (0 = length only).</summary>
    public double FeatureWeight { get; init; } = 0.5;

    /// <summary>Penalty for zig-zag between consecutive legs.</summary>
    public double TurnWeight { get; init; } = 0.1;

    /// <summary>Spacing of the candidate station positions along the axis.</summary>
    public double SampleSpacing { get; init; } = 0.25;
}

/// <summary>Where stations stand.</summary>
public sealed record AnchorOptions
{
    public AnchorMode Mode { get; init; } = AnchorMode.Axial;

    public double FloorOffset { get; init; }

    public double WallOffset { get; init; }

    /// <summary>On a floor steeper than this, a floor station goes to the wall instead.</summary>
    public double MaxFloorSlope { get; init; } = 50.0;
}

/// <summary>How splays are shot.</summary>
public sealed record SplayOptions
{
    public SplayMode Mode { get; init; } = SplayMode.Ring;

    /// <summary>Shots per station (the budget for <see cref="SplayMode.Adaptive"/> and <see cref="SplayMode.WallUniform"/>).</summary>
    public int Count { get; init; } = 8;

    /// <summary>When set, shots per metre of passage instead of <see cref="Count"/> per station.</summary>
    public double? PerMetre { get; init; }

    /// <summary>More shots in chambers, fewer in crawls.</summary>
    public bool ScaleBySize { get; init; } = true;

    /// <summary>Two more shots along the passage (<see cref="SplayMode.Ring"/>).</summary>
    public bool ForeBack { get; init; } = true;

    /// <summary>Tilt of the ring out of the cross-section plane.</summary>
    public double RingTilt { get; init; }

    public double MinRange { get; init; } = 0.1;

    public double MaxRange { get; init; } = 80.0;

    public double MaxIncidence { get; init; } = 80.0;

    /// <summary>Hits this close to the station are dropped (a floor station's own floor).</summary>
    public double SelfClearance { get; init; } = 0.05;

    public int MaxTotal { get; init; } = 100_000;

    /// <summary><see cref="SplayMode.Adaptive"/> refines where neighbouring shots differ by more than this fraction.</summary>
    public double DepthJump { get; init; } = 0.3;
}

/// <summary>Synthetic instrument error. Off unless given: real exports stay exact.</summary>
public sealed record NoiseOptions
{
    public double TapeSigma { get; init; } = 0.005;

    public double TapeSigmaRelative { get; init; } = 0.002;

    public double CompassSigma { get; init; } = 0.5;

    public double ClinoSigma { get; init; } = 0.5;

    /// <summary>A fixed compass offset for the whole survey.</summary>
    public double CompassBias { get; init; }

    public double ClinoBias { get; init; }

    /// <summary>Tape scale factor (1 = exact).</summary>
    public double TapeScale { get; init; } = 1.0;

    /// <summary>Fraction of legs with a gross error.</summary>
    public double BlunderRate { get; init; }
}

/// <summary>How the readings are written.</summary>
public sealed record ReadingOptions
{
    public ReadingStyle Style { get; init; } = ReadingStyle.Normal;

    public int LengthDecimals { get; init; } = 2;

    public int AngleDecimals { get; init; } = 1;

    /// <summary>LRUD columns on the legs.</summary>
    public bool EmitLrud { get; init; } = true;

    /// <summary>Synthetic error; null = exact readings.</summary>
    public NoiseOptions? Noise { get; init; }
}

/// <summary>What is written.</summary>
public sealed record OutputOptions
{
    /// <summary>Name of the survey; null = from the model's file name.</summary>
    public string? SurveyName { get; init; }

    public StationNaming Naming { get; init; } = StationNaming.Sequential;

    /// <summary>Split long lines into sub-surveys of at most this many stations; null = no split.</summary>
    public int? StationsPerSurvey { get; init; }

    /// <summary>Write the skeleton, chambers, cuts, rejected legs and uncovered regions as PLY files.</summary>
    public bool DebugLayers { get; init; }
}

/// <summary>Every setting of a derived survey.</summary>
public sealed record MeshSurveyOptions
{
    public InputOptions Input { get; init; } = new();
    public VoidOptions Void { get; init; } = new();
    public SkeletonOptions Skeleton { get; init; } = new();
    public MainlineOptions Mainline { get; init; } = new();
    public LateralOptions Laterals { get; init; } = new();
    public CoverageOptions Coverage { get; init; } = new();
    public LegOptions Legs { get; init; } = new();

    /// <summary>Leg settings for laterals; null = the same as <see cref="Legs"/>.</summary>
    public LegOptions? LateralLegs { get; init; }

    public AnchorOptions Anchor { get; init; } = new();
    public SplayOptions Splays { get; init; } = new();
    public ReadingOptions Readings { get; init; } = new();
    public OutputOptions Output { get; init; } = new();

    /// <summary>Every random choice (splay rotation, noise) comes from this seed.</summary>
    public int Seed { get; init; } = 1;

    public static readonly MeshSurveyOptions Default = new();

    /// <summary>The settings that make no sense together, as messages; empty when all is well.</summary>
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        void Positive(double value, string name) { if (!(value > 0)) issues.Add($"{name} must be greater than 0."); }
        void NotNegative(double value, string name) { if (!(value >= 0)) issues.Add($"{name} must not be negative."); }

        NotNegative(Void.VoxelSize, "void.voxelSize");
        Positive(Void.MinPassageWidth, "void.minPassageWidth");
        NotNegative(Void.MinPillarSize, "void.minPillarSize");
        NotNegative(Void.HoleClosing, "void.holeClosing");
        if (Void.MaxVoxels < 1000) issues.Add("void.maxVoxels must be at least 1000.");
        NotNegative(Skeleton.MinBranchLength, "skeleton.minBranchLength");
        NotNegative(Skeleton.BranchSignificance, "skeleton.branchSignificance");
        Positive(Skeleton.ChamberMinRadius, "skeleton.chamberMinRadius");
        Positive(Skeleton.ChamberRatio, "skeleton.chamberRatio");
        if (Coverage.Target is < 0 or > 1) issues.Add("coverage.target must be between 0 and 1.");
        Positive(Coverage.Range, "coverage.range");
        if (Laterals.MaxDepth is < 0) issues.Add("laterals.maxDepth must not be negative.");
        if (Laterals.MaxCount is < 0) issues.Add("laterals.maxCount must not be negative.");
        ValidateLegs(Legs, "legs", issues);
        if (LateralLegs is not null) ValidateLegs(LateralLegs, "lateralLegs", issues);
        if (Splays.Count < 0) issues.Add("splays.count must not be negative.");
        if (Splays.MinRange > Splays.MaxRange) issues.Add("splays.minRange must not exceed splays.maxRange.");
        if (Readings.LengthDecimals is < 0 or > 6) issues.Add("readings.lengthDecimals must be 0–6.");
        if (Readings.AngleDecimals is < 0 or > 6) issues.Add("readings.angleDecimals must be 0–6.");
        if (Output.StationsPerSurvey is < 2) issues.Add("output.stationsPerSurvey must be at least 2.");
        return issues;
    }

    private static void ValidateLegs(LegOptions legs, string name, List<string> issues)
    {
        if (!(legs.Min > 0)) issues.Add($"{name}.min must be greater than 0.");
        if (legs.Min > legs.Preferred) issues.Add($"{name}.min must not exceed {name}.preferred.");
        if (legs.Preferred > legs.Max) issues.Add($"{name}.preferred must not exceed {name}.max.");
        if (!(legs.SampleSpacing > 0)) issues.Add($"{name}.sampleSpacing must be greater than 0.");
        if (legs.Clearance < 0) issues.Add($"{name}.clearance must not be negative.");
        if (legs.MaxVertical is { } v && v < legs.Min) issues.Add($"{name}.maxVertical must not be below {name}.min.");
    }
}
