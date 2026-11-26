using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Cave3D.Parsing;

namespace Therion.Mcp.Tools;

/// <summary>What a render writes, as start_render and export_render_script take it.</summary>
public enum RenderOutput
{
    /// <summary>A video file (H.264).</summary>
    Video,
    /// <summary>Numbered PNG frames.</summary>
    Frames,
    /// <summary>A single PNG.</summary>
    Still,
}

/// <param name="Script">The Blender Python script, workspace-relative. Run it with
/// <c>blender -b --factory-startup --python</c>.</param>
/// <param name="Directory">The folder it was written to, beside what it reads: the scene plan, the player and
/// the meshes and images, or the mesh and scene data of a generated script. Nothing beside it when it is one
/// file.</param>
/// <param name="Model">The model file converted: workspace-relative, or absolute outside the workspace.</param>
/// <param name="ModelKind">workspaceArtifact, externalFile, or reExported, as in render_status.</param>
/// <param name="StaleModel">Why the model may be out of date, when it is older than the sources.</param>
/// <param name="Frames">How many frames the script renders.</param>
/// <param name="Builder">What builds the scene when the script runs: player or generator.</param>
/// <param name="OneFile">True when the script carries everything it needs.</param>
/// <param name="LeftOut">The settings of the scene the script does not show: what the script generator does
/// not draw. Empty for the player.</param>
/// <param name="Warnings">What the scene shows less of than was asked for: labels thinned out, a terrain the
/// model does not have.</param>
public sealed record RenderScriptExport(
    string Script,
    string Directory,
    string Model,
    string ModelKind,
    string? StaleModel,
    int Frames,
    string? Builder = null,
    bool OneFile = false,
    IReadOnlyList<string>? LeftOut = null,
    IReadOnlyList<string>? Warnings = null);

/// <summary>
/// Rendering the cave model in Blender. Registered only on a host that can render (the running IDE),
/// and only in the full profile: a render runs Blender, may run Therion to export the model first, and
/// writes over earlier outputs of the same name.
/// </summary>
/// <remarks>
/// Neither Therion nor Blender gets anything from the project to run as a command: a re-export runs
/// Therion on a copy of the thconfig without its <c>system</c> lines, and Blender runs the script this
/// tool generated, or the player with the scene plan this tool wrote, with factory settings.
/// </remarks>
[McpServerToolType]
public sealed partial class BlenderRenderTools(IWorkspaceHost host, IBlenderRenderHost blender, BlenderRenderJobs jobs)
{
    /// <summary>How long cancel_render waits for Blender to stop before answering.</summary>
    private static readonly TimeSpan CancelWait = TimeSpan.FromSeconds(10);

    // destructiveHint: a render writes over an earlier one's outputs of the same name.
    [McpServerTool(Name = "start_render", Title = "Render with Blender",
        ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description("Starts rendering the cave model in Blender with a preset (see list_render_presets) and "
               + "answers at once with a job id; a render takes minutes to hours. Follow it with "
               + "render_status (waitSeconds waits for it), stop it with cancel_render. One render runs at "
               + "a time: a render started while another runs waits for its turn (state queued). The model is the project's own unless you name a .lox or .3d: its last build "
               + "output, or, when that is missing or older than the sources, a fresh export by Therion of "
               + "the model alone (the thconfig's system commands are left out). Outputs go to "
               + "outputDirectory in the workspace, or to a temporary folder that render_status names. "
               + "draft: true renders a quick preview first (seconds, not minutes): look at it before the "
               + "long render. title, credits, endCard and captions are written over the picture; a movie "
               + "with captions also gets them as .srt and .vtt subtitles. annotations names a JSON file of "
               + "labels, callouts, markers and photos that stand in the scene. reveal shows the cave part "
               + "by part while the clip runs: by survey date, survey order or distance from an entrance. "
               + "hdri lights the scene by an image all round it, which is also its background. storyboard renders a "
               + "film of several shots from a storyboard file instead of one scene, and a preset that is a composite "
               + "(list_render_presets, composites) renders its film of this cave.")]
    public async Task<ToolResult<RenderJobStatus>> StartRender(
        [Description("Preset name from list_render_presets. Defaults to \"Orbit showcase\".")]
        string preset = DefaultPreset,
        [Description("Workspace-relative .lox or .3d to render. Omit for the project's own model.")]
        string? model = null,
        [Description("video, frames, or still. Defaults to the preset's.")]
        string? outputKind = null,
        [Description("Width in pixels (even for video). 0 keeps the preset's.")]
        int width = 0,
        [Description("Height in pixels (even for video). 0 keeps the preset's.")]
        int height = 0,
        [Description("Length of a video or frame sequence, in seconds. 0 keeps the preset's.")]
        double durationSeconds = 0,
        [Description("Render samples per pixel: fewer is faster and noisier (16 makes a quick draft). 0 keeps the preset's.")]
        int samples = 0,
        [Description("Workspace-relative folder for the outputs; created when missing. Omit for a temporary folder.")]
        string? outputDirectory = null,
        [Description("File name the outputs start with, without extension. Defaults to the preset's (cave-render).")]
        string? baseName = null,
        [Description("When rendering the project's own model and its build output is missing or out of date, "
                   + "export it with Therion first. Defaults to true; false renders the build output as it is.")]
        bool reExport = true,
        [Description("True renders a quick draft instead: half the size, in Blender's solid view (no lights, no "
                   + "shadows), a fraction of a second per frame, written as <baseName>-draft. For checking the "
                   + "camera move, the framing and the texts before the long render.")]
        bool draft = false,
        [Description("What builds the scene in Blender: auto (the default: the player when it carries the whole "
                   + "scene, otherwise the script generator; render_status says which and why), player, or "
                   + "generator. A draft is always the player's.")]
        string? builder = null,
        [Description("A title written over the picture; {cave} in it stands for the cave's name, taken from the "
                   + "model. Omit to keep the preset's (the built-in presets write the cave's name); an empty "
                   + "text writes no title.")]
        string? title = null,
        [Description("A small line that stays in a corner of the picture: who surveyed, a licence.")]
        string? credits = null,
        [Description("The text of a card the movie ends on, over a black picture.")]
        string? endCard = null,
        [Description("How long the end card stays, in seconds; the movie is that much longer. 0 keeps the default (4).")]
        double endCardSeconds = 0,
        [Description("Captions shown at the bottom of the picture, each as \"<start>-<end>:<text>\" with the times in "
                   + "seconds from the start of the movie, e.g. \"2.5-6:Sala Mare\". One is shown at a time.")]
        string[]? captions = null,
        [Description("Workspace-relative JSON file of things that point something out in the scene: a list of "
                   + "{ \"kind\": \"label\" | \"callout\" | \"marker\" | \"photo\", \"station\": \"<name>\" or \"at\": [x, y, z] "
                   + "in the model's coordinates, \"text\", \"image\" (a .png or .jpg, relative to the file, inside the "
                   + "workspace), \"size\" in metres, and when it is there: \"from\" and \"to\" in seconds, or \"within\" "
                   + "metres of the camera, with \"fade\" }. Only the player draws them.")]
        string? annotations = null,
        [Description("Shows the cave part by part while the clip runs instead of all of it from the first frame: "
                   + "date (in the order of the survey dates, with a counter of the years: the history of the "
                   + "exploration), order (leg by leg, in the survey's own order) or distance (from the entrances "
                   + "inwards). Omit to keep the preset's; the preset \"Map reveal\" reveals by distance. Not in a "
                   + "still. Only the player draws it.")]
        string? reveal = null,
        [Description("With reveal: distance, the name of the station the distances are measured from, in place of "
                   + "the entrances.")]
        string? revealFrom = null,
        [Description("With reveal: date, a workspace-relative .3d file of the same cave that has the survey dates, "
                   + "for a model that has none: a .lox has the walls and no dates. Without it a .3d of the same "
                   + "name beside the model is used.")]
        string? revealDates = null,
        [Description(HdriHelp)]
        string? hdri = null,
        [Description(HdriStrengthHelp)]
        double? hdriStrength = null,
        [Description(HdriRotationHelp)]
        double? hdriRotation = null,
        [Description(StoryboardHelp)]
        string? storyboard = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<RenderJobStatus>.Failure(error);

        // A film of several shots: from a storyboard file, or a composite preset made into this cave's.
        var composite = Composite(preset);
        if (!string.IsNullOrWhiteSpace(storyboard) || composite is not null)
        {
            bool scene = !string.IsNullOrWhiteSpace(outputKind) || durationSeconds > 0 || samples > 0 || title is not null
                || credits is not null || endCard is not null || endCardSeconds > 0 || captions is { Length: > 0 } || annotations is not null
                || reveal is not null || revealFrom is not null || revealDates is not null || hdri is not null || hdriStrength is not null
                || hdriRotation is not null;
            if (scene)
                return ToolResult<RenderJobStatus>.Failure(ToolErrorCodes.InvalidArgument,
                    "A storyboard's shots say what they show: outputKind, durationSeconds, samples, the texts, annotations, reveal and hdri "
                    + "go with one scene. Say them in the storyboard; width, height, baseName and outputDirectory apply to the film.");
            if (!string.IsNullOrWhiteSpace(storyboard) && composite is not null)
                return ToolResult<RenderJobStatus>.Failure(ToolErrorCodes.InvalidArgument,
                    $"'{composite.Name}' is a composite preset, a film of its own: name it or a storyboard file, not both.");
            return StartFilm(snapshot!.Root, storyboard, composite, model, width, height, outputDirectory, baseName, reExport, draft, builder);
        }

        var (plan, refused) = Plan(snapshot!.Root, preset, model, outputKind, width, height, durationSeconds,
            samples, outputDirectory, baseName, reExport, needsDirectory: false,
            new RenderTexts(title, credits, endCard, endCardSeconds, captions), annotations,
            new RenderReveal(reveal, revealFrom, revealDates), new RenderLight(hdri, hdriStrength, hdriRotation));
        if (refused is not null) return ToolResult<RenderJobStatus>.Failure(refused);

        // Which builder, and whether it can build this scene, before a job is started for it.
        var chosen = SceneBuilder.Auto;
        if (!string.IsNullOrWhiteSpace(builder) && !ToolEnums.TryParse(builder, out chosen))
            return ToolResult<RenderJobStatus>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown builder '{builder}'. Use one of: {ToolEnums.Names<SceneBuilder>()}.");
        var options = new RenderOptions { Builder = chosen, Draft = draft };
        if (SceneBuilders.Choose(plan!.Spec, options).Refusal is { } refusal)
            return ToolResult<RenderJobStatus>.Failure(ToolErrorCodes.InvalidArgument, refusal);

        var job = new BlenderRenderJob(snapshot.Root, plan.Preset, plan.Spec, draft);
        string name = $"{plan.Spec.Output.BaseName} (agent {job.Id})";
        options = options with { JobName = name, Owner = "agent" };
        return Launch(job, name, plan.Request,
            (source, progress, token) => blender.Renderer.RenderAsync(plan.Spec, source, options, progress, token));
    }

    /// <summary>Starts <paramref name="job"/>: claims the host's render slot, finds the model and runs
    /// <paramref name="render"/> on it in the background.</summary>
    private ToolResult<RenderJobStatus> Launch(
        BlenderRenderJob job, string name, ModelSourceRequest request,
        Func<RenderSource, IProgress<RenderProgress>, CancellationToken, Task<RenderResult>> render)
    {
        if (!blender.TryBeginRender(name))
            return ToolResult<RenderJobStatus>.Failure(ToolErrorCodes.RenderBusy,
                "A render started in ThIDE's Blender panel is running, and renders run one at a time. "
                + "Try again when it has finished.");

        bool started = jobs.TryStart(job, async (running, token) =>
        {
            RenderResult? result = null;
            string? error = null;
            try
            {
                running.Report(new RenderProgress(RenderPhase.AcquiringSource, "Finding the model"));
                var source = await blender.AcquireAsync(request, token).ConfigureAwait(false);
                running.SetModel(source.Model);
                result = await render(source, new InlineProgress<RenderProgress>(running.Report), token).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                error = ex is OperationCanceledException ? "cancelled" : ex.Message;
                throw;
            }
            finally
            {
                blender.EndRender(name, result, error);
            }
        }, out var busy);

        if (started) return ToolResult<RenderJobStatus>.Success(job.Status());
        blender.EndRender(name, null, "not started");
        return Busy(busy!);

        static ToolResult<RenderJobStatus> Busy(BlenderRenderJob running) =>
            ToolResult<RenderJobStatus>.Failure(ToolErrorCodes.RenderBusy,
                $"{BlenderRenderJobs.Kept} renders have not ended yet, the oldest '{running.Id}'. Wait for them with "
                + "render_status (waitSeconds), or stop one with cancel_render, then start again.");
    }

    [McpServerTool(Name = "cancel_render", Title = "Cancel a Blender render",
        ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Stops a render started with start_render, Blender included, and answers with its status. "
               + "Outputs already written stay. Cancelling a render that has finished changes nothing.")]
    public async Task<ToolResult<RenderJobStatus>> CancelRender(
        [Description("The job id start_render returned. Defaults to the latest render.")]
        string? jobId = null,
        CancellationToken ct = default)
    {
        if (jobs.Find(jobId) is not { } job) return BlenderStatusTools.NotFound<RenderJobStatus>(jobId);

        job.Cancel();
        await Task.WhenAny(job.Completion, Task.Delay(CancelWait, ct));
        ct.ThrowIfCancellationRequested();
        return ToolResult<RenderJobStatus>.Success(job.Status());
    }

    // No dry run, as with run_build: writing the script is the whole point. destructiveHint because it
    // writes over an earlier export in the same folder.
    [McpServerTool(Name = "export_render_script", Title = "Export a Blender script",
        ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Writes the scene a render would show into a workspace folder, for someone to render with "
               + "Blender themselves — without running Blender. It is the scene start_render renders, made "
               + "by the same builder: by default the player's folder (scene-plan.json, the player in "
               + "thide_player/, the meshes and images, a README and render.sh / render.cmd, which start "
               + "Blender), or with oneFile one render.py that carries all of it. Run it with "
               + "`blender -b --factory-startup --python render.py`; what it renders goes to out/ beside "
               + "the script. builder: generator writes the earlier generated script instead (render.py, "
               + "model.ply, scene-meta.json; its outputs land beside it), which leaves out what leftOut "
               + "names. Takes the same preset, model, texts, annotations, reveal and hdri as start_render.")]
    public async Task<ToolResult<RenderScriptExport>> ExportRenderScript(
        [Description("Workspace-relative folder to write into; created when missing. Files of the same names are replaced.")]
        string outputDirectory,
        [Description("Preset name from list_render_presets. Defaults to \"Orbit showcase\".")]
        string preset = DefaultPreset,
        [Description("Workspace-relative .lox or .3d. Omit for the project's own model.")]
        string? model = null,
        [Description("video, frames, or still. Defaults to the preset's.")]
        string? outputKind = null,
        [Description("Width in pixels (even for video). 0 keeps the preset's.")]
        int width = 0,
        [Description("Height in pixels (even for video). 0 keeps the preset's.")]
        int height = 0,
        [Description("Length of a video or frame sequence, in seconds. 0 keeps the preset's.")]
        double durationSeconds = 0,
        [Description("Render samples per pixel. 0 keeps the preset's.")]
        int samples = 0,
        [Description("File name the outputs start with, without extension. Defaults to the preset's.")]
        string? baseName = null,
        [Description("Export the project's model with Therion first when its build output is missing or out of date. Defaults to true.")]
        bool reExport = true,
        [Description("What builds the scene when the script runs: auto or player (the default: the scene a "
                   + "render shows), or generator (the earlier generated script).")]
        string? builder = null,
        [Description("True writes one render.py that carries everything it needs (the scene, the player, the "
                   + "meshes and the images) instead of a folder: larger, and it renders anywhere.")]
        bool oneFile = false,
        [Description("A title written over the picture, as for start_render: {cave} stands for the cave's name; "
                   + "omitted, the preset's is kept; an empty text writes none.")]
        string? title = null,
        [Description("A small line that stays in a corner of the picture: who surveyed, a licence.")]
        string? credits = null,
        [Description("The text of a card the movie ends on, over a black picture.")]
        string? endCard = null,
        [Description("How long the end card stays, in seconds. 0 keeps the default (4).")]
        double endCardSeconds = 0,
        [Description("Captions, each as \"<start>-<end>:<text>\" with the times in seconds, as for start_render.")]
        string[]? captions = null,
        [Description("Workspace-relative JSON file of annotations (labels, callouts, markers, photos), as for start_render.")]
        string? annotations = null,
        [Description("Shows the cave part by part: date, order or distance, as for start_render.")]
        string? reveal = null,
        [Description("With reveal: distance, the station the distances are measured from.")]
        string? revealFrom = null,
        [Description("With reveal: date, a workspace-relative .3d file of the same cave that has the survey dates.")]
        string? revealDates = null,
        [Description(HdriHelp)]
        string? hdri = null,
        [Description(HdriStrengthHelp)]
        double? hdriStrength = null,
        [Description(HdriRotationHelp)]
        double? hdriRotation = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<RenderScriptExport>.Failure(error);
        var root = snapshot!.Root;

        var (plan, refused) = Plan(root, preset, model, outputKind, width, height, durationSeconds,
            samples, outputDirectory, baseName, reExport, needsDirectory: true,
            new RenderTexts(title, credits, endCard, endCardSeconds, captions), annotations,
            new RenderReveal(reveal, revealFrom, revealDates), new RenderLight(hdri, hdriStrength, hdriRotation));
        if (refused is not null) return ToolResult<RenderScriptExport>.Failure(refused);

        var chosen = SceneBuilder.Auto;
        if (!string.IsNullOrWhiteSpace(builder) && !ToolEnums.TryParse(builder, out chosen))
            return ToolResult<RenderScriptExport>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown builder '{builder}'. Use one of: {ToolEnums.Names<SceneBuilder>()}.");
        var options = new ExportOptions { Builder = chosen, SingleFile = oneFile };

        int reported = 0;
        var relay = new InlineProgress<RenderProgress>(p => progress?.Report(new ProgressNotificationValue
        {
            Progress = Interlocked.Increment(ref reported),
            Message = p.Message,
        }));

        try
        {
            var source = await blender.AcquireAsync(plan!.Request, ct).ConfigureAwait(false);
            var export = await blender.Renderer.ExportAsync(plan.Spec, source, plan.Spec.Output.OutputDirectory, options, relay, ct)
                .ConfigureAwait(false);
            return ToolResult<RenderScriptExport>.Success(new RenderScriptExport(
                Script: BlenderRenderJob.Display(root, export.ScriptPath),
                Directory: BlenderRenderJob.Display(root, plan.Spec.Output.OutputDirectory),
                Model: BlenderRenderJob.Display(root, source.Model.Path),
                ModelKind: BlenderRenderJob.Camel(source.Model.Kind.ToString()),
                StaleModel: source.Model.StalenessReason,
                Frames: export.FrameCount,
                Builder: BlenderRenderJob.Camel(export.Builder.ToString()),
                OneFile: export.SingleFile,
                LeftOut: export.LeftOut,
                Warnings: export.Notices));
        }
        catch (OperationCanceledException)
        {
            throw;   // the host asked us to stop
        }
        catch (ModelSourceNotFoundException ex)
        {
            return ToolResult<RenderScriptExport>.Failure(ToolErrorCodes.ModelUnavailable, ex.Message);
        }
        catch (CaveFileFormatException ex)
        {
            return ToolResult<RenderScriptExport>.Failure(ToolErrorCodes.ReadFailed, "The model file cannot be read: " + ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ToolResult<RenderScriptExport>.Failure(ToolErrorCodes.InvalidArgument, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<RenderScriptExport>.Failure(ToolErrorCodes.WriteFailed, ex.Message);
        }
    }

    private const string DefaultPreset = "Orbit showcase";

    /// <summary>A checked request: the preset's scene with the caller's overrides, and the model to find.</summary>
    private sealed record RenderPlan(string Preset, SceneSpec Spec, ModelSourceRequest Request);

    /// <summary>The texts over the picture a caller adds to the preset's.</summary>
    private sealed record RenderTexts(string? Title, string? Credits, string? EndCard, double EndCardSeconds, string[]? Captions);

    /// <summary>The reveal a caller asks for, over the preset's.</summary>
    private sealed record RenderReveal(string? By, string? From, string? Dates);

    /// <summary>The image a caller lights the scene by, over the preset's lighting.</summary>
    private sealed record RenderLight(string? Image, double? Strength, double? Rotation);

    private const string HdriHelp =
        "Workspace-relative image the scene is lit by, laid all round it and seen behind the cave (an HDRI): a "
        + "Radiance .hdr or an OpenEXR .exr file. Omit to keep the preset's lighting.";

    private const string HdriStrengthHelp =
        "With an hdri image: how brightly it shines; 1 is the image as it is. Omit to keep the preset's.";

    private const string HdriRotationHelp =
        "With an hdri image: how far it is turned about the vertical, in degrees, anticlockwise seen from above. "
        + "Omit to keep the preset's.";

    /// <summary>
    /// The scene and model request for a render or export, or the error to answer with. Every path is
    /// held to the workspace; the output folder may not be the project's config folder.
    /// </summary>
    private (RenderPlan? Plan, ToolError? Error) Plan(
        string root, string preset, string? model, string? outputKind, int width, int height,
        double durationSeconds, int samples, string? outputDirectory, string? baseName, bool reExport,
        bool needsDirectory, RenderTexts? texts = null, string? annotations = null, RenderReveal? reveal = null,
        RenderLight? light = null)
    {
        static (RenderPlan?, ToolError?) Refuse(string code, string message) => (null, new ToolError(code, message));

        var presets = BlenderStatusTools.Presets(blender);
        var name = string.IsNullOrWhiteSpace(preset) ? DefaultPreset : preset.Trim();
        var chosen = presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
            return Refuse(ToolErrorCodes.InvalidArgument,
                $"Unknown preset '{preset}'. Use one of: {string.Join(", ", presets.Select(p => $"\"{p.Name}\""))}.");

        var spec = chosen.Spec;
        if (!string.IsNullOrWhiteSpace(outputKind))
        {
            if (!ToolEnums.TryParse<RenderOutput>(outputKind, out var kind))
                return Refuse(ToolErrorCodes.InvalidArgument,
                    $"Unknown output kind '{outputKind}'. Use one of: {ToolEnums.Names<RenderOutput>()}.");
            spec = spec with { Output = spec.Output with { Kind = kind switch
            {
                RenderOutput.Video => OutputKind.Video,
                RenderOutput.Frames => OutputKind.FrameSequence,
                _ => OutputKind.Still,
            } } };
        }
        if (width > 0) spec = spec with { Output = spec.Output with { Width = width } };
        if (height > 0) spec = spec with { Output = spec.Output with { Height = height } };
        if (durationSeconds > 0) spec = spec with { Animation = spec.Animation with { DurationSeconds = durationSeconds } };
        if (samples > 0) spec = spec with { Engine = spec.Engine with { Samples = samples } };
        if (!string.IsNullOrWhiteSpace(baseName)) spec = spec with { Output = spec.Output with { BaseName = baseName.Trim() } };
        if (texts is not null)
        {
            var overlays = spec.Labels.Overlays;
            // A title that is given and empty takes the preset's away.
            if (texts.Title is not null) overlays = overlays with { Title = string.IsNullOrWhiteSpace(texts.Title) ? null : texts.Title.Trim() };
            if (!string.IsNullOrWhiteSpace(texts.Credits)) overlays = overlays with { Credits = texts.Credits.Trim() };
            if (!string.IsNullOrWhiteSpace(texts.EndCard))
                overlays = overlays with { EndCard = (overlays.EndCard ?? new EndCardSpec()) with { Text = texts.EndCard.Trim() } };
            if (texts.EndCardSeconds > 0)
            {
                if (overlays.EndCard is null)
                    return Refuse(ToolErrorCodes.InvalidArgument, "endCardSeconds goes with an end card: give its text (endCard).");
                overlays = overlays with { EndCard = overlays.EndCard with { Seconds = texts.EndCardSeconds } };
            }
            var captions = new List<CaptionSpec>();
            foreach (string line in texts.Captions ?? [])
            {
                if (!Subtitles.TryParseCaption(line, out var caption))
                    return Refuse(ToolErrorCodes.InvalidArgument,
                        $"'{line}' is not a caption. Write each as \"<start>-<end>:<text>\" with the times in seconds, e.g. \"2.5-6:Sala Mare\".");
                captions.Add(caption);
            }
            if (captions.Count > 0) overlays = overlays with { Captions = [.. overlays.Captions, .. captions] };
            spec = spec with { Labels = spec.Labels with { Overlays = overlays } };
        }

        if (!string.IsNullOrWhiteSpace(annotations))
        {
            if (!WorkspacePaths.TryResolve(root, annotations, out var file, out var reason))
                return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (!File.Exists(file))
                return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {annotations}");
            IReadOnlyList<AnnotationSpec> read;
            try { read = AnnotationsFile.ReadFile(file); }
            catch (SceneSpecFormatException ex)
            {
                return Refuse(ToolErrorCodes.InvalidArgument, $"'{annotations}' is not a list of annotations: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Refuse(ToolErrorCodes.ReadFailed, ex.Message);
            }
            // A photo is read from the workspace, like everything else an agent names.
            var held = new List<AnnotationSpec>(read.Count);
            foreach (var annotation in read)
            {
                if (string.IsNullOrWhiteSpace(annotation.Image))
                {
                    held.Add(annotation);
                    continue;
                }
                if (!WorkspacePaths.TryResolve(root, annotation.Image, out var photo, out _))
                    return Refuse(ToolErrorCodes.PathOutsideWorkspace,
                        $"The photo '{Path.GetFileName(annotation.Image)}' of '{annotations}' lies outside the workspace.");
                if (!File.Exists(photo))
                    return Refuse(ToolErrorCodes.FileNotFound,
                        $"No such file: {WorkspacePaths.ToRelative(root, photo)} (a photo of '{annotations}').");
                held.Add(annotation with { Image = photo });
            }
            spec = spec with { Annotations = [.. spec.Annotations, .. held] };
        }

        // A file with survey dates is read from the workspace, like everything else an agent names; one
        // that a user preset carries from elsewhere is left where it is.
        if (spec.Reveal is { DatesPath: { } carried } && !WorkspacePaths.TryResolve(root, carried, out _, out _))
            spec = spec with { Reveal = spec.Reveal with { DatesPath = null } };
        if (reveal is not null && (!string.IsNullOrWhiteSpace(reveal.By) || !string.IsNullOrWhiteSpace(reveal.From) || !string.IsNullOrWhiteSpace(reveal.Dates)))
        {
            var asked = spec.Reveal ?? new RevealSpec();
            if (!string.IsNullOrWhiteSpace(reveal.By))
            {
                if (!ToolEnums.TryParse<RevealBy>(reveal.By, out var by))
                    return Refuse(ToolErrorCodes.InvalidArgument, $"Unknown reveal '{reveal.By}'. Use one of: {ToolEnums.Names<RevealBy>()}.");
                asked = asked with { By = by, From = by == RevealBy.Distance ? asked.From : null, DatesPath = by == RevealBy.Date ? asked.DatesPath : null };
            }
            else if (spec.Reveal is null)
            {
                return Refuse(ToolErrorCodes.InvalidArgument, "revealFrom and revealDates go with a reveal: say which (reveal: date, order or distance).");
            }
            if (!string.IsNullOrWhiteSpace(reveal.From))
            {
                if (asked.By != RevealBy.Distance)
                    return Refuse(ToolErrorCodes.InvalidArgument, "revealFrom names the station a reveal by distance is measured from (reveal: distance).");
                asked = asked with { From = reveal.From.Trim() };
            }
            if (!string.IsNullOrWhiteSpace(reveal.Dates))
            {
                if (asked.By != RevealBy.Date)
                    return Refuse(ToolErrorCodes.InvalidArgument, "revealDates names the file with the survey dates of a reveal by date (reveal: date).");
                if (!WorkspacePaths.TryResolve(root, reveal.Dates, out var dates, out var reason))
                    return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
                if (!File.Exists(dates))
                    return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {reveal.Dates}");
                if (!dates.EndsWith(".3d", StringComparison.OrdinalIgnoreCase) && !dates.EndsWith(".lox", StringComparison.OrdinalIgnoreCase))
                    return Refuse(ToolErrorCodes.InvalidArgument, $"'{reveal.Dates}' is not a model file (.3d or .lox).");
                asked = asked with { DatesPath = dates };
            }
            spec = spec with { Reveal = asked };
        }

        // The image a scene is lit by is read from the workspace, like a photo: an agent names no file
        // outside it. One that a user preset carries stays the user's own choice.
        if (light is not null && (!string.IsNullOrWhiteSpace(light.Image) || light.Strength is not null || light.Rotation is not null))
        {
            var lighting = spec.Lighting;
            if (!string.IsNullOrWhiteSpace(light.Image))
            {
                if (!WorkspacePaths.TryResolve(root, light.Image, out var image, out var reason))
                    return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
                if (!File.Exists(image))
                    return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {light.Image}");
                if (!Therion.Blender.Plan.PlanEnvironmentImage.HasExtension(image))
                    return Refuse(ToolErrorCodes.InvalidArgument,
                        $"'{light.Image}' is not an image a scene is lit by: name a Radiance .hdr or an OpenEXR .exr file.");
                lighting = lighting with { Rig = LightingRig.HdriFile, HdriPath = image };
            }
            else if (lighting.Rig != LightingRig.HdriFile)
            {
                return Refuse(ToolErrorCodes.InvalidArgument,
                    "hdriStrength and hdriRotation go with an image the scene is lit by: name it (hdri).");
            }
            if (light.Strength is { } strength) lighting = lighting with { Strength = strength };
            if (light.Rotation is { } rotation) lighting = lighting with { HdriRotation = rotation };
            spec = spec with { Lighting = lighting };
        }

        // Always replaced: a user preset may carry the folder it was saved with, which is no business of
        // an agent's render.
        string directory = "";
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            if (!WorkspacePaths.TryResolve(root, outputDirectory, out var full, out var reason))
                return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (WorkspacePaths.IsProjectConfig(root, full))
                return Refuse(ToolErrorCodes.ProtectedPath,
                    $"'{outputDirectory}' is in the project's config folder ({Workspace.ProjectLocalConfig.DirectoryName}/), "
                    + "which holds the hooks and tasks that run commands. Pick another folder.");
            if (File.Exists(full))
                return Refuse(ToolErrorCodes.InvalidArgument, $"'{outputDirectory}' is a file, not a folder.");
            directory = full;
        }
        else if (needsDirectory)
        {
            return Refuse(ToolErrorCodes.InvalidArgument, "Give the workspace-relative folder to write into (outputDirectory).");
        }
        spec = spec with { Output = spec.Output with { OutputDirectory = directory } };

        // The model mesh and the output folder are filled in by the render service; stand-ins let the
        // rest of the scene be checked now, rather than after a model export.
        var probe = spec with
        {
            Source = spec.Source with { PlyPath = "model.ply" },
            Output = spec.Output with { OutputDirectory = directory.Length > 0 ? directory : "out" },
        };
        if (SceneSpecValidator.Validate(probe) is { Count: > 0 } problems)
            return Refuse(ToolErrorCodes.InvalidArgument,
                "The render settings are not valid: " + string.Join("; ", problems.Select(p => $"{p.Path}: {p.Message}")));

        var (request, wrong) = ModelRequest(root, model, reExport);
        if (wrong is not null) return (null, wrong);
        return (new RenderPlan(chosen.Name, spec, request!), null);
    }
}
