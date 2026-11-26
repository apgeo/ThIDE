// A render service whose renders wait their turn in a RenderQueue: whoever asks (a panel, an agent)
// gets the same service, and a second render waits for the first instead of being refused. Exports
// start no Blender and do not wait. Each render keeps its frames in a folder of the queue's, so that
// a render a restart cut short goes on with them when the queue starts it again.

using Therion.Blender.Sources;
using Therion.Cave3D;

namespace Therion.Blender.Execution;

/// <summary>An <see cref="IBlenderRenderService"/> that renders one job at a time, in turn.</summary>
public sealed class QueuedRenderService : IBlenderRenderService, IRenderEstimator
{
    private readonly IBlenderRenderService _inner;
    private readonly string? _framesRoot;

    /// <param name="inner">The service that renders.</param>
    /// <param name="queue">The queue the renders wait in.</param>
    /// <param name="framesRoot">Where each render keeps its frames (a folder per job) when it
    /// does not name a frames folder itself; null keeps them where the service would.</param>
    public QueuedRenderService(IBlenderRenderService inner, RenderQueue queue, string? framesRoot = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(queue);
        _inner = inner;
        Queue = queue;
        _framesRoot = framesRoot;
    }

    public RenderQueue Queue { get; }

    public Task<RenderResult> RenderAsync(
        SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        string id = NewId();
        var placed = WithFrames(options, id);
        var request = Request(options, spec.Output.BaseName, source, placed) with { Spec = SceneSpecSerializer.Write(spec) };
        return RunAsync(id, request, placed, (p, t) => _inner.RenderAsync(spec, source, placed, p, t), progress, ct);
    }

    public Task<RenderResult> RenderStoryboardAsync(
        Storyboard storyboard, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        string id = NewId();
        var placed = WithFrames(options, id);
        var request = Request(options, storyboard.Output.BaseName, source, placed) with { Storyboard = StoryboardSerializer.Write(storyboard) };
        return RunAsync(id, request, placed, (p, t) => _inner.RenderStoryboardAsync(storyboard, source, placed, p, t), progress, ct);
    }

    /// <summary>The estimate of the service that renders; it does not wait in the queue.</summary>
    public Task<RenderEstimate> EstimateStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, CancellationToken ct = default)
        => _inner is IRenderEstimator estimator
            ? estimator.EstimateStoryboardAsync(storyboard, source, options, ct)
            : throw new NotSupportedException("The render service makes no estimates.");

    public Task<string> ExportScriptAsync(
        SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default,
        bool interactive = false)
        => _inner.ExportScriptAsync(spec, source, outputDir, progress, ct, interactive);

    public Task<RenderExport> ExportAsync(
        SceneSpec spec, RenderSource source, string outputDir, ExportOptions options, IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default)
        => _inner.ExportAsync(spec, source, outputDir, options, progress, ct);

    /// <summary>
    /// Starts again the renders a restart cut short (<see cref="RenderQueue.Interrupted"/>) whose
    /// model is still there: a scene or a film of an external model file, with its frames. Returns
    /// how many were started; the queue says how each ends.
    /// </summary>
    public int ResumeInterrupted()
    {
        var tasks = Queue.Resume(request =>
        {
            if (request.Model is not { } model || !File.Exists(model)) return null;
            var format = model.EndsWith(".3d", StringComparison.OrdinalIgnoreCase) ? CaveSourceFormat.Survex3d : CaveSourceFormat.Lox;
            var source = new RenderSource(
                new ResolvedModelSource { Path = model, Format = format, Kind = ModelSourceKind.ExternalFile }, request.Leads);
            var options = new RenderOptions
            {
                Draft = request.Draft,
                KeepFrames = request.KeepFrames,
                FramesDirectory = request.FramesDirectory,
                JobName = request.Name,
                Owner = request.Owner,
            };
            try
            {
                if (request.Spec is { } spec)
                {
                    var scene = SceneSpecSerializer.Read(spec);
                    return (p, t) => _inner.RenderAsync(scene, source, options, p, t);
                }
                if (request.Storyboard is { } text)
                {
                    var film = StoryboardSerializer.Read(text);
                    return (p, t) => _inner.RenderStoryboardAsync(film, source, options, p, t);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or System.Text.Json.JsonException
                                           or StoryboardFormatException) { }
            return null;
        });
        return tasks.Count;
    }

    private async Task<RenderResult> RunAsync(
        string id, RenderJobRequest request, RenderOptions options,
        Func<IProgress<RenderProgress>?, CancellationToken, Task<RenderResult>> work, IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        bool ours = _framesRoot is not null && options.FramesDirectory is { } frames && !options.KeepFrames
                    && Path.GetDirectoryName(frames) is { } parent && IsUnder(parent, _framesRoot);
        try
        {
            return await Queue.RunAsync(request, work, progress, ct, id).ConfigureAwait(false);
        }
        finally
        {
            // The folder the queue gave the frames goes once the job has ended; only a restart
            // that cut the job short leaves it, for the job to go on with.
            if (ours)
            {
                try
                {
                    string folder = Path.GetDirectoryName(options.FramesDirectory!)!;
                    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private RenderOptions WithFrames(RenderOptions options, string id)
        => _framesRoot is null || options.FramesDirectory is not null || options.ComposeOnly || options.KeepFrames
            ? options
            : options with { FramesDirectory = Path.Combine(_framesRoot, id, PlanOutputs.FramesFolderName) };

    private static RenderJobRequest Request(RenderOptions options, string baseName, RenderSource source, RenderOptions placed) => new()
    {
        Name = options.JobName ?? baseName,
        Owner = options.Owner,
        Model = source.Model.Path,
        Leads = source.Leads,
        Draft = options.Draft,
        KeepFrames = options.KeepFrames,
        FramesDirectory = placed.FramesDirectory,
    };

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static bool IsUnder(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != "." && !Path.IsPathRooted(relative) && !relative.StartsWith("..", StringComparison.Ordinal);
    }
}
