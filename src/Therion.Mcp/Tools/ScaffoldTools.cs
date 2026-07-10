using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Mcp.Mutations;
using Therion.Syntax;
using Therion.Workspace.Import;

namespace Therion.Mcp.Tools;

/// <param name="InputLine">The <c>input</c> line that pulls the new sketch into a survey.</param>
/// <param name="AddedTo">The .th the input line was appended to, or null when the caller must add it.</param>
public sealed record Th2ScaffoldResult(string InputLine, string? AddedTo, MutationResult Mutation);

/// <param name="Thconfig">Workspace-relative path of the generated thconfig — the project's entry point.</param>
/// <param name="Survey">The survey name found inside the source file, which the wrapper inputs.</param>
public sealed record ProjectScaffoldResult(
    string Thconfig,
    string Survey,
    bool Georeferenced,
    MutationResult Mutation);

/// <summary>Ring R2 — creating the files a new sketch or a new project needs.</summary>
[McpServerToolType]
public sealed class ScaffoldTools(WorkspaceHost host, MutationEngine mutations)
{
    private static readonly string[] Projections = ["plan", "elevation", "extended", "none"];

    // Nothing here overwrites: every change is a create, and the engine refuses a create whose target
    // exists. Hence destructiveHint:false — unusual for a writing tool, and true.
    [McpServerTool(Name = "scaffold_th2", Title = "Scaffold sketch",
        ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Creates an empty .th2 sketch holding one scrap, optionally wired to an .xvi "
               + "background image, and hands back the 'input' line that pulls it into a survey. "
               + "Give addInputTo to have that line appended for you. Never overwrites anything.")]
    public async Task<ToolResult<Th2ScaffoldResult>> ScaffoldTh2(
        [Description("Workspace-relative path of the .th2 to create, e.g. 'caves/upper-plan.th2'.")]
        string path,
        [Description("Id of the scrap inside it. Defaults to the file's name.")]
        string? scrapId = null,
        [Description("plan, elevation, extended, or none. Defaults to plan.")]
        string projection = "plan",
        [Description("Workspace-relative .xvi to trace over, if there is one.")]
        string? sketchXvi = null,
        [Description("Workspace-relative .th to append the 'input' line to. Omit to add it yourself.")]
        string? addInputTo = null,
        [Description("Preview without writing. Defaults to true — pass false to create the files.")]
        bool dryRun = true,
        CancellationToken ct = default)
    {
        if (!Projections.Contains(projection, StringComparer.OrdinalIgnoreCase))
            return Fail<Th2ScaffoldResult>(ToolErrorCodes.InvalidArgument,
                $"Unknown projection '{projection}'. Use one of: {string.Join(", ", Projections)}.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<Th2ScaffoldResult>.Failure(error);

        var root = snapshot!.Root;

        if (!WorkspacePaths.TryResolve(root, path, out var full, out var reason))
            return Fail<Th2ScaffoldResult>(ToolErrorCodes.PathOutsideWorkspace, reason);

        if (!Path.GetExtension(full).Equals(".th2", StringComparison.OrdinalIgnoreCase))
            return Fail<Th2ScaffoldResult>(ToolErrorCodes.InvalidArgument, "A sketch file must be named '.th2'.");

        string? xviRelative = null;
        if (!string.IsNullOrWhiteSpace(sketchXvi))
        {
            if (!WorkspacePaths.TryResolve(root, sketchXvi, out var xvi, out var xviReason))
                return Fail<Th2ScaffoldResult>(ToolErrorCodes.PathOutsideWorkspace, xviReason);
            if (!File.Exists(xvi))
                return Fail<Th2ScaffoldResult>(ToolErrorCodes.FileNotFound, $"No such sketch image: {sketchXvi}");

            xviRelative = RelativeTo(Path.GetDirectoryName(full)!, xvi);
        }

        var id = string.IsNullOrWhiteSpace(scrapId) ? Path.GetFileNameWithoutExtension(full) : scrapId;
        var changes = new List<FileChange> { new CreateFile(full, Th2Scaffold.NewScrap(id, projection, xviRelative)) };

        string? addedTo = null;
        string inputLine;

        if (!string.IsNullOrWhiteSpace(addInputTo))
        {
            if (!WorkspacePaths.TryResolve(root, addInputTo, out var survey, out var surveyReason))
                return Fail<Th2ScaffoldResult>(ToolErrorCodes.PathOutsideWorkspace, surveyReason);
            if (!File.Exists(survey))
                return Fail<Th2ScaffoldResult>(ToolErrorCodes.FileNotFound, $"No such file: {addInputTo}");

            inputLine = Th2Scaffold.InputLine(RelativeTo(Path.GetDirectoryName(survey)!, full));

            string text;
            try { text = EncodingResolver.ReadAllText(survey); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail<Th2ScaffoldResult>(ToolErrorCodes.ReadFailed, ex.Message);
            }

            // Append at the very end: a zero-length edit whose expected text is empty, so the engine's
            // slice guard still fires if the file grew between the plan and the apply.
            var appended = (text.EndsWith('\n') ? "" : "\n") + inputLine + "\n";
            changes.Add(new EditFile(survey, [new TextEdit(text.Length, 0, "", appended)]));
            addedTo = WorkspacePaths.ToRelative(root, survey);
        }
        else
        {
            inputLine = Th2Scaffold.InputLine(Path.GetFileName(full));
        }

        var applied = await mutations.ApplyAsync(new MutationPlan(changes), dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<Th2ScaffoldResult>.Failure(failure);

        return ToolResult<Th2ScaffoldResult>.Success(new Th2ScaffoldResult(inputLine, addedTo, applied.Data!));
    }

    [McpServerTool(Name = "scaffold_topodroid_project", Title = "Scaffold TopoDroid project",
        ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Builds a compilable Therion project around a bare TopoDroid survey export: a "
               + "directory tree, a wrapper .th that inputs the survey and can georeference it, and "
               + "a thconfig with a layout. The survey itself is copied, never moved or rewritten. "
               + "Never overwrites anything, and refuses outright if the wrapper would land on the "
               + "source survey.")]
    public async Task<ToolResult<ProjectScaffoldResult>> ScaffoldTopodroidProject(
        [Description("Workspace-relative path of the TopoDroid .th survey to wrap.")]
        string source,
        [Description("Workspace-relative directory to build the project in. Created if absent.")]
        string targetDir,
        [Description("Project name, used for the wrapper survey, the layout and the output files.")]
        string projectName,
        [Description("Human-readable cave title, e.g. 'Peștera Meziad'.")]
        string? title = null,
        [Description("Station to mark as the entrance, e.g. '25'. Taken from the survey if it names one.")]
        string? entranceStation = null,
        [Description("Coordinate system for the fix, e.g. 'lat-long' or 'EPSG:31700'. Defaults to lat-long.")]
        string coordinateSystem = "lat-long",
        [Description("First fix coordinate (latitude for lat-long). Give all three to georeference the cave.")]
        string? fixC1 = null,
        [Description("Second fix coordinate (longitude for lat-long).")]
        string? fixC2 = null,
        [Description("Altitude in metres. Defaults to 0.")]
        string? fixC3 = null,
        [Description("Map scale denominator, e.g. 500 for 1:500. Defaults to 500.")]
        int scale = 500,
        [Description("Preview without writing. Defaults to true — pass false to create the files.")]
        bool dryRun = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.InvalidArgument, "No project name given.");
        if (scale <= 0)
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.InvalidArgument, "Scale must be positive.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ProjectScaffoldResult>.Failure(error);

        var root = snapshot!.Root;

        if (!WorkspacePaths.TryResolve(root, source, out var sourceFull, out var sourceReason))
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.PathOutsideWorkspace, sourceReason);
        if (!File.Exists(sourceFull))
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.FileNotFound, $"No such survey: {source}");

        if (!WorkspacePaths.TryResolve(root, targetDir, out var targetFull, out var targetReason))
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.PathOutsideWorkspace, targetReason);

        string surveyText;
        try { surveyText = EncodingResolver.ReadAllText(sourceFull); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.ReadFailed, ex.Message);
        }

        var info = TopodroidProjectScaffold.Parse(surveyText);
        if (string.IsNullOrWhiteSpace(info.SurveyName))
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.InvalidArgument,
                $"'{source}' has no 'survey' command, so there is nothing to wrap.");

        var options = new ScaffoldOptions
        {
            ProjectName = projectName,
            InnerSurveyName = info.SurveyName,
            SourceFileName = Path.GetFileName(sourceFull),
            Title = string.IsNullOrWhiteSpace(title) ? info.Title : title,
            EntranceStation = string.IsNullOrWhiteSpace(entranceStation) ? info.EntranceHint : entranceStation,
            CoordinateSystem = coordinateSystem,
            FixC1 = fixC1 ?? "",
            FixC2 = fixC2 ?? "",
            FixC3 = string.IsNullOrWhiteSpace(fixC3) ? "0" : fixC3,
            Scale = scale,
        };

        bool georeferenced = !string.IsNullOrWhiteSpace(fixC1) && !string.IsNullOrWhiteSpace(fixC2);
        var plan = TopodroidProjectScaffold.BuildPlan(options);

        // Scaffolding into the survey's own directory makes the wrapper land on the survey itself,
        // replacing the data with a handful of `input` lines. The library knows how to spot that.
        if (TopodroidProjectScaffold.ConflictsWithSource(plan, targetFull, sourceFull) is { Count: > 0 } clashes)
            return Fail<ProjectScaffoldResult>(ToolErrorCodes.FileExists,
                $"The generated {string.Join(", ", clashes)} would overwrite the source survey itself. "
                + "Scaffold into a different directory.");

        var changes = new List<FileChange> { new CreateDirectory(targetFull) };
        foreach (var directory in plan.Directories)
            changes.Add(new CreateDirectory(Path.Combine(targetFull, directory)));
        foreach (var file in plan.Files)
            changes.Add(new CreateFile(Path.Combine(targetFull, file.RelativePath), file.Content));

        // Copied byte for byte: the survey may declare its own encoding, and re-encoding the copy as
        // UTF-8 would leave that declaration lying about the bytes.
        changes.Add(new CopyFile(Path.Combine(targetFull, plan.SourceCopyRelativePath), sourceFull));

        var applied = await mutations.ApplyAsync(new MutationPlan(changes), dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<ProjectScaffoldResult>.Failure(failure);

        return ToolResult<ProjectScaffoldResult>.Success(new ProjectScaffoldResult(
            Thconfig: WorkspacePaths.ToRelative(root, Path.Combine(targetFull, plan.ThconfigRelativePath)),
            Survey: info.SurveyName,
            Georeferenced: georeferenced,
            Mutation: applied.Data!));
    }

    /// <summary>A Therion <c>input</c>/<c>-sketch</c> path: relative to the referring file, forward slashes.</summary>
    private static string RelativeTo(string fromDirectory, string target) =>
        Path.GetRelativePath(fromDirectory, target).Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

    private static ToolResult<T> Fail<T>(string code, string message) => ToolResult<T>.Failure(code, message);
}
