// The public pipeline: Prepare (void + skeleton, cached by its settings) then Plan (guide, mainline,
// laterals, stations, legs, LRUD, splays, names, report). Run does both for a file.

using System.Diagnostics;
using Therion.Cave3D;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Skeleton;
using Therion.MeshSurvey.Splays;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey;

/// <summary>The slow stage's result: the void and its skeleton.</summary>
public sealed class PreparedCave
{
    public required VoidGrid Grid { get; init; }
    public required VoidReport VoidReport { get; init; }
    public required SkeletonResult Skeleton { get; init; }
    internal string Key { get; init; } = "";
    public double PrepareMilliseconds { get; init; }
}

public sealed partial class MeshSurveySession
{
    private PreparedCave? _cached;

    private static string PrepareKey(MeshSurveyOptions o, IReadOnlyList<CaveVector3> points) =>
        MeshSurveyJson.Serialize(new { o.Void, o.Skeleton, Points = points.Select(p => new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3) }) });

    private IReadOnlyList<CaveVector3> GuidePointsInternal(SurveyGuide guide) =>
        guide.AllPoints.Select(p => Model.ToInternal(guide.Frame == GuideFrame.Source ? Model.SourceToModel(p.Position) : p.Position)).ToList();

    /// <summary>Builds the void and the skeleton, or returns the cached result when the void and
    /// skeleton settings (and the guide's points) have not changed.</summary>
    public PreparedCave Prepare(MeshSurveyOptions options, SurveyGuide? guide = null, IProgress<MeshSurveyProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        guide ??= SurveyGuide.None;
        var issues = options.Validate();
        if (issues.Count > 0) throw new ArgumentException(string.Join(" ", issues), nameof(options));
        if (Model.Mesh is { } mesh ? mesh.Triangles.Count == 0 : (Model.Points?.Count ?? 0) < 10)
            throw new InvalidOperationException("The model has no surface to work on (no triangles, or almost no points).");
        var points = GuidePointsInternal(guide);
        string key = PrepareKey(options, points);
        if (_cached is { } c && c.Key == key) return c;
        var sw = Stopwatch.StartNew();
        var (grid, voidReport) = BuildVoid(options, points, progress, ct);
        var skeleton = BuildSkeleton(grid, options, points, progress, ct);
        _cached = new PreparedCave { Grid = grid, VoidReport = voidReport, Skeleton = skeleton, Key = key, PrepareMilliseconds = sw.Elapsed.TotalMilliseconds };
        return _cached;
    }

    public Task<PreparedCave> PrepareAsync(MeshSurveyOptions options, SurveyGuide? guide = null, IProgress<MeshSurveyProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Prepare(options, guide, progress, ct), ct);

    /// <summary>Plans the survey on a prepared cave. Throws <see cref="GuideRejectedException"/> when
    /// guide legs fail and the guide says to stop.</summary>
    public DerivedSurvey Plan(PreparedCave cave, MeshSurveyOptions options, SurveyGuide? guide = null, IProgress<MeshSurveyProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cave);
        ArgumentNullException.ThrowIfNull(options);
        guide ??= SurveyGuide.None;
        PreparedVoid = cave.Grid;
        if (cave.Grid.Count == 0)
            throw new InvalidOperationException("No enclosed space was found in the model: check the unit and the up axis, and for a "
                + "point cloud give a start point inside the cave or raise void.holeClosing.");
        var sw = Stopwatch.StartNew();
        progress?.Report(new MeshSurveyProgress("plan", 0));
        string name = options.Output.SurveyName ?? Model.Name;
        var partial = new MeshSurveyReport
        {
            Source = SourcePath is null ? Model.Name : Path.GetFileName(SourcePath),
            SourceSha256 = SourceSha256,
            Normalization = Model.Report with { Warnings = [.. ReadWarnings, .. Model.Report.Warnings] },
            Void = cave.VoidReport,
            Skeleton = cave.Skeleton.Report,
            Plan = new PlanReport(),
        };
        var planner = new SurveyPlanner(this, cave.Grid, cave.Skeleton, guide, options, ct);
        var (survey, coverage) = planner.Plan(name, partial);
        double planMs = sw.Elapsed.TotalMilliseconds;
        progress?.Report(new MeshSurveyProgress("splays", 0));
        var (splays, splayReport) = SplayCaster.Cast(this, survey, options.Splays, coverage, options.Seed, ct);
        double splayMs = sw.Elapsed.TotalMilliseconds - planMs;
        var result = new DerivedSurvey
        {
            Name = survey.Name,
            Stations = survey.Stations,
            Legs = survey.Legs,
            Splays = splays,
            Branches = survey.Branches,
            StartStation = survey.StartStation,
            CoordinateSystem = survey.CoordinateSystem,
            Report = survey.Report with
            {
                Splays = splayReport,
                TimingsMs = new Dictionary<string, double>
                {
                    ["prepare"] = Math.Round(cave.PrepareMilliseconds, 1),
                    ["plan"] = Math.Round(planMs, 1),
                    ["splays"] = Math.Round(splayMs, 1),
                },
            },
        };
        TherionCentrelineWriter.Name(result, options.Output.Naming);
        progress?.Report(new MeshSurveyProgress("splays", 1));
        return result;
    }

    /// <summary>Opens a model file, prepares it and plans the survey in one go.</summary>
    public static (MeshSurveySession Session, DerivedSurvey Survey) Run(string path, MeshSurveyOptions options, SurveyGuide? guide = null,
        IProgress<MeshSurveyProgress>? progress = null, CancellationToken ct = default)
    {
        var session = Open(path, options.Input);
        var cave = session.Prepare(options, guide, progress, ct);
        return (session, session.Plan(cave, options, guide, progress, ct));
    }

    /// <summary>The Therion text of a planned survey (with this session's provenance).</summary>
    public string WriteTherion(DerivedSurvey survey, MeshSurveyOptions options) =>
        TherionCentrelineWriter.Write(survey, options, SourcePath is null ? Model.Name : Path.GetFileName(SourcePath), SourceSha256);
}
