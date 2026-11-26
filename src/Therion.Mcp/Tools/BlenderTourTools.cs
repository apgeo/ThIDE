using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;

namespace Therion.Mcp.Tools;

/// <summary>A row of a tour's table: the main passage (<c>trunk</c>), or one side passage.</summary>
/// <param name="Id"><c>trunk</c>, or the branch's id: B1, B2 … along the main passage, B3.1 for what hangs on B3.</param>
/// <param name="Kind">trunk, branch (hangs on the main passage at one station) or bypass (at two: a loop).</param>
/// <param name="Junction">The station where it leaves the main passage; for the trunk, where the tour starts.</param>
/// <param name="End">Where its way ends: a branch's farthest station, where a bypass comes back, the trunk's end.</param>
/// <param name="LengthMeters">All its legs together.</param>
/// <param name="ReachMeters">How far from the main passage it gets, along its own passages.</param>
/// <param name="DepthChangeMeters">The height of the highest or lowest station of its way, relative to its junction; negative is down.</param>
/// <param name="MeanWidthMeters">The mean of left + right over its legs; null when the model has none.</param>
/// <param name="Rank">1 is the best among the side passages of the same depth: by reach, then by length.</param>
/// <param name="Chosen">Chosen by the rules or by hand, before the time budget had its say.</param>
/// <param name="Why">trunk, byRank, included, lead (short, but it has a lead), short, overCount, or excluded.</param>
/// <param name="Mode">What the film does: trunk, cut, return, through (a bypass), peek, or skip.</param>
/// <param name="Dropped">Why a chosen side passage is not in the film after all: budget, or notReached (it hangs
/// on a way that was cut short before it). Null otherwise.</param>
/// <param name="Seconds">Its share of the film.</param>
/// <param name="Speed">How many times the passages' own speed it is flown at: 1 to 2.</param>
/// <param name="ContinuesMeters">How much of its way is left when the film cuts away; 0 when flown to the end.</param>
public sealed record TourBranchDto(
    string Id, string Kind, int Depth, string Junction, string End, double LengthMeters, double? ReachMeters, double DepthChangeMeters,
    double? MeanWidthMeters, int Leads, IReadOnlyList<string> Surveys, int? Rank, bool Chosen, string Why, string Mode, string? Dropped,
    double Seconds, double Speed, double FlownMeters, double ContinuesMeters);

/// <param name="Begins">cut, or dip: the film goes back to a junction through a short dip to black.</param>
public sealed record TourShotDto(int Number, string Name, double Seconds, string Begins);

/// <param name="Model">The model the tour was planned for: workspace-relative, or absolute outside the workspace.</param>
/// <param name="StaleModel">Why the model may be out of date, when it is older than the sources.</param>
/// <param name="Trunk">The kind of main passage this is: longest, farthest, deepest, through or via.</param>
/// <param name="Seconds">The film's length.</param>
/// <param name="Branches">The main passage first, then every side passage in the order of the tour.</param>
/// <param name="Shots">The film, shot by shot.</param>
/// <param name="Notices">What was left out, cut short or changed to keep within the time, and what the options named in vain.</param>
/// <param name="Storyboard">The storyboard file written, workspace-relative; null from plan_tour, which writes none.</param>
public sealed record TourPlanResult(
    string Model, string ModelKind, string? StaleModel, string Trunk, double Seconds, IReadOnlyList<TourBranchDto> Branches,
    IReadOnlyList<TourShotDto> Shots, IReadOnlyList<string> Notices, string? Storyboard = null);

/// <summary>What plan_tour and write_tour_storyboard both take.</summary>
internal sealed record TourArguments(
    string? Model, string? Trunk, string? Start, string[]? Via, string? End, double TourSeconds, double BranchSeconds, string? Mode,
    string[]? Modes, bool PeekShort, double MinLength, int MaxCount, bool WithLeads, string[]? Include, string[]? Exclude, int Depth,
    string? Preset, string? Language)
{
    internal const string ModelHelp = "Workspace-relative .lox or .3d. Omit for the project's own model.";
    internal const string TrunkHelp =
        "Which way the main passage runs: auto (the default: farthest when the model flags an entrance or start is given, else "
        + "longest), longest (the cave's longest passage), farthest (from the start to the station farthest along the passages), "
        + "deepest (to the lowest station), through (to another entrance, or to end), via (through the stations of via, to end).";
    internal const string StartHelp = "The station the tour starts at. Omit for the first station flagged as an entrance. A name in several surveys is written name@survey.";
    internal const string ViaHelp = "With trunk: via, the stations the main passage passes, in order.";
    internal const string EndHelp = "With trunk: through or via, the station the main passage ends at.";
    internal const string TourSecondsHelp = "The most the film may take, in seconds. 0 keeps the default (180).";
    internal const string BranchSecondsHelp = "The most the visit of one side passage may take, in seconds. 0 keeps the default (20).";
    internal const string ModeHelp =
        "What the camera does about a chosen side passage: cut (the default: flies in, holds at the end, and the film cuts back to "
        + "the junction), return (flies in and back out, about 1.6 times the film time), peek (looks into it from the junction for "
        + "3 s), skip.";
    internal const string ModesHelp =
        "A mode for one side passage, each as \"<id>=<mode>\", e.g. \"B5=return\". Naming a branch is a choice by hand: cut and return "
        + "visit it whatever the rules say, skip leaves it out.";
    internal const string PeekShortHelp = "True looks into the side passages that are left out as too short.";
    internal const string MinLengthHelp = "Side passages shorter than this many metres are left out. 0 keeps the default: the greater of 50 m and 2 % of the main passage.";
    internal const string MaxCountHelp = "At most this many side passages, the best ranked first. -1 keeps the default (12).";
    internal const string WithLeadsHelp = "A side passage with a lead is visited whatever its length. Defaults to true.";
    internal const string IncludeHelp = "Side passages visited whatever the rules say: branch ids (B3), station names or survey names.";
    internal const string ExcludeHelp = "Side passages left out whatever the rules say: branch ids, station names or survey names.";
    internal const string DepthHelp = "1 (the default) visits what hangs on the main passage; 2 also what hangs on the chosen side passages.";
    internal const string PresetHelp = "The preset every shot takes its materials, lighting and render engine from. Defaults to \"Full flythrough\".";
    internal const string LanguageHelp = "The language of the texts the tour writes over the picture (\"Side passage B3 · 96 m\"): en (the default) or ro.";

    /// <summary>The tour these arguments ask for, or what is wrong with them.</summary>
    internal (TourSpec? Spec, string? Problem) ToSpec()
    {
        var spec = new TourSpec { PeekShort = PeekShort, WithLeads = WithLeads };
        if (!string.IsNullOrWhiteSpace(Trunk))
        {
            if (!ToolEnums.TryParse(Trunk, out TourTrunkKind kind)) return (null, $"Unknown trunk '{Trunk}'. Use one of: {ToolEnums.Names<TourTrunkKind>()}.");
            spec = spec with { Trunk = kind };
        }
        if (!string.IsNullOrWhiteSpace(Mode))
        {
            if (!ToolEnums.TryParse(Mode, out TourBranchMode mode)) return (null, $"Unknown mode '{Mode}'. Use one of: {ToolEnums.Names<TourBranchMode>()}.");
            spec = spec with { Mode = mode };
        }
        var modes = new Dictionary<string, TourBranchMode>(StringComparer.Ordinal);
        foreach (string one in Modes ?? [])
        {
            int at = one?.LastIndexOf('=') ?? -1;
            if (at <= 0 || !ToolEnums.TryParse(one![(at + 1)..].Trim(), out TourBranchMode mode))
                return (null, $"'{one}' is not a mode for a side passage. Write \"<id>=<mode>\" with one of: {ToolEnums.Names<TourBranchMode>()}.");
            modes[one[..at].Trim()] = mode;
        }
        if (TourSeconds < 0 || BranchSeconds < 0 || MinLength < 0 || !double.IsFinite(TourSeconds + BranchSeconds + MinLength))
            return (null, "tourSeconds, branchSeconds and minLength are not negative.");
        if (Depth is < 0 or > 16) return (null, "depth is 1 or more.");
        var via = (Via ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return (spec with
        {
            Start = string.IsNullOrWhiteSpace(Start) ? null : Start,
            End = string.IsNullOrWhiteSpace(End) ? null : End,
            Via = via,
            // Stations to pass say what kind of trunk it is.
            Trunk = via.Count > 0 && spec.Trunk == TourTrunkKind.Auto ? TourTrunkKind.Via : spec.Trunk,
            TourSeconds = TourSeconds > 0 ? TourSeconds : spec.TourSeconds,
            BranchSeconds = BranchSeconds > 0 ? BranchSeconds : spec.BranchSeconds,
            MinLength = MinLength > 0 ? MinLength : null,
            MaxCount = MaxCount >= 0 ? MaxCount : spec.MaxCount,
            Modes = modes,
            Include = (Include ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToList(),
            Exclude = (Exclude ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToList(),
            Depth = Depth > 0 ? Depth : 1,
            Preset = string.IsNullOrWhiteSpace(Preset) ? spec.Preset : Preset,
            Language = string.IsNullOrWhiteSpace(Language) ? spec.Language : Language,
        }, null);
    }
}

/// <summary>Working out a tour for the workspace's model: shared by the tool that only looks and the one
/// that writes the storyboard.</summary>
internal static class TourPlans
{
    internal static async Task<(TourPlanResult? Result, Tour? Tour, ToolError? Error)> PlanAsync(
        string root, IBlenderRenderHost blender, TourArguments arguments, bool reExport, CancellationToken ct)
    {
        static (TourPlanResult?, Tour?, ToolError?) Refuse(string code, string message) => (null, null, new ToolError(code, message));

        var (spec, problem) = arguments.ToSpec();
        if (spec is null) return Refuse(ToolErrorCodes.InvalidArgument, problem!);

        ModelSourceRequest request;
        if (!string.IsNullOrWhiteSpace(arguments.Model))
        {
            if (!WorkspacePaths.TryResolve(root, arguments.Model, out var file, out var reason))
                return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (!File.Exists(file)) return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {arguments.Model}");
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".lox", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".3d", StringComparison.OrdinalIgnoreCase))
                return Refuse(ToolErrorCodes.InvalidArgument, $"'{arguments.Model}' is not a cave model. Name a Therion .lox or a Survex .3d, or omit model.");
            request = ModelSourceRequest.ForExternalFile(file);
        }
        else
        {
            request = ModelSourceRequest.ForWorkspace(allowReExport: reExport);
        }

        try
        {
            var source = await blender.AcquireAsync(request, ct).ConfigureAwait(false);
            var presets = BlenderStatusTools.Presets(blender);
            RenderPreset? Preset(string name) => presets.FirstOrDefault(p => p.Name == name);
            var tour = await Task.Run(
                () => TourBuilder.Build(GeometryStage.Build(CaveModelReader.ReadFile(source.Model.Path)), spec, source.Leads, Preset), ct).ConfigureAwait(false);
            if (StoryboardValidator.Validate(tour.Storyboard, Preset) is { Count: > 0 } errors)
                return Refuse(ToolErrorCodes.InvalidArgument,
                    "The tour does not make a valid storyboard: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));

            var shots = tour.Storyboard.Shots
                .Select((s, i) => new TourShotDto(i + 1, s.Name ?? "", s.Seconds ?? 0, s.In is { Kind: TransitionKind.Dip } ? "dip" : "cut")).ToList();
            var result = new TourPlanResult(
                Model: BlenderRenderJob.Display(root, source.Model.Path),
                ModelKind: BlenderRenderJob.Camel(source.Model.Kind.ToString()),
                StaleModel: source.Model.StalenessReason,
                Trunk: BlenderRenderJob.Camel(tour.Plan.Trunk.ToString()),
                Seconds: tour.Seconds,
                Branches: tour.Rows.Select(r => new TourBranchDto(
                    r.Id, r.Kind, r.Depth, r.Junction, r.End, r.Length, r.Reach, r.DepthChange, r.MeanWidth, r.Leads, r.Surveys, r.Rank, r.Chosen,
                    r.Why, r.Mode, r.Dropped, r.Seconds, r.Speed, r.FlownMeters, r.ContinuesMeters)).ToList(),
                Shots: shots,
                Notices: tour.Notices);
            return (result, tour, null);
        }
        catch (ModelSourceNotFoundException ex)
        {
            return Refuse(ToolErrorCodes.ModelUnavailable, ex.Message);
        }
        catch (CaveFileFormatException ex)
        {
            return Refuse(ToolErrorCodes.ReadFailed, "The model file cannot be read: " + ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Refuse(ToolErrorCodes.InvalidArgument, ex.Message.Split(" (Parameter", 2)[0]);
        }
        catch (IOException ex)
        {
            return Refuse(ToolErrorCodes.ReadFailed, ex.Message);
        }
    }
}

/// <summary>
/// Planning a tour of the cave's branches. Read-only: it reads the model and answers with a table; it runs
/// neither Therion nor Blender and writes nothing. Registered only on a host that can render, in both profiles.
/// </summary>
[McpServerToolType]
public sealed class BlenderTourTools(IWorkspaceHost host, IBlenderRenderHost blender)
{
    [McpServerTool(Name = "plan_tour", Title = "Plan a tour of the cave's branches", ReadOnly = true, Idempotent = true)]
    [Description("Works out a film that tours the cave: the main passage (the trunk) flown from the entrance to the "
               + "farthest station, with the side passages worth a visit flown into at their junctions. Answers with "
               + "the branch table (every side passage: junction, length, reach, depth change, mean width, leads, "
               + "surveys, rank, whether it is chosen and why, what the film does about it and for how long), the "
               + "film shot by shot, and notices of what was left out or cut short to keep within the time (180 s "
               + "for the film, 20 s for a side passage). Nothing is written or rendered: change the options and ask "
               + "again until the table is right, then write_tour_storyboard writes the storyboard. The model is the "
               + "project's last build output as it is (staleModel says when it is older than the sources).")]
    public async Task<ToolResult<TourPlanResult>> PlanTour(
        [Description(TourArguments.ModelHelp)] string? model = null,
        [Description(TourArguments.TrunkHelp)] string? trunk = null,
        [Description(TourArguments.StartHelp)] string? start = null,
        [Description(TourArguments.ViaHelp)] string[]? via = null,
        [Description(TourArguments.EndHelp)] string? end = null,
        [Description(TourArguments.TourSecondsHelp)] double tourSeconds = 0,
        [Description(TourArguments.BranchSecondsHelp)] double branchSeconds = 0,
        [Description(TourArguments.ModeHelp)] string? mode = null,
        [Description(TourArguments.ModesHelp)] string[]? modes = null,
        [Description(TourArguments.PeekShortHelp)] bool peekShort = false,
        [Description(TourArguments.MinLengthHelp)] double minLength = 0,
        [Description(TourArguments.MaxCountHelp)] int maxCount = -1,
        [Description(TourArguments.WithLeadsHelp)] bool withLeads = true,
        [Description(TourArguments.IncludeHelp)] string[]? include = null,
        [Description(TourArguments.ExcludeHelp)] string[]? exclude = null,
        [Description(TourArguments.DepthHelp)] int depth = 1,
        [Description(TourArguments.PresetHelp)] string? preset = null,
        [Description(TourArguments.LanguageHelp)] string? language = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<TourPlanResult>.Failure(error);
        var arguments = new TourArguments(
            model, trunk, start, via, end, tourSeconds, branchSeconds, mode, modes, peekShort, minLength, maxCount, withLeads, include, exclude,
            depth, preset, language);
        var (result, _, refused) = await TourPlans.PlanAsync(snapshot!.Root, blender, arguments, reExport: false, ct);
        return refused is not null ? ToolResult<TourPlanResult>.Failure(refused) : ToolResult<TourPlanResult>.Success(result!);
    }
}

/// <summary>
/// Writing a tour's storyboard into the workspace. Registered only on a host that can render, and only in the
/// full profile: it writes a file, and may run Therion to export the model first.
/// </summary>
[McpServerToolType]
public sealed class BlenderTourWriteTools(IWorkspaceHost host, IBlenderRenderHost blender)
{
    // destructiveHint: with overwrite it replaces a storyboard somebody may have edited by hand.
    [McpServerTool(Name = "write_tour_storyboard", Title = "Write a tour's storyboard",
        ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Works out a tour of the cave's branches exactly as plan_tour does, with the same options, and writes "
               + "it as a storyboard file in the workspace: one shot for every stretch of the main passage and every "
               + "visit of a side passage, each with the stations it flies past, its length in seconds and how it "
               + "begins. The file is the form a film is kept and edited in (reorder or remove shots, change seconds "
               + "and texts) and rendered from (`therion-cli blender <model> --storyboard <file>`). Answers with what "
               + "plan_tour answers, and the file written. An existing file is kept unless overwrite is true.")]
    public async Task<ToolResult<TourPlanResult>> WriteTourStoryboard(
        [Description("Workspace-relative file to write, ending in .storyboard.json; its folder is created when missing.")]
        string storyboard,
        [Description("True replaces a file that is there. Defaults to false: the tool then refuses, so a storyboard edited by hand is not lost.")]
        bool overwrite = false,
        [Description(TourArguments.ModelHelp)] string? model = null,
        [Description("When planning for the project's own model and its build output is missing or out of date, export it with Therion first. Defaults to true.")]
        bool reExport = true,
        [Description(TourArguments.TrunkHelp)] string? trunk = null,
        [Description(TourArguments.StartHelp)] string? start = null,
        [Description(TourArguments.ViaHelp)] string[]? via = null,
        [Description(TourArguments.EndHelp)] string? end = null,
        [Description(TourArguments.TourSecondsHelp)] double tourSeconds = 0,
        [Description(TourArguments.BranchSecondsHelp)] double branchSeconds = 0,
        [Description(TourArguments.ModeHelp)] string? mode = null,
        [Description(TourArguments.ModesHelp)] string[]? modes = null,
        [Description(TourArguments.PeekShortHelp)] bool peekShort = false,
        [Description(TourArguments.MinLengthHelp)] double minLength = 0,
        [Description(TourArguments.MaxCountHelp)] int maxCount = -1,
        [Description(TourArguments.WithLeadsHelp)] bool withLeads = true,
        [Description(TourArguments.IncludeHelp)] string[]? include = null,
        [Description(TourArguments.ExcludeHelp)] string[]? exclude = null,
        [Description(TourArguments.DepthHelp)] int depth = 1,
        [Description(TourArguments.PresetHelp)] string? preset = null,
        [Description(TourArguments.LanguageHelp)] string? language = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<TourPlanResult>.Failure(error);
        string root = snapshot!.Root;

        if (!WorkspacePaths.TryResolve(root, storyboard, out var file, out var reason))
            return ToolResult<TourPlanResult>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);
        if (!file.EndsWith(Storyboard.FileExtension, StringComparison.OrdinalIgnoreCase))
            return ToolResult<TourPlanResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"A storyboard file's name ends in {Storyboard.FileExtension}, e.g. \"film/tour{Storyboard.FileExtension}\".");
        if (WorkspacePaths.IsProjectConfig(root, file))
            return ToolResult<TourPlanResult>.Failure(ToolErrorCodes.ProtectedPath,
                $"'{storyboard}' is in the project's config folder ({Workspace.ProjectLocalConfig.DirectoryName}/), "
                + "which holds the hooks and tasks that run commands. Pick another folder.");
        if (Directory.Exists(file))
            return ToolResult<TourPlanResult>.Failure(ToolErrorCodes.InvalidArgument, $"'{storyboard}' is a folder, not a file.");
        if (File.Exists(file) && !overwrite)
            return ToolResult<TourPlanResult>.Failure(ToolErrorCodes.FileExists,
                $"'{storyboard}' is there already. Pick another name, or pass overwrite: true to replace it.");

        var arguments = new TourArguments(
            model, trunk, start, via, end, tourSeconds, branchSeconds, mode, modes, peekShort, minLength, maxCount, withLeads, include, exclude,
            depth, preset, language);
        var (result, tour, refused) = await TourPlans.PlanAsync(root, blender, arguments, reExport, ct);
        if (refused is not null) return ToolResult<TourPlanResult>.Failure(refused);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            StoryboardSerializer.WriteFile(tour!.Storyboard, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<TourPlanResult>.Failure(ToolErrorCodes.WriteFailed, ex.Message);
        }
        return ToolResult<TourPlanResult>.Success(result! with { Storyboard = WorkspacePaths.ToRelative(root, file) });
    }
}
