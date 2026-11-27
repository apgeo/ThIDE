using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Cave3D;
using Therion.Mcp.Mutations;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.Workspace.Import;

namespace Therion.Mcp.Tools;

/// <param name="Units">The unit the model was read in ("metres", "millimetres" …).</param>
/// <param name="UnitsGuessed">The file gave no unit; it was guessed from the model's size.</param>
/// <param name="Size">East, north, up extent in metres.</param>
/// <param name="ClosedSurfaceLoops">Loops of the cave when the mesh is closed; null when it has holes.</param>
public sealed record MeshInspectResult(
    string Model, bool PointCloud, int Points, int Triangles, string Units, bool UnitsGuessed, string UpAxis,
    double[] Size, double SurfaceArea, double EnclosedVolume, int Holes, double LargestHoleArea, int? ClosedSurfaceLoops,
    double VoxelSize, long EstimatedVoxels, long EstimatedMegabytes, IReadOnlyList<string> Warnings);

/// <param name="Status">clear, tight, outOfRange, blocked, endpointInRock, endpointOutside.</param>
/// <param name="Description">One line for a person.</param>
/// <param name="FirstHit">Distance from the start to the first wall crossing, when blocked.</param>
public sealed record MeshLegCheckResult(
    string Status, string Description, bool Usable, double Length, double? MinClearance, double? FirstHit, int Crossings, double RockThickness);

/// <param name="Text">The derived Therion source, on a dry run (capped by maxBytes). Null once written.</param>
/// <param name="Report">Where the full report (settings, findings, positions) is, once written.</param>
public sealed record MeshSurveyRunResult(
    string? Target, string? Report, string? Text, bool Truncated,
    int Stations, int Legs, double Length, double MainlineLength, int Laterals, int LoopClosures, int SideStations, int Parts,
    double LegMin, double LegMedian, double LegMax, double CoverageReached, double CoverageTarget, string StopReason,
    int Splays, int BlockedLegs, int TightLegs, int Relaxations, int Continuations,
    IReadOnlyList<string> GuideLegs, IReadOnlyList<string> Warnings, MutationResult? Mutation);

/// <summary>Looking at a 3-D model and checking legs against it (read-only).</summary>
[McpServerToolType]
public sealed class MeshSurveyReadTools(IWorkspaceHost host)
{
    [McpServerTool(Name = "mesh_survey_inspect", Title = "Inspect a 3-D model", ReadOnly = true, Idempotent = true)]
    [Description("A quick look at a 3-D model (STL, PLY, OBJ, glb, XYZ) before deriving a survey from it: size in metres, "
               + "the unit and up axis assumed (STL/PLY/OBJ carry no unit), holes (the largest is the likely entrance), "
               + "loops when the mesh is closed, and the voxel size, voxel count and memory the derivation would need.")]
    public async Task<ToolResult<MeshInspectResult>> Inspect(
        [Description("Workspace-relative path of the model, e.g. 'scans/cave.ply'.")] string model,
        [Description("auto, m, cm, mm or ft. Default auto (guessed from the size, glb is metres).")] string? units = null,
        [Description("auto, z or y. Default auto (glb is y-up, others z-up).")] string? upAxis = null,
        [Description("Azimuth (degrees clockwise from north) the model's +Y axis points to. Default 0.")] double northRotation = 0,
        CancellationToken ct = default)
    {
        var (path, input, error) = await Resolve(model, units, upAxis, northRotation, ct);
        if (error is not null) return ToolResult<MeshInspectResult>.Failure(error.Value.Code, error.Value.Message);
        try
        {
            var session = MeshSurveySession.Open(path!, input);
            var r = session.Inspect(new MeshSurveyOptions { Input = input });
            var n = r.Normalization;
            return ToolResult<MeshInspectResult>.Success(new MeshInspectResult(
                model, r.IsPointCloud, r.Points, r.Triangles, n.Units.ToString().ToLowerInvariant(), n.UnitsGuessed, n.UpAxis.ToString().ToLowerInvariant(),
                [Round(r.Size.X), Round(r.Size.Y), Round(r.Size.Z)], Round(r.SurfaceArea), Round(r.EnclosedVolume), r.Holes, Round(r.LargestHoleArea),
                r.ClosedSurfaceLoops, r.VoxelSize, r.EstimatedVoxels, r.EstimatedBytes / (1024 * 1024), [.. n.Warnings, .. r.Warnings]));
        }
        catch (Exception ex) when (IsModelError(ex))
        {
            return ToolResult<MeshInspectResult>.Failure(ToolErrorCodes.ImportFailed, $"'{model}' is not readable as a model: {ex.Message}");
        }
    }

    [McpServerTool(Name = "mesh_survey_check_leg", Title = "Check a leg against a 3-D model", ReadOnly = true, Idempotent = true)]
    [Description("Whether a straight leg between two points stays inside the passage of a 3-D model: clear, tight (closer to a "
               + "wall than the clearance), blocked (crosses the wall: where, how much rock), or an end outside the passage. "
               + "Points are model coordinates in metres (z up, north applied), or the file's own with sourceFrame.")]
    public async Task<ToolResult<MeshLegCheckResult>> CheckLeg(
        [Description("Workspace-relative path of the model.")] string model,
        [Description("Start of the leg: [x, y, z].")] double[] from,
        [Description("End of the leg: [x, y, z].")] double[] to,
        [Description("Least distance to a wall, metres. Default 0.15.")] double clearance = 0.15,
        [Description("The points are in the file's own coordinates (as another viewer shows them).")] bool sourceFrame = false,
        [Description("auto, m, cm, mm or ft.")] string? units = null,
        [Description("auto, z or y.")] string? upAxis = null,
        [Description("Azimuth of the model's +Y axis, degrees.")] double northRotation = 0,
        CancellationToken ct = default)
    {
        if (from is not { Length: 3 } || to is not { Length: 3 })
            return ToolResult<MeshLegCheckResult>.Failure(ToolErrorCodes.InvalidArgument, "from and to are [x, y, z].");
        var (path, input, error) = await Resolve(model, units, upAxis, northRotation, ct);
        if (error is not null) return ToolResult<MeshLegCheckResult>.Failure(error.Value.Code, error.Value.Message);
        try
        {
            var session = MeshSurveySession.Open(path!, input);
            var a = new CaveVector3(from[0], from[1], from[2]);
            var b = new CaveVector3(to[0], to[1], to[2]);
            if (sourceFrame) { a = session.Model.SourceToModel(a); b = session.Model.SourceToModel(b); }
            var c = session.CheckLeg(a, b, new LegCheckOptions { Clearance = clearance });
            return ToolResult<MeshLegCheckResult>.Success(new MeshLegCheckResult(
                Camel(c.Status.ToString()), c.Describe(), c.IsUsable, Round(c.Length),
                double.IsInfinity(c.MinClearance) ? null : Round(c.MinClearance), c.FirstHit is { } f ? Round(f) : null, c.Crossings, Round(c.RockThickness)));
        }
        catch (Exception ex) when (IsModelError(ex))
        {
            return ToolResult<MeshLegCheckResult>.Failure(ToolErrorCodes.ImportFailed, $"'{model}' is not readable as a model: {ex.Message}");
        }
    }

    private async Task<(string? Path, InputOptions Input, (string Code, string Message)? Error)> Resolve(
        string model, string? units, string? upAxis, double northRotation, CancellationToken ct)
    {
        var input = new InputOptions { NorthRotation = northRotation };
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return (null, input, (error.Code, error.Message));
        if (!WorkspacePaths.TryResolve(snapshot!.Root, model, out var full, out var reason)) return (null, input, (ToolErrorCodes.PathOutsideWorkspace, reason));
        if (!File.Exists(full)) return (null, input, (ToolErrorCodes.FileNotFound, $"No such file: {model}"));
        if (units is not null)
        {
            ModelLengthUnit? u = units.Trim().ToLowerInvariant() switch
            {
                "auto" => ModelLengthUnit.Auto, "m" or "metres" or "meters" => ModelLengthUnit.Metres, "cm" => ModelLengthUnit.Centimetres,
                "mm" => ModelLengthUnit.Millimetres, "ft" or "feet" => ModelLengthUnit.Feet, _ => null,
            };
            if (u is null) return (null, input, (ToolErrorCodes.InvalidArgument, $"units: '{units}' is not auto, m, cm, mm or ft."));
            input = input with { Units = u.Value };
        }
        if (upAxis is not null)
        {
            if (!Enum.TryParse<ModelUpAxisChoice>(upAxis, true, out var up)) return (null, input, (ToolErrorCodes.InvalidArgument, $"upAxis: '{upAxis}' is not auto, z or y."));
            input = input with { UpAxis = up };
        }
        return (full, input, null);
    }

    private static bool IsModelError(Exception ex) => MeshSurveyTools.IsModelError(ex);

    private static double Round(double v) => Math.Round(v, 3);

    private static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

/// <summary>A survey derived from a 3-D model (STL, PLY, OBJ, glb, XYZ) that has none.</summary>
[McpServerToolType]
public sealed class MeshSurveyTools(IWorkspaceHost host, MutationEngine mutations)
{
    // Create-only, like import_survey: the .th and its report are new files, never overwritten.
    [McpServerTool(Name = "mesh_survey_run", Title = "Derive a survey from a 3-D model", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Derives a Therion centreline (and splays) from a 3-D model that has none: a mainline from the start, laterals "
               + "until the coverage target is reached, legs near the preferred length that never cut through a wall. The file "
               + "says it is DERIVED, NOT MEASURED. Settings and guide are JSON (see the mesh survey docs): settings e.g. "
               + "{\"legs\":{\"preferred\":6},\"laterals\":{\"maxDepth\":1},\"splays\":{\"mode\":\"ring\",\"count\":8}}; guide e.g. "
               + "{\"start\":{\"name\":\"e\",\"position\":{\"x\":0,\"y\":0,\"z\":0}}}. Returns the numbers; the survey goes to the target "
               + "(.th) and the full report beside it (.mesh-survey.json). Preview by default (dryRun:true). Can take minutes on a large scan.")]
    public async Task<ToolResult<MeshSurveyRunResult>> Run(
        [Description("Workspace-relative path of the model.")] string model,
        [Description("Workspace-relative .th to create. Omit to only look at the result.")] string? target = null,
        [Description("A preset: scan-backbone (default), mainline-only, main-and-major-laterals, thorough, synthetic-survey.")] string? preset = null,
        [Description("Settings as JSON (replaces the preset; unnamed settings keep their defaults).")] string? settings = null,
        [Description("Guide as JSON: start, points, legs, mode (complete|onlyGiven), onBlocked (error|warn|drop), frame (model|source).")] string? guide = null,
        [Description("Workspace-relative guide file instead: .json, .th (a centreline with a fix), .lox or .3d.")] string? guideFile = null,
        [Description("Preview without writing. Defaults to true — pass false to create the target.")] bool dryRun = true,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")] int maxBytes = 0,
        CancellationToken ct = default)
    {
        var (snapshot, loadError) = await host.TryGetSnapshotAsync(ct);
        if (loadError is not null) return ToolResult<MeshSurveyRunResult>.Failure(loadError);
        var root = snapshot!.Root;
        if (!WorkspacePaths.TryResolve(root, model, out var modelFull, out var reason)) return Fail(ToolErrorCodes.PathOutsideWorkspace, reason);
        if (!File.Exists(modelFull)) return Fail(ToolErrorCodes.FileNotFound, $"No such file: {model}");

        MeshSurveyOptions options;
        try
        {
            if (settings is not null && preset is not null) return Fail(ToolErrorCodes.InvalidArgument, "Give a preset or settings, not both.");
            options = settings is not null ? MeshSurveyJson.ReadOptions(settings)
                : preset is not null ? MeshSurveyPresets.Get(preset) ?? throw new ArgumentException($"Unknown preset '{preset}'. Use one of: {string.Join(", ", MeshSurveyPresets.Names)}.")
                : new MeshSurveyOptions();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            return Fail(ToolErrorCodes.InvalidArgument, "settings: " + ex.Message);
        }
        var issues = options.Validate();
        if (issues.Count > 0) return Fail(ToolErrorCodes.InvalidArgument, string.Join(" ", issues));

        SurveyGuide surveyGuide = SurveyGuide.None;
        IReadOnlyList<string> guideWarnings = [];
        try
        {
            if (guide is not null) surveyGuide = MeshSurveyJson.ReadGuide(guide);
            else if (guideFile is not null)
            {
                if (!WorkspacePaths.TryResolve(root, guideFile, out var guideFull, out var guideReason)) return Fail(ToolErrorCodes.PathOutsideWorkspace, guideReason);
                if (!File.Exists(guideFull)) return Fail(ToolErrorCodes.FileNotFound, $"No such file: {guideFile}");
                (surveyGuide, guideWarnings) = MeshSurveyImport.ReadGuide(guideFull);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or Cave3D.Parsing.CaveFileFormatException)
        {
            return Fail(ToolErrorCodes.InvalidArgument, "guide: " + ex.Message);
        }

        string? targetFull = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            if (!WorkspacePaths.TryResolve(root, target, out targetFull, out var targetReason)) return Fail(ToolErrorCodes.PathOutsideWorkspace, targetReason);
            if (!Path.GetExtension(targetFull).Equals(".th", StringComparison.OrdinalIgnoreCase))
                return Fail(ToolErrorCodes.InvalidArgument, "The derived survey must be written to a '.th' file.");
        }
        else if (!dryRun)
        {
            return Fail(ToolErrorCodes.InvalidArgument, "Give a target .th to write the derived survey to.");
        }

        MeshSurveyImportResult result;
        try
        {
            result = await Task.Run(() => MeshSurveyImport.Run(modelFull, options, surveyGuide, null, ct, guideWarnings), ct);
        }
        catch (GuideRejectedException ex)
        {
            return Fail(ToolErrorCodes.InvalidArgument, ex.Message);
        }
        catch (Exception ex) when (IsModelError(ex))
        {
            return Fail(ToolErrorCodes.ImportFailed, $"'{model}' could not be turned into a survey: {ex.Message}");
        }

        var plan = result.Survey.Report.Plan;
        var guideLegs = plan.GuideLegs.Select(g => $"{g.From}–{g.To}: {g.Check.Describe()}{(g.Kept ? "" : " (left out)")}").ToList();
        MeshSurveyRunResult Summary(string? text, bool truncated, string? relTarget, string? relReport, MutationResult? mutation) => new(
            relTarget, relReport, text, truncated, plan.Stations, plan.Legs, Round(plan.Length), Round(plan.MainlineLength), plan.Laterals,
            plan.LoopClosures, plan.SideStations, plan.Components, Round(plan.LegMin), Round(plan.LegMedian), Round(plan.LegMax),
            Math.Round(plan.CoverageReached, 3), plan.CoverageTarget, Camel(plan.StopReason.ToString()), result.Survey.Splays.Count,
            plan.BlockedLegs, plan.TightLegs, plan.Relaxations.Count, plan.Continuations, guideLegs, [.. guideWarnings, .. plan.Warnings], mutation);

        if (targetFull is null)
            return ToolResult<MeshSurveyRunResult>.Success(Summary(Cap(result.Therion, maxBytes, out var cut), cut, null, null, null));

        string reportFull = MeshSurveyImport.ReportPath(targetFull);
        var mutationPlan = new MutationPlan([new CreateFile(targetFull, result.Therion), new CreateFile(reportFull, result.ReportJson)]);
        var applied = await mutations.ApplyAsync(mutationPlan, dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<MeshSurveyRunResult>.Failure(failure);
        string relTh = WorkspacePaths.ToRelative(root, targetFull), relJson = WorkspacePaths.ToRelative(root, reportFull);
        return dryRun
            ? ToolResult<MeshSurveyRunResult>.Success(Summary(Cap(result.Therion, maxBytes, out var truncated), truncated, relTh, relJson, applied.Data))
            : ToolResult<MeshSurveyRunResult>.Success(Summary(null, false, relTh, relJson, applied.Data));
    }

    internal static bool IsModelError(Exception ex) =>
        ex is IOException or InvalidDataException or Cave3D.Parsing.CaveFileFormatException or ArgumentException or InvalidOperationException or UnauthorizedAccessException;

    private static double Round(double v) => Math.Round(v, 3);

    private static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static string Cap(string text, int maxBytes, out bool truncated)
    {
        var capped = ToolLimits.Utf8Prefix(text, ToolLimits.ClampBytes(maxBytes));
        truncated = capped.Length < text.Length;
        return capped;
    }

    private static ToolResult<MeshSurveyRunResult> Fail(string code, string message) => ToolResult<MeshSurveyRunResult>.Failure(code, message);
}
