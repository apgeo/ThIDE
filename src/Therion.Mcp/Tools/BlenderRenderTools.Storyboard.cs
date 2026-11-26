// start_render with a film of several shots: a storyboard file of the workspace, or a composite preset
// (a storyboard template, made into a film of this cave when the model is read). Every file the
// storyboard names is held to the workspace, as an annotations file's photos are; the presets its
// shots name are the built-in ones and the user's, and the user's are written into the shots before
// the film is queued, so that it renders the same if a restart starts it again.

using ModelContextProtocol;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;

namespace Therion.Mcp.Tools;

public sealed partial class BlenderRenderTools
{
    internal const string StoryboardHelp =
        "Workspace-relative storyboard file (*.storyboard.json): a film of several shots, each a camera move with a look of its "
        + "own, joined by cuts, dips or dissolves, with texts over a shot or the whole film. The files it names (photos, HDRI "
        + "images, survey dates) are relative to it and inside the workspace. write_tour_storyboard writes one. Rendered by the "
        + "player, queued like any render; draft drafts every shot.";

    /// <summary>The composite preset of that name: the built-in ones, then the user's.</summary>
    private CompositePreset? Composite(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return BlenderStatusTools.Composites(blender).FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private ToolResult<RenderJobStatus> StartFilm(
        string root, string? file, CompositePreset? composite, string? model, int width, int height, string? outputDirectory,
        string? baseName, bool reExport, bool draft, string? builder)
    {
        static ToolResult<RenderJobStatus> Refuse(string code, string message) => ToolResult<RenderJobStatus>.Failure(code, message);

        if (!string.IsNullOrWhiteSpace(builder) && (!ToolEnums.TryParse(builder, out SceneBuilder chosen) || chosen == SceneBuilder.Generator))
            return Refuse(ToolErrorCodes.InvalidArgument, "A storyboard is rendered by the player: builder is auto or player.");

        Storyboard storyboard;
        string label;
        if (composite is not null)
        {
            storyboard = composite.Storyboard;
            label = composite.Name;
        }
        else
        {
            if (!WorkspacePaths.TryResolve(root, file!, out var path, out var reason))
                return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (!File.Exists(path)) return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {file}");
            try { storyboard = StoryboardSerializer.ReadFile(path); }
            catch (StoryboardFormatException ex)
            {
                return Refuse(ToolErrorCodes.InvalidArgument, $"'{file}' is not a storyboard: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Refuse(ToolErrorCodes.ReadFailed, ex.Message);
            }
            // What it names is read from the workspace, like everything else an agent names.
            storyboard = StoryboardFiles.Rooted(storyboard, Path.GetDirectoryName(path)!);
            foreach (string named in StoryboardFiles.Named(storyboard))
            {
                if (!WorkspacePaths.TryResolve(root, named, out var held, out _))
                    return Refuse(ToolErrorCodes.PathOutsideWorkspace, $"The file '{Path.GetFileName(named)}' that '{file}' names lies outside the workspace.");
                if (!File.Exists(held))
                    return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {WorkspacePaths.ToRelative(root, held)} (named by '{file}').");
            }
            label = WorkspacePaths.ToRelative(root, path);
        }

        var presets = BlenderStatusTools.Presets(blender);
        RenderPreset? Find(string name) => presets.FirstOrDefault(p => p.Name == name);
        if (StoryboardValidator.Validate(storyboard, Find) is { Count: > 0 } errors)
            return Refuse(ToolErrorCodes.InvalidArgument,
                "The storyboard is not valid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
        storyboard = StoryboardExpander.InlinePresets(storyboard, Find);

        // The output folder is always the agent's: a storyboard may name one of its own.
        string directory = "";
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            if (!WorkspacePaths.TryResolve(root, outputDirectory, out var full, out var reason))
                return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (WorkspacePaths.IsProjectConfig(root, full))
                return Refuse(ToolErrorCodes.ProtectedPath,
                    $"'{outputDirectory}' is in the project's config folder ({Workspace.ProjectLocalConfig.DirectoryName}/), "
                    + "which holds the hooks and tasks that run commands. Pick another folder.");
            if (File.Exists(full)) return Refuse(ToolErrorCodes.InvalidArgument, $"'{outputDirectory}' is a file, not a folder.");
            directory = full;
        }
        var output = storyboard.Output with { OutputDirectory = directory };
        if (width > 0) output = output with { Width = width };
        if (height > 0) output = output with { Height = height };
        if (!string.IsNullOrWhiteSpace(baseName)) output = output with { BaseName = baseName.Trim() };
        storyboard = storyboard with { Output = output };
        if (StoryboardValidator.Validate(storyboard with { Output = output with { OutputDirectory = directory.Length > 0 ? directory : "out" } })
            is { Count: > 0 } sized)
            return Refuse(ToolErrorCodes.InvalidArgument,
                "The film's settings are not valid: " + string.Join("; ", sized.Select(e => $"{e.Path}: {e.Message}")));

        var (request, wrong) = ModelRequest(root, model, reExport);
        if (wrong is not null) return ToolResult<RenderJobStatus>.Failure(wrong);

        // What render_status says of the film before it has ended: its output, and its length as the
        // shots say it (a tour's is known once the model is read).
        double seconds = Enumerable.Range(0, storyboard.Shots.Count).Sum(i => StoryboardPlanner.ShotSpec(storyboard, i).Animation.DurationSeconds);
        var standIn = new SceneSpec { Output = output, Animation = new AnimationSpec { Fps = storyboard.Fps, DurationSeconds = seconds } };
        var job = new BlenderRenderJob(root, label, standIn, draft);
        string name = $"{output.BaseName} (agent {job.Id})";
        var options = new RenderOptions { Draft = draft, JobName = name, Owner = "agent" };
        var film = storyboard;
        return Launch(job, name, request!, (source, progress, token) => blender.Renderer.RenderStoryboardAsync(film, source, options, progress, token));
    }

    /// <summary>The model a render reads: a workspace file the agent names, or the project's own.</summary>
    private static (ModelSourceRequest? Request, ToolError? Error) ModelRequest(string root, string? model, bool reExport)
    {
        static (ModelSourceRequest?, ToolError?) Refuse(string code, string message) => (null, new ToolError(code, message));
        if (string.IsNullOrWhiteSpace(model)) return (ModelSourceRequest.ForWorkspace(allowReExport: reExport), null);
        if (!WorkspacePaths.TryResolve(root, model, out var file, out var reason))
            return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
        if (!File.Exists(file))
            return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {model}");
        var ext = Path.GetExtension(file);
        if (!ext.Equals(".lox", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".3d", StringComparison.OrdinalIgnoreCase))
            return Refuse(ToolErrorCodes.InvalidArgument,
                $"'{model}' is not a model Blender can render. Name a Therion .lox or a Survex .3d, or omit "
                + "model to render the project's own.");
        return (ModelSourceRequest.ForExternalFile(file), null);
    }
}
