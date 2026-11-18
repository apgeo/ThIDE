// The Blender renders agents start through the MCP tools. A render can run for minutes or hours, longer
// than a host waits for one tool call, so start_render answers at once with a job id and render_status /
// cancel_render follow the job. One render runs at a time: Blender takes every core it is given.

using System.Diagnostics;
using System.Globalization;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Cave3D.Parsing;

namespace Therion.Mcp.Tools;

/// <param name="Phase">acquiringSource, parsingModel, convertingGeometry, generatingScript, rendering, collectingOutputs, or done.</param>
/// <param name="Fraction">How much of the whole render is done, 0–1, when Blender says.</param>
public sealed record RenderProgressDto(string Phase, string Message, double? Fraction, int? Frame, int? FrameCount);

/// <param name="State">running, succeeded, failed, or cancelled.</param>
/// <param name="Frames">How many frames the render makes (one per still).</param>
/// <param name="Model">The model file rendered: workspace-relative, or absolute when it is not in the
/// workspace (a fresh re-export lives in a temporary folder). Null until it has been found.</param>
/// <param name="ModelKind">workspaceArtifact (the project's last build output), externalFile (the file you
/// named), or reExported (Therion was run for this render).</param>
/// <param name="StaleModel">Why the model may be out of date, when it is older than the sources.</param>
/// <param name="OutputDirectory">Where the outputs go, workspace-relative; null for a temporary folder,
/// which <c>outputs</c> names once the render has finished.</param>
/// <param name="Outputs">The files the render wrote: workspace-relative, or absolute outside the workspace.</param>
/// <param name="Device">The device Blender rendered on (CPU, OPTIX, …), once it says.</param>
/// <param name="Warnings">Non-fatal notices: an untested Blender version, labels left out, a GPU fallback.</param>
/// <param name="JobLog">Blender's whole output, for a failed or cancelled render (absolute path).</param>
/// <param name="FailureKind">Why it did not succeed: blenderNotFound, blenderTooOld, scriptError, crashed,
/// cancelled, diskSpace, noOutput, modelUnavailable, invalidSpec, or error.</param>
public sealed record RenderJobStatus(
    string JobId,
    string State,
    string Preset,
    string OutputKind,
    int Width,
    int Height,
    int Frames,
    string? Model,
    string? ModelKind,
    string? StaleModel,
    string? OutputDirectory,
    string StartedUtc,
    double ElapsedSeconds,
    RenderProgressDto? Progress,
    IReadOnlyList<string> Outputs,
    string? Device,
    IReadOnlyList<string> Warnings,
    string? JobLog,
    string? FailureKind,
    string? Error);

/// <summary>
/// The renders started on this server: the running one and the last few finished ones, whose outcome
/// can still be read. Disposing it (the server stopping) cancels a running render, which stops Blender.
/// </summary>
public sealed class BlenderRenderJobs : IDisposable
{
    /// <summary>How many jobs are remembered, the running one included.</summary>
    internal const int Kept = 10;

    private readonly object _lock = new();
    private readonly List<BlenderRenderJob> _jobs = [];   // oldest first
    private bool _disposed;

    /// <summary>
    /// Starts <paramref name="work"/> as <paramref name="job"/>, or returns false with the job that is
    /// still running.
    /// </summary>
    internal bool TryStart(BlenderRenderJob job, Func<BlenderRenderJob, CancellationToken, Task<RenderResult>> work,
        out BlenderRenderJob? running)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            running = _jobs.FirstOrDefault(j => !j.IsFinished);
            if (running is not null) return false;
            _jobs.Add(job);
            if (_jobs.Count > Kept) _jobs.RemoveAt(0);
            job.Run(work);
            return true;
        }
    }

    /// <summary>The job with <paramref name="id"/>, or the latest one when no id is given; null when there is none.</summary>
    internal BlenderRenderJob? Find(string? id)
    {
        lock (_lock)
            return string.IsNullOrWhiteSpace(id)
                ? _jobs.LastOrDefault()
                : _jobs.FirstOrDefault(j => string.Equals(j.Id, id.Trim(), StringComparison.Ordinal));
    }

    public void Dispose()
    {
        BlenderRenderJob[] jobs;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            jobs = [.. _jobs];
        }
        foreach (var job in jobs) job.Cancel();
        // Cancelling kills Blender at once; the wait lets the job write its log before the host goes.
        Task.WaitAll(jobs.Select(j => j.Completion).ToArray(), TimeSpan.FromSeconds(5));
    }
}

/// <summary>One render: what was asked, how far it got, and how it ended.</summary>
internal sealed class BlenderRenderJob
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = new();
    private readonly string _root;
    private readonly string _preset;
    private readonly SceneSpec _spec;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    private RenderProgress? _progress;
    private ResolvedModelSource? _model;
    private RenderResult? _result;
    private (string Kind, string Message)? _failure;
    private bool _finished;

    /// <param name="root">The workspace root paths are reported against.</param>
    /// <param name="spec">The scene to render; an empty output directory means a temporary folder.</param>
    public BlenderRenderJob(string root, string preset, SceneSpec spec)
    {
        _root = root;
        _preset = preset;
        _spec = spec;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Completes when the job has finished, however it ended. Never faults.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public bool IsFinished { get { lock (_lock) return _finished; } }

    public RenderProgress? LatestProgress { get { lock (_lock) return _progress; } }

    public void Report(RenderProgress progress) { lock (_lock) _progress = progress; }

    public void SetModel(ResolvedModelSource model) { lock (_lock) _model = model; }

    public void Cancel()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal void Run(Func<BlenderRenderJob, CancellationToken, Task<RenderResult>> work)
    {
        _clock.Start();
        Completion = Task.Run(() => RunAsync(work));
    }

    private async Task RunAsync(Func<BlenderRenderJob, CancellationToken, Task<RenderResult>> work)
    {
        RenderResult? result = null;
        (string, string)? failure = null;
        try
        {
            result = await work(this, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            failure = (Camel(nameof(RenderFailureKind.Cancelled)), "The render was cancelled.");
        }
        catch (ModelSourceNotFoundException ex)
        {
            failure = ("modelUnavailable", ex.Message);
        }
        catch (CaveFileFormatException ex)
        {
            failure = ("modelUnavailable", "The model file cannot be read: " + ex.Message);
        }
        catch (ArgumentException ex)
        {
            failure = ("invalidSpec", ex.Message);
        }
        // Nobody awaits a job to throw to: whatever stops it has to end up in its status, or it would
        // read "running" for ever.
        catch (Exception ex)
        {
            failure = ("error", ex.Message);
        }

        lock (_lock)
        {
            _clock.Stop();
            _result = result;
            _failure = failure;
            _finished = true;
        }
    }

    public RenderJobStatus Status()
    {
        lock (_lock)
        {
            var result = _result;
            string state = !_finished ? "running"
                : result is { Succeeded: true } ? "succeeded"
                : result?.FailureKind == RenderFailureKind.Cancelled || _failure?.Kind == Camel(nameof(RenderFailureKind.Cancelled)) ? "cancelled"
                : "failed";
            string? failureKind = _failure?.Kind
                ?? (result is { Succeeded: false } ? Camel(result.FailureKind.ToString()) : null);
            string? error = _failure?.Message ?? (result is { Succeeded: false } ? result.ErrorMessage : null);
            if (result?.StartFailedPath is { } exe) error = $"{error} (Blender: {exe})";

            return new RenderJobStatus(
                JobId: Id,
                State: state,
                Preset: _preset,
                OutputKind: OutputKindName(_spec.Output.Kind),
                Width: _spec.Output.Width,
                Height: _spec.Output.Height,
                Frames: result?.FrameCount ?? SceneSpecValidator.FrameCount(_spec),
                Model: _model is null ? null : Display(_root, _model.Path),
                ModelKind: _model is null ? null : Camel(_model.Kind.ToString()),
                StaleModel: _model?.StalenessReason,
                OutputDirectory: string.IsNullOrEmpty(_spec.Output.OutputDirectory) ? null : Display(_root, _spec.Output.OutputDirectory),
                StartedUtc: _started.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                ElapsedSeconds: Math.Round(_clock.Elapsed.TotalSeconds, 1),
                Progress: _progress is { } p
                    ? new RenderProgressDto(Camel(p.Phase.ToString()), p.Message, p.Fraction, p.Frame, p.FrameCount)
                    : null,
                Outputs: result is null ? [] : result.OutputPaths.Select(o => Display(_root, o)).ToList(),
                Device: result?.Device ?? _progress?.Device,
                Warnings: result?.Warnings ?? [],
                JobLog: result?.JobLogPath,
                FailureKind: failureKind,
                Error: error);
        }
    }

    /// <summary>The wire name of an output kind, as start_render takes it.</summary>
    internal static string OutputKindName(OutputKind kind) => kind switch
    {
        OutputKind.Video => "video",
        OutputKind.FrameSequence => "frames",
        _ => "still",
    };

    /// <summary>Workspace-relative inside the workspace, absolute outside it.</summary>
    internal static string Display(string root, string path) =>
        WorkspacePaths.IsInside(root, path) ? WorkspacePaths.ToRelative(root, path) : path;

    internal static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
