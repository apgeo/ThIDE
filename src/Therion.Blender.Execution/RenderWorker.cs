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

using Therion.Blender.Player;

namespace Therion.Blender.Execution;

/// <summary>How a <see cref="RenderWorker"/> runs.</summary>
public sealed record RenderWorkerOptions
{
    /// <summary>The folder bundles arrive in (<c>&lt;job&gt;.zip</c>) and cancels too (<c>&lt;job&gt;.cancel</c>).</summary>
    public required string Inbox { get; init; }

    /// <summary>The folder the results go to.</summary>
    public required string Outbox { get; init; }

    /// <summary>Where the bundles are unpacked and their frames are kept while they render. It
    /// must be the user's alone (<see cref="PrivateFolders.Ensure"/>);
    /// <see cref="PrivateFolders.Of"/>("render-worker") by default in the CLI.</summary>
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
    private readonly BundleJobRunner _core;
    private readonly RenderWorkerOptions _options;
    private readonly Action<string>? _log;

    /// <param name="blender">The operator's Blender, if named.</param>
    /// <param name="log">Hears one line for each thing the worker does.</param>
    public RenderWorker(BlenderLocator locator, BlenderRunner runner, RenderWorkerOptions options,
        Func<string?>? blender = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _core = new BundleJobRunner(locator, runner, options.ModuleFolders, blender);
        _log = log;
    }

    public const string StatusSuffix = ".status.json";
    public const string CancelSuffix = ".cancel";
    public const string LogSuffix = BundleJobRunner.LogSuffix;

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
    /// <exception cref="UnsafeFolderException">The scratch folder is not the user's alone.</exception>
    public async Task<int> RunAsync(bool once, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.Outbox);
        PrivateFolders.Ensure(_options.Scratch);
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
        PrivateFolders.Ensure(_options.Scratch);
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
        _log?.Invoke($"{id}: taken");

        // The cancel file, looked at while the job runs.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string cancelFile = Path.Combine(_options.Inbox, id + CancelSuffix);
        using var watcher = new Timer(_ => { if (File.Exists(cancelFile)) TryCancel(stop); }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        var job = new BundleJob
        {
            Id = id, ZipPath = zipPath, Scratch = scratch, Outbox = _options.Outbox, Limits = _options.Limits, MaxTime = _options.MaxTime,
        };
        var outcome = await _core.RunAsync(job, record, WriteStatus, stop.Token).ConfigureAwait(false);
        record = outcome.Record;
        WriteStatus(record);
        // On one line, without the control characters a bundle's names may hold.
        _log?.Invoke(PortableNames.Printable($"{id}: {record.State.ToString().ToLowerInvariant()}{(record.Result?.Error is { } error ? ": " + error : "")}", 4000));

        // A job that ended is done with: its zip goes, and so does its scratch unless it failed in
        // Blender or ran out of time (its frames stay, for the same bundle sent again to go on
        // with). A stopped job keeps both and goes on when its cancel file is removed, or when the
        // worker that was stopped starts again.
        if (record.State != RenderJobState.Cancelled)
        {
            TryDelete(zipPath);
            if (record.State == RenderJobState.Succeeded || outcome.Refused) TryDeleteFolder(scratch);
            else TryDeleteFolder(Path.Combine(scratch, "bundle"));
        }
        return record;
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
