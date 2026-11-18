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

/// <param name="Script">The Blender Python script, workspace-relative. Run it with <c>blender -b --python</c>.</param>
/// <param name="Directory">The folder it was written to, beside the model mesh and scene data it reads.</param>
/// <param name="Model">The model file converted: workspace-relative, or absolute outside the workspace.</param>
/// <param name="ModelKind">workspaceArtifact, externalFile, or reExported, as in render_status.</param>
/// <param name="StaleModel">Why the model may be out of date, when it is older than the sources.</param>
/// <param name="Frames">How many frames the script renders.</param>
public sealed record RenderScriptExport(
    string Script,
    string Directory,
    string Model,
    string ModelKind,
    string? StaleModel,
    int Frames);

/// <summary>
/// Rendering the cave model in Blender. Registered only on a host that can render (the running IDE),
/// and only in the full profile: a render runs Blender, may run Therion to export the model first, and
/// writes over earlier outputs of the same name.
/// </summary>
/// <remarks>
/// Neither Therion nor Blender gets anything from the project to run as a command: a re-export runs
/// Therion on a copy of the thconfig without its <c>system</c> lines, and Blender runs the script this
/// tool generated, with factory settings.
/// </remarks>
[McpServerToolType]
public sealed class BlenderRenderTools(IWorkspaceHost host, IBlenderRenderHost blender, BlenderRenderJobs jobs)
{
    /// <summary>How long cancel_render waits for Blender to stop before answering.</summary>
    private static readonly TimeSpan CancelWait = TimeSpan.FromSeconds(10);

    // destructiveHint: a render writes over an earlier one's outputs of the same name.
    [McpServerTool(Name = "start_render", Title = "Render with Blender",
        ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description("Starts rendering the cave model in Blender with a preset (see list_render_presets) and "
               + "answers at once with a job id; a render takes minutes to hours. Follow it with "
               + "render_status (waitSeconds waits for it), stop it with cancel_render. One render runs at "
               + "a time. The model is the project's own unless you name a .lox or .3d: its last build "
               + "output, or, when that is missing or older than the sources, a fresh export by Therion of "
               + "the model alone (the thconfig's system commands are left out). Outputs go to "
               + "outputDirectory in the workspace, or to a temporary folder that render_status names.")]
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
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<RenderJobStatus>.Failure(error);

        var (plan, refused) = Plan(snapshot!.Root, preset, model, outputKind, width, height, durationSeconds,
            samples, outputDirectory, baseName, reExport, needsDirectory: false);
        if (refused is not null) return ToolResult<RenderJobStatus>.Failure(refused);

        var job = new BlenderRenderJob(snapshot.Root, plan!.Preset, plan.Spec);
        string name = $"{plan.Spec.Output.BaseName} (agent {job.Id})";
        if (jobs.Find(null) is { IsFinished: false } running)
            return Busy(running);
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
                var source = await blender.AcquireAsync(plan.Request, token).ConfigureAwait(false);
                running.SetModel(source.Model);
                result = await blender.Renderer.RenderAsync(plan.Spec, source, new InlineProgress<RenderProgress>(running.Report), token)
                    .ConfigureAwait(false);
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
                $"Render '{running.Id}' is still running, and renders run one at a time. Wait for it with "
                + "render_status (waitSeconds), or stop it with cancel_render, then start again.");
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
    [Description("Writes the Blender Python script a render would run, with the model mesh and scene data "
               + "it reads, into a workspace folder — without running Blender. Read the script, or hand it "
               + "to someone to run with `blender -b --python render.py`. Takes the same preset, model and "
               + "overrides as start_render; the outputs of running it land beside the script.")]
    public async Task<ToolResult<RenderScriptExport>> ExportRenderScript(
        [Description("Workspace-relative folder to write render.py, model.ply and scene-meta.json into; created when missing.")]
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
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<RenderScriptExport>.Failure(error);
        var root = snapshot!.Root;

        var (plan, refused) = Plan(root, preset, model, outputKind, width, height, durationSeconds,
            samples, outputDirectory, baseName, reExport, needsDirectory: true);
        if (refused is not null) return ToolResult<RenderScriptExport>.Failure(refused);

        int reported = 0;
        var relay = new InlineProgress<RenderProgress>(p => progress?.Report(new ProgressNotificationValue
        {
            Progress = Interlocked.Increment(ref reported),
            Message = p.Message,
        }));

        try
        {
            var source = await blender.AcquireAsync(plan!.Request, ct).ConfigureAwait(false);
            var script = await blender.Renderer.ExportScriptAsync(plan.Spec, source, plan.Spec.Output.OutputDirectory, relay, ct)
                .ConfigureAwait(false);
            return ToolResult<RenderScriptExport>.Success(new RenderScriptExport(
                Script: BlenderRenderJob.Display(root, script),
                Directory: BlenderRenderJob.Display(root, plan.Spec.Output.OutputDirectory),
                Model: BlenderRenderJob.Display(root, source.Model.Path),
                ModelKind: BlenderRenderJob.Camel(source.Model.Kind.ToString()),
                StaleModel: source.Model.StalenessReason,
                Frames: SceneSpecValidator.FrameCount(plan.Spec)));
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

    /// <summary>
    /// The scene and model request for a render or export, or the error to answer with. Every path is
    /// held to the workspace; the output folder may not be the project's config folder.
    /// </summary>
    private (RenderPlan? Plan, ToolError? Error) Plan(
        string root, string preset, string? model, string? outputKind, int width, int height,
        double durationSeconds, int samples, string? outputDirectory, string? baseName, bool reExport,
        bool needsDirectory)
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

        ModelSourceRequest request;
        if (!string.IsNullOrWhiteSpace(model))
        {
            if (!WorkspacePaths.TryResolve(root, model, out var file, out var reason))
                return Refuse(ToolErrorCodes.PathOutsideWorkspace, reason);
            if (!File.Exists(file))
                return Refuse(ToolErrorCodes.FileNotFound, $"No such file: {model}");
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".lox", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".3d", StringComparison.OrdinalIgnoreCase))
                return Refuse(ToolErrorCodes.InvalidArgument,
                    $"'{model}' is not a model Blender can render. Name a Therion .lox or a Survex .3d, or omit "
                    + "model to render the project's own.");
            request = ModelSourceRequest.ForExternalFile(file);
        }
        else
        {
            request = ModelSourceRequest.ForWorkspace(allowReExport: reExport);
        }

        return (new RenderPlan(chosen.Name, spec, request), null);
    }
}
