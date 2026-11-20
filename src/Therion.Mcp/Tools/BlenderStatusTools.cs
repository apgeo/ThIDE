using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Therion.Blender;
using Therion.Blender.Presets;

namespace Therion.Mcp.Tools;

/// <param name="Camera">The camera move: static, orbit, helix, flythrough, viewpoints, or stillSet.</param>
/// <param name="DurationSeconds">Length of an animated output; null for stills.</param>
/// <param name="Frames">How many frames it renders as it stands (one per still).</param>
public sealed record RenderPresetDto(
    string Name,
    string? Description,
    bool BuiltIn,
    string OutputKind,
    int Width,
    int Height,
    string Camera,
    double? DurationSeconds,
    int Fps,
    int Frames,
    string Engine,
    int Samples);

public sealed record RenderPresetList(IReadOnlyList<RenderPresetDto> Presets);

/// <summary>
/// Reading the Blender side: which presets exist and how a render is getting on. Registered only on a
/// host that can render (the running IDE), in both profiles.
/// </summary>
[McpServerToolType]
public sealed class BlenderStatusTools(IBlenderRenderHost blender, BlenderRenderJobs jobs)
{
    /// <summary>The longest render_status waits in one call. Hosts give up on a tool call long before a render ends.</summary>
    internal const int MaxWaitSeconds = 60;

    [McpServerTool(Name = "list_render_presets", Title = "List Blender presets", ReadOnly = true, Idempotent = true)]
    [Description("The presets start_render and export_render_script accept: the built-in ones and the "
               + "user's own, each with its camera move, output (video, frames or still), size, length "
               + "and render samples. Pick one by name; start_render can override the output kind, size, "
               + "length and samples.")]
    public ToolResult<RenderPresetList> ListRenderPresets() =>
        ToolResult<RenderPresetList>.Success(new RenderPresetList(Presets(blender).Select(ToDto).ToList()));

    [McpServerTool(Name = "render_status", Title = "Blender render status", ReadOnly = true, Idempotent = true)]
    [Description("How a render started with start_render is getting on: running, succeeded, failed or "
               + "cancelled; the phase and frame it is at; and once it has finished, the files it wrote, "
               + "the device Blender used, warnings, and on failure the reason (failureKind) and Blender's "
               + "log. Give waitSeconds to wait up to 60 seconds for it to finish before answering; the "
               + "wait streams progress.")]
    public async Task<ToolResult<RenderJobStatus>> RenderStatus(
        [Description("The job id start_render returned. Defaults to the latest render.")]
        string? jobId = null,
        [Description("Wait up to this many seconds (at most 60) for the render to finish. Defaults to 0: answer at once.")]
        int waitSeconds = 0,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        if (jobs.Find(jobId) is not { } job) return NotFound<RenderJobStatus>(jobId);

        int wait = Math.Clamp(waitSeconds, 0, MaxWaitSeconds);
        var clock = Stopwatch.StartNew();
        RenderProgress? reported = null;
        int ticks = 0;
        while (!job.IsFinished && clock.Elapsed.TotalSeconds < wait)
        {
            await Task.WhenAny(job.Completion, Task.Delay(TimeSpan.FromSeconds(1), ct));
            ct.ThrowIfCancellationRequested();
            if (progress is not null && job.LatestProgress is { } now && now != reported)
            {
                reported = now;
                progress.Report(new ProgressNotificationValue
                {
                    Progress = ++ticks,
                    Message = now is { Frame: int f, FrameCount: int n } ? $"{now.Message} ({f}/{n})" : now.Message,
                });
            }
        }

        return ToolResult<RenderJobStatus>.Success(job.Status());
    }

    /// <summary>The presets a render may name: the built-in ones first (they win a name clash), then the user's.</summary>
    internal static IReadOnlyList<RenderPreset> Presets(IBlenderRenderHost blender) =>
        [.. BuiltInPresets.All, .. blender.UserPresets()];

    internal static ToolResult<T> NotFound<T>(string? jobId) => ToolResult<T>.Failure(ToolErrorCodes.JobNotFound,
        string.IsNullOrWhiteSpace(jobId)
            ? "No render has been started on this server. Start one with start_render."
            : $"No render job '{jobId}'. Only the last {BlenderRenderJobs.Kept} renders are remembered, "
              + "and none from before ThIDE's tools server last started. Call render_status without a jobId for the latest.");

    private static RenderPresetDto ToDto(RenderPreset preset)
    {
        var spec = preset.Spec;
        bool animated = spec.Output.Kind is not OutputKind.Still && spec.Camera.Template is not CameraTemplate.StillSet;
        return new RenderPresetDto(
            Name: preset.Name,
            Description: preset.Description,
            BuiltIn: preset.BuiltIn,
            OutputKind: BlenderRenderJob.OutputKindName(spec.Output.Kind),
            Width: spec.Output.Width,
            Height: spec.Output.Height,
            Camera: BlenderRenderJob.Camel(spec.Camera.Template.ToString()),
            DurationSeconds: animated ? spec.Animation.DurationSeconds : null,
            Fps: spec.Animation.Fps,
            Frames: SceneSpecValidator.FrameCount(spec),
            Engine: BlenderRenderJob.Camel(spec.Engine.Kind.ToString()),
            Samples: spec.Engine.Samples);
    }
}
