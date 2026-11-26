// A render worker that takes render bundles from a folder and leaves what they render in another:
// for a machine with Blender (a GPU) and nothing else of ThIDE, filled by whatever moves files to
// it. One bundle at a time, oldest first. A job is the zip <job>.zip in the inbox; while it runs,
// <job>.status.json in the outbox says how far it got; on success the outbox gets the movie (or
// stills), its subtitles and Blender's log, named after the job; <job>.cancel in the inbox stops
// it, and a stopped or cut-short job goes on with its frames when it is taken again.
//
// A bundle is data (RenderBundleReader): its player and scripts are never run. Its plans are
// played by this assembly's own player, with only the module folders the operator names, in
// Blender started with -b --factory-startup, and only the files the worker names leave it.

using System.Diagnostics;
using Therion.Blender.Plan;
using Therion.Blender.Player;

namespace Therion.Blender.Execution;

/// <summary>How a <see cref="RenderWorker"/> runs.</summary>
public sealed record RenderWorkerOptions
{
    /// <summary>The folder bundles arrive in (<c>&lt;job&gt;.zip</c>) and cancels too (<c>&lt;job&gt;.cancel</c>).</summary>
    public required string Inbox { get; init; }

    /// <summary>The folder the results go to.</summary>
    public required string Outbox { get; init; }

    /// <summary>Where the bundles are unpacked and their frames are kept while they render.</summary>
    public required string Scratch { get; init; }

    /// <summary>What a bundle may hold at most.</summary>
    public RenderBundleLimits Limits { get; init; } = new();

    /// <summary>The longest a job may run; it is stopped after, its frames kept.</summary>
    public TimeSpan MaxTime { get; init; } = TimeSpan.FromHours(72);

    /// <summary>Extra module folders the operator trusts; a bundle cannot add any.</summary>
    public IReadOnlyList<string> ModuleFolders { get; init; } = [];

    /// <summary>How often the inbox is looked at, and a running job's cancel file.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Renders the bundles of an inbox into an outbox, one at a time.</summary>
public sealed class RenderWorker
{
    /// <summary>The extensions of the files a job's result is: the movie, stills, subtitles.</summary>
    private static readonly string[] ResultExtensions = [".mp4", ".mkv", ".webm", ".png", ".srt", ".vtt"];

    private readonly BlenderLocator _locator;
    private readonly BlenderRunner _runner;
    private readonly RenderWorkerOptions _options;
    private readonly Func<string?>? _blender;
    private readonly Action<string>? _log;

    /// <param name="blender">The operator's Blender, if named.</param>
    /// <param name="log">Hears one line for each thing the worker does.</param>
    public RenderWorker(BlenderLocator locator, BlenderRunner runner, RenderWorkerOptions options,
        Func<string?>? blender = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        _locator = locator;
        _runner = runner;
        _options = options;
        _blender = blender;
        _log = log;
    }

    public const string StatusSuffix = ".status.json";
    public const string CancelSuffix = ".cancel";
    public const string LogSuffix = ".log";

    /// <summary>The jobs of the inbox, oldest first: its zips that have no cancel file.</summary>
    public IReadOnlyList<string> Pending()
    {
        if (!Directory.Exists(_options.Inbox)) return [];
        return new DirectoryInfo(_options.Inbox).EnumerateFiles("*.zip")
            .Where(f => !File.Exists(Path.Combine(_options.Inbox, JobId(f.FullName) + CancelSuffix)))
            .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => f.FullName)
            .ToList();
    }

    /// <summary>The job's id: the zip's name without <c>.zip</c>, made a safe file name.</summary>
    public static string JobId(string zipPath) => RenderBundleWriter.FolderName(zipPath);

    /// <summary>
    /// Takes the jobs of the inbox one after the other until <paramref name="ct"/> is cancelled,
    /// looking again every <see cref="RenderWorkerOptions.Poll"/>; with <paramref name="once"/>,
    /// until the inbox has none left. Returns how many jobs it took.
    /// </summary>
    public async Task<int> RunAsync(bool once, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.Outbox);
        Directory.CreateDirectory(_options.Scratch);
        int taken = 0;
        while (!ct.IsCancellationRequested)
        {
            var pending = Pending();
            if (pending.Count == 0)
            {
                if (once) break;
                try { await Task.Delay(_options.Poll, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            await ProcessAsync(pending[0], ct).ConfigureAwait(false);
            taken++;
        }
        return taken;
    }

    /// <summary>Renders one bundle and returns how its job ended. Never throws for the job's own
    /// failures; <paramref name="ct"/> stops it as a cancel file does (its zip and frames stay).</summary>
    public async Task<RenderJobRecord> ProcessAsync(string zipPath, CancellationToken ct)
    {
        string id = JobId(zipPath);
        Directory.CreateDirectory(_options.Outbox);
        Directory.CreateDirectory(_options.Scratch);
        var record = new RenderJobRecord
        {
            Id = id,
            Request = new RenderJobRequest { Name = id, Owner = "worker", Bundle = Path.GetFileName(zipPath) },
            State = RenderJobState.Running,
            QueuedUtc = new FileInfo(zipPath).LastWriteTimeUtc,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        WriteStatus(record);
        string scratch = Path.Combine(_options.Scratch, id);
        string bundleDir = Path.Combine(scratch, "bundle");
        string framesDir = Path.Combine(scratch, PlanOutputs.FramesFolderName);
        _log?.Invoke($"{id}: taken");

        // The cancel file, looked at while the job runs; the wall-clock limit beside it.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(_options.MaxTime);
        string cancelFile = Path.Combine(_options.Inbox, id + CancelSuffix);
        using var watcher = new Timer(_ => { if (File.Exists(cancelFile)) TryCancel(stop); }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        RenderJobResult result;
        bool cancelled = false, refused = false;
        try
        {
            var bundle = RenderBundleReader.Unpack(zipPath, bundleDir, _options.Limits);
            PlayerWriter.WritePlayer(bundle.Scenes, bundleDir);
            var locate = _locator.Locate(_blender?.Invoke());
            if (!locate.IsUsable)
            {
                result = RenderJobResult.Failure(Camel(locate.Status == BlenderLocateStatus.TooOld ? RenderFailureKind.BlenderTooOld : RenderFailureKind.BlenderNotFound), locate.Detail);
            }
            else
            {
                var progress = new StatusProgress(this, () => record, r => record = r);
                var (rendered, perFrame) = await PlayAsync(bundle, locate.Installation!, framesDir, progress, stop.Token).ConfigureAwait(false);
                cancelled = rendered.FailureKind == RenderFailureKind.Cancelled;
                result = RenderJobResult.From(rendered, perFrame);
                if (rendered.Succeeded) result = result with { Outputs = Deliver(id, rendered, bundle, bundleDir) };
                if (rendered.JobLogPath is { } log && File.Exists(log)) CopyLog(id, scratch);
            }
        }
        catch (RenderBundleRefusedException ex)
        {
            refused = true;
            result = RenderJobResult.Failure("refused", ex.Message);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            result = RenderJobResult.Failure("cancelled", "The render was stopped.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            result = RenderJobResult.Failure("error", ex.Message);
        }
        if (cancelled && !File.Exists(cancelFile) && !ct.IsCancellationRequested)
            result = RenderJobResult.Failure("timedOut", $"The render ran longer than the {_options.MaxTime.TotalHours:0.#} h allowed; its frames are kept.");

        record = record.Ended(result, DateTimeOffset.UtcNow, cancelled && result.FailureKind == "cancelled");
        WriteStatus(record);
        _log?.Invoke($"{id}: {record.State.ToString().ToLowerInvariant()}{(result.Error is { } error ? ": " + error : "")}");

        // A job that ended is done with: its zip goes, and so does its scratch unless it failed in
        // Blender or ran out of time (its frames stay, for the same bundle sent again to go on
        // with). A stopped job keeps both and goes on when its cancel file is removed, or when the
        // worker that was stopped starts again.
        if (record.State != RenderJobState.Cancelled)
        {
            TryDelete(zipPath);
            if (record.State == RenderJobState.Succeeded || refused) TryDeleteFolder(scratch);
            else TryDeleteFolder(bundleDir);
        }
        return record;
    }

    /// <summary>Plays the bundle: each scene's first stage (every segment of a film in a Blender
    /// of its own, its frames in a folder of its own), then the output from the frames.</summary>
    private async Task<(RenderResult Result, double? SecondsPerFrame)> PlayAsync(
        UnpackedBundle bundle, BlenderInstallation blender, string framesDir, IProgress<RenderProgress> progress, CancellationToken ct)
    {
        string script = Path.Combine(bundle.Folder, PlayerFiles.EntryFileName);
        var modules = _options.ModuleFolders.SelectMany(f => new[] { "--modules", Path.GetFullPath(f) }).ToArray();
        string top = Path.Combine(bundle.Folder, ScenePlanSerializer.FileName);
        double? perFrame = null;
        var paced = new BlenderRenderService.Paced(new Relay(p =>
        {
            if (p.SecondsPerFrame is { } s) perFrame = s;
            progress.Report(p);
        }), bundle.Scenes.Sum(PlanOutputs.StageFrameCount));

        if (bundle.Movie is { } movie)
        {
            int before = 0;
            for (int i = 0; i < movie.Segments.Count; i++)
            {
                var segment = movie.Segments[i];
                var plan = bundle.Segments[i];
                string frames = Path.Combine(framesDir, segment.Frames.Replace('/', Path.DirectorySeparatorChar));
                int index = i;
                paced.Starts(before);
                var part = new Relay(t => paced.Report(t with { Segment = index + 1, SegmentCount = movie.Segments.Count }));
                var stage = await _runner.RunAsync(blender,
                    new RenderJob(script, bundle.Folder, segment.FrameCount, PlanOutputs.FramesOf(plan, frames))
                    {
                        ScriptArguments = ["--plan", Path.Combine(bundle.Folder, segment.Plan), "--stage", "frames", "--frames", frames, .. modules],
                        LogFileName = $"job-frames.{segment.Id}.log",
                    }, part, ct).ConfigureAwait(false);
                if (!stage.Succeeded) return (stage, perFrame);
                before += segment.FrameCount;
            }
            var output = PlanOutputs.SpecOf(bundle.Segments[0] with { Output = movie.Output }, Path.Combine(bundle.Folder, movie.Output.Directory));
            var composed = await _runner.RunAsync(blender,
                new RenderJob(script, bundle.Folder, movie.OutputFrameCount, output)
                {
                    ScriptArguments = ["--plan", top, "--stage", "compose", "--frames", framesDir, .. modules],
                }, progress, ct).ConfigureAwait(false);
            return (composed, perFrame);
        }

        var scene = bundle.Scene;
        var sceneOutput = PlanOutputs.SpecOf(scene, Path.Combine(bundle.Folder, scene.Output.Directory));
        // One stage, or two when the scene has texts: the player starts the second itself.
        var result = await _runner.RunAsync(blender,
            new RenderJob(script, bundle.Folder, scene.OutputFrameCount, sceneOutput)
            {
                ScriptArguments = ["--plan", top, "--frames", framesDir, .. modules],
                ExtraBytes = scene.HasTexts ? PlanOutputs.FrameBytes(scene) : 0,
            }, paced, ct).ConfigureAwait(false);
        return (result, perFrame);
    }

    /// <summary>Moves the outputs a job's render wrote, and the subtitles of its bundle that lie
    /// beside them, to the outbox under the job's name. Nothing else leaves the scratch folder.</summary>
    private IReadOnlyList<string> Deliver(string id, RenderResult rendered, UnpackedBundle bundle, string bundleDir)
    {
        var delivered = new List<string>();
        var outputs = rendered.OutputPaths.ToList();
        var output = (bundle.Movie?.Output ?? bundle.Scene.Output);
        string outDir = Path.GetFullPath(Path.Combine(bundleDir, output.Directory));
        foreach (string extension in new[] { Subtitles.SrtExtension, Subtitles.VttExtension })
        {
            string subtitles = Path.Combine(outDir, output.BaseName + extension);
            if (File.Exists(subtitles)) outputs.Add(subtitles);
        }
        foreach (string path in outputs)
        {
            string full = Path.GetFullPath(path);
            string extension = Path.GetExtension(full).ToLowerInvariant();
            if (!ResultExtensions.Contains(extension) || !full.StartsWith(Path.GetFullPath(bundleDir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            if (new FileInfo(full).LinkTarget is not null) continue;
            // frames and stills keep the number Blender gave them after the job's name.
            string stem = Path.GetFileNameWithoutExtension(full);
            string suffix = stem.Length > output.BaseName.Length && stem.StartsWith(output.BaseName, StringComparison.Ordinal)
                ? stem[output.BaseName.Length..] : "";
            string target = Path.Combine(_options.Outbox, id + suffix + extension);
            File.Move(full, target, overwrite: true);
            delivered.Add(target);
        }
        return delivered;
    }

    /// <summary>Blender's logs of the job, one after the other, as <c>&lt;job&gt;.log</c> in the outbox.</summary>
    private void CopyLog(string id, string scratch)
    {
        string bundleDir = Path.Combine(scratch, "bundle");
        var logs = Directory.EnumerateFiles(bundleDir, "job*.log").Order(StringComparer.Ordinal).ToList();
        using var target = new StreamWriter(Path.Combine(_options.Outbox, id + LogSuffix), append: false);
        foreach (string log in logs)
        {
            target.WriteLine($"==== {Path.GetFileName(log)}");
            target.Write(File.ReadAllText(log));
        }
    }

    private void WriteStatus(RenderJobRecord record)
    {
        string path = Path.Combine(_options.Outbox, record.Id + StatusSuffix);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, RenderJobFile.WriteOne(record));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Keeps the status file up to date with the progress, once a second at most.</summary>
    private sealed class StatusProgress(RenderWorker worker, Func<RenderJobRecord> get, Action<RenderJobRecord> set) : IProgress<RenderProgress>
    {
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private readonly object _lock = new();

        public void Report(RenderProgress value)
        {
            lock (_lock)
            {
                var record = get() with { Progress = RenderJobProgress.From(value) };
                set(record);
                if (_since.Elapsed < TimeSpan.FromSeconds(1) && value.Phase != RenderPhase.Composing) return;
                _since.Restart();
                worker.WriteStatus(record);
            }
        }
    }

    private sealed class Relay(Action<RenderProgress> report) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => report(value);
    }

    private static string Camel(RenderFailureKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    private static void TryCancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
