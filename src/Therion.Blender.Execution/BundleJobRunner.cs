// The loop every render worker shares, whatever brings it its bundles (a folder, a server): unpack
// a bundle into the job's scratch folder and check it (RenderBundleReader), play its plans with
// this assembly's own player in Blender started with -b --factory-startup, and move only the files
// the worker names (the movie or stills, the subtitles, Blender's log) to where the results go.
// The frames stay in the job's scratch folder, so that the same job taken again goes on with them.

using System.Diagnostics;
using Therion.Blender.Plan;
using Therion.Blender.Player;

namespace Therion.Blender.Execution;

/// <summary>One bundle to render, and where.</summary>
internal sealed record BundleJob
{
    /// <summary>The job's id, a safe file name: the results are named after it.</summary>
    public required string Id { get; init; }

    /// <summary>The bundle's zip.</summary>
    public required string ZipPath { get; init; }

    /// <summary>The job's own scratch folder: the bundle is unpacked into <c>bundle/</c>, the
    /// frames are kept in <c>frames/</c>.</summary>
    public required string Scratch { get; init; }

    /// <summary>Where the results go.</summary>
    public required string Outbox { get; init; }

    /// <summary>Which plan of the bundle is played, and in what container.</summary>
    public RenderBundleChoice Choice { get; init; } = new();

    public RenderBundleLimits Limits { get; init; } = new();

    /// <summary>The longest the job may run; it is stopped after, its frames kept.</summary>
    public TimeSpan MaxTime { get; init; } = TimeSpan.FromHours(72);
}

/// <summary>How a bundle job ended.</summary>
/// <param name="Record">The job's record, ended.</param>
/// <param name="Refused">The bundle broke a rule, and nothing of it was played.</param>
/// <param name="TimedOut">The job ran longer than it was allowed.</param>
internal sealed record BundleJobOutcome(RenderJobRecord Record, bool Refused, bool TimedOut);

/// <summary>Renders one bundle; shared by <see cref="RenderWorker"/> and <see cref="RenderNodeWorker"/>.</summary>
internal sealed class BundleJobRunner(
    BlenderLocator locator, BlenderRunner runner, IReadOnlyList<string> moduleFolders, Func<string?>? blender)
{
    /// <summary>The extensions of the files a job's result is: the movie, stills, subtitles.</summary>
    private static readonly string[] ResultExtensions = [".mp4", ".mkv", ".webm", ".png", ".srt", ".vtt"];

    public const string LogSuffix = ".log";

    /// <summary>The longest a job may be allowed to run; a longer limit is this.</summary>
    public static TimeSpan LongestTime => RenderWorkerOptions.LongestMaxTime;

    /// <summary><paramref name="limit"/>, at most <see cref="LongestTime"/>; a limit of zero or
    /// less is that too.</summary>
    public static TimeSpan Longest(TimeSpan limit) => limit <= TimeSpan.Zero || limit > LongestTime ? LongestTime : limit;

    /// <summary>
    /// Renders <paramref name="job"/>, starting from <paramref name="record"/>; tells
    /// <paramref name="status"/> how far it got, once a second at most. Never throws for the
    /// job's own failures; <paramref name="cancel"/> stops it (its frames stay), and so does the
    /// job's time limit.
    /// </summary>
    public async Task<BundleJobOutcome> RunAsync(BundleJob job, RenderJobRecord record, Action<RenderJobRecord> status, CancellationToken cancel)
    {
        Directory.CreateDirectory(job.Outbox);
        string bundleDir = Path.Combine(job.Scratch, "bundle");
        string framesDir = Path.Combine(job.Scratch, PlanOutputs.FramesFolderName);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var maxTime = Longest(job.MaxTime);
        stop.CancelAfter(maxTime);

        RenderJobResult result;
        bool cancelled = false, refused = false;
        try
        {
            var bundle = Unpack(job, bundleDir);
            PlayerWriter.WritePlayer(bundle.Scenes, bundleDir);
            var locate = locator.Locate(blender?.Invoke());
            if (!locate.IsUsable)
            {
                result = RenderJobResult.Failure(Camel(locate.Status == BlenderLocateStatus.TooOld ? RenderFailureKind.BlenderTooOld : RenderFailureKind.BlenderNotFound), locate.Detail ?? "No Blender.");
            }
            else
            {
                var progress = new StatusProgress(status, () => record, r => record = r);
                var (rendered, perFrame) = await PlayAsync(bundle, locate.Installation!, framesDir, progress, stop.Token).ConfigureAwait(false);
                cancelled = rendered.FailureKind == RenderFailureKind.Cancelled;
                result = RenderJobResult.From(rendered, perFrame);
                if (rendered.Succeeded) result = result with { Outputs = Deliver(job, rendered, bundle, bundleDir) };
                if (rendered.JobLogPath is { } log && File.Exists(log)) CopyLog(job, bundleDir);
            }
        }
        catch (RenderBundleRefusedException ex)
        {
            refused = true;
            result = RenderJobResult.Failure(RenderNodeFailure.Refused, ex.Message);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            result = RenderJobResult.Failure(RenderNodeFailure.Cancelled, "The render was stopped.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            result = RenderJobResult.Failure(RenderNodeFailure.Error, ex.Message);
        }
        bool timedOut = cancelled && !cancel.IsCancellationRequested;
        if (timedOut)
            result = RenderJobResult.Failure(RenderNodeFailure.TimedOut, $"The render ran longer than the {maxTime.TotalHours:0.#} h allowed; its frames are kept.");
        return new BundleJobOutcome(record.Ended(result, DateTimeOffset.UtcNow, cancelled && result.FailureKind == "cancelled"), refused, timedOut);
    }

    /// <summary>Unpacks and checks the bundle. A bundle is data from elsewhere: whatever reading it
    /// throws, other than the disk failing under it, is a bundle that is refused (a plan the
    /// checks cannot even read, such as one with null where a section is), never a fault of the
    /// worker's that another try would get past.</summary>
    private static UnpackedBundle Unpack(BundleJob job, string bundleDir)
    {
        try { return RenderBundleReader.Unpack(job.ZipPath, bundleDir, job.Limits, job.Choice); }
        catch (Exception ex) when (ex is not (RenderBundleRefusedException or IOException or UnauthorizedAccessException or OperationCanceledException or OutOfMemoryException))
        {
            throw new RenderBundleRefusedException($"The bundle cannot be read: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Plays the bundle: each scene's first stage (every segment of a film in a Blender
    /// of its own, its frames in a folder of its own), then the output from the frames.</summary>
    private async Task<(RenderResult Result, double? SecondsPerFrame)> PlayAsync(
        UnpackedBundle bundle, BlenderInstallation installation, string framesDir, IProgress<RenderProgress> progress, CancellationToken ct)
    {
        string script = Path.Combine(bundle.Folder, PlayerFiles.EntryFileName);
        // The player's options of every stage of a bundle: whatever its plans name stays in its
        // folder (--confine), and the module folders are the operator's alone.
        string[] modules = ["--confine", .. moduleFolders.SelectMany(f => new[] { "--modules", Path.GetFullPath(f) })];
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
                var stage = await runner.RunAsync(installation,
                    new RenderJob(script, bundle.Folder, segment.FrameCount, PlanOutputs.FramesOf(plan, frames))
                    {
                        ScriptArguments = ["--plan", Path.Combine(bundle.Folder, segment.Plan), "--stage", "frames", "--frames", frames, .. modules],
                        LogFileName = $"job-frames.{segment.Id}.log",
                    }, part, ct).ConfigureAwait(false);
                if (!stage.Succeeded) return (stage, perFrame);
                before += segment.FrameCount;
            }
            var output = PlanOutputs.SpecOf(bundle.Segments[0] with { Output = movie.Output }, Path.Combine(bundle.Folder, movie.Output.Directory));
            var composed = await runner.RunAsync(installation,
                new RenderJob(script, bundle.Folder, movie.OutputFrameCount, output)
                {
                    ScriptArguments = ["--plan", top, "--stage", "compose", "--frames", framesDir, .. modules],
                }, progress, ct).ConfigureAwait(false);
            return (composed, perFrame);
        }

        var scene = bundle.Scene;
        var sceneOutput = PlanOutputs.SpecOf(scene, Path.Combine(bundle.Folder, scene.Output.Directory));
        if (scene.Output.Kind == PlanOutputKind.Video)
        {
            // A movie takes two stages, texts or not: its frames stay in the scratch folder, so
            // that a job stopped or cut short goes on from them when it is taken again.
            var frames = await runner.RunAsync(installation,
                new RenderJob(script, bundle.Folder, PlanOutputs.StageFrameCount(scene), PlanOutputs.FramesOf(scene, framesDir))
                {
                    ScriptArguments = ["--plan", top, "--stage", "frames", "--frames", framesDir, .. modules],
                    ExtraBytes = DiskPreflight.EstimateBytes(scene.OutputFrameCount, sceneOutput.Width, sceneOutput.Height, sceneOutput.Kind),
                    LogFileName = "job-frames.log",
                }, paced, ct).ConfigureAwait(false);
            if (!frames.Succeeded) return (frames, perFrame);
            var composed = await runner.RunAsync(installation,
                new RenderJob(script, bundle.Folder, scene.OutputFrameCount, sceneOutput)
                {
                    ScriptArguments = ["--plan", top, "--stage", "compose", "--frames", framesDir, .. modules],
                }, progress, ct).ConfigureAwait(false);
            return (BlenderRenderService.Merge(frames, composed), perFrame);
        }
        // Stills and frames: one stage, or two when the scene has texts (the player starts the second itself).
        var result = await runner.RunAsync(installation,
            new RenderJob(script, bundle.Folder, scene.OutputFrameCount, sceneOutput)
            {
                ScriptArguments = ["--plan", top, "--frames", framesDir, .. modules],
                ExtraBytes = scene.HasTexts ? PlanOutputs.FrameBytes(scene) : 0,
            }, paced, ct).ConfigureAwait(false);
        return (result, perFrame);
    }

    /// <summary>Moves the outputs a job's render wrote, and the subtitles of its bundle that lie
    /// beside them, to the outbox under the job's name. Nothing else leaves the scratch folder.</summary>
    private static IReadOnlyList<string> Deliver(BundleJob job, RenderResult rendered, UnpackedBundle bundle, string bundleDir)
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
            string target = Path.Combine(job.Outbox, job.Id + suffix + extension);
            File.Move(full, target, overwrite: true);
            delivered.Add(target);
        }
        return delivered;
    }

    /// <summary>Blender's logs of the job, one after the other, as <c>&lt;job&gt;.log</c> in the outbox.</summary>
    private static void CopyLog(BundleJob job, string bundleDir)
    {
        var logs = Directory.EnumerateFiles(bundleDir, "job*.log").Order(StringComparer.Ordinal).ToList();
        using var target = new FileStream(Path.Combine(job.Outbox, job.Id + LogSuffix), FileMode.Create, FileAccess.Write);
        foreach (string log in logs)
        {
            // Streamed: the log of a render of days can be large.
            target.Write(System.Text.Encoding.UTF8.GetBytes($"==== {Path.GetFileName(log)}{Environment.NewLine}"));
            using var source = File.OpenRead(log);
            source.CopyTo(target);
        }
    }

    /// <summary>Keeps the record up to date with the progress, and tells whoever listens once a
    /// second at most.</summary>
    private sealed class StatusProgress(Action<RenderJobRecord> status, Func<RenderJobRecord> get, Action<RenderJobRecord> set) : IProgress<RenderProgress>
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
                status(record);
            }
        }
    }

    private sealed class Relay(Action<RenderProgress> report) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => report(value);
    }

    private static string Camel(RenderFailureKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
}

/// <summary>The scratch folders of a worker's jobs, one per job.</summary>
internal static class ScratchFolders
{
    /// <summary>What a job keeps in its scratch folder: the bundle unpacked and its zip, the
    /// frames, and the results on their way.</summary>
    private static readonly HashSet<string> JobEntries = new(StringComparer.Ordinal)
    {
        "bundle", PlanOutputs.FramesFolderName, "out", "bundle.zip", "bundle.zip.part",
    };

    /// <summary>
    /// Removes the job folders under <paramref name="scratch"/> (those whose name
    /// <paramref name="isJob"/> takes) in which nothing was written for longer than
    /// <paramref name="keep"/>: the frames of a job that did not end here and never came back.
    /// Returns each one removed with the bytes it held. A folder that cannot be removed now is
    /// tried again next time. A folder that holds anything but what a job keeps there is not a
    /// job's, and is left alone (a scratch folder named by mistake).
    /// </summary>
    public static IReadOnlyList<(string Name, long Bytes)> Prune(string scratch, TimeSpan keep, Func<string, bool> isJob)
    {
        var removed = new List<(string, long)>();
        if (keep <= TimeSpan.Zero || !Directory.Exists(scratch)) return removed;
        var before = DateTime.UtcNow - keep;
        IEnumerable<DirectoryInfo> folders;
        try { folders = new DirectoryInfo(scratch).EnumerateDirectories().Where(d => isJob(d.Name)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return removed; }
        foreach (var folder in folders)
        {
            try
            {
                if (folder.EnumerateFileSystemInfos().Any(e => !JobEntries.Contains(e.Name))) continue;
                var entries = folder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).ToList();
                var newest = entries.Select(e => e.LastWriteTimeUtc).Append(folder.LastWriteTimeUtc).Max();
                if (newest >= before) continue;
                long bytes = entries.OfType<FileInfo>().Sum(f => f.Length);
                folder.Delete(recursive: true);
                removed.Add((folder.Name, bytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }
}
