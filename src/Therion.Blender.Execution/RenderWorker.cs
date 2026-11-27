// A render worker that takes render bundles from a folder and leaves what they render in another:
// for a machine with Blender (a GPU) and nothing else of ThIDE, filled by whatever moves files to
// it. One bundle at a time, oldest first. A job is the zip <job>.zip in the inbox; while it runs,
// <job>.status.json in the outbox says how far it got; on success the outbox gets the movie (or
// stills), its subtitles and Blender's log, named after the job; <job>.cancel in the inbox stops
// it, and a stopped or cut-short job goes on with its frames when it is taken again.
//
// A zip is taken only once it has arrived: one that cannot be read yet as a zip and was written to
// lately is left to arrive (an upload under way). Workers that share an inbox each hold a lock file
// beside the zip they render, so that no two take the same job. A zip that cannot be removed after
// its job ended (a read-only share) is not taken again while it stays as it was.
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

    /// <summary>The longest a job may run; it is stopped after, its frames kept. A limit above
    /// <see cref="LongestMaxTime"/> is that.</summary>
    public TimeSpan MaxTime { get; init; } = TimeSpan.FromHours(72);

    /// <summary>The longest time limit a worker takes: 30 days.</summary>
    public static readonly TimeSpan LongestMaxTime = TimeSpan.FromDays(30);

    /// <summary>Extra module folders the operator trusts; a bundle cannot add any.</summary>
    public IReadOnlyList<string> ModuleFolders { get; init; } = [];

    /// <summary>How often the inbox is looked at, and a running job's cancel file.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a zip that cannot be read as one yet is taken to be still arriving after
    /// it was last written to; after that, it is taken (and refused).</summary>
    public TimeSpan Arrival { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the scratch folder of a job that did not end here (it failed in Blender,
    /// or ran out of time) is kept, untouched, for the same bundle sent again to go on with.</summary>
    public TimeSpan KeepScratch { get; init; } = TimeSpan.FromDays(14);
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

    /// <summary>The suffix of the lock file a worker holds beside the zip it renders.</summary>
    public const string LockSuffix = ".lock";

    /// <summary>The zips passed over while they stay as they were: those that could not be removed
    /// after their job ended.</summary>
    private readonly Dictionary<string, (long Size, DateTime WrittenUtc)> _passed = new(StringComparer.Ordinal);

    /// <summary>The zips said to be arriving, so that it is said once.</summary>
    private readonly HashSet<string> _arriving = new(StringComparer.Ordinal);
    private DateTime _pruned;

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
            PruneScratch();
            var (zip, claim) = Next();
            if (zip is null)
            {
                if (once) break;
                try { await Task.Delay(_options.Poll, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            RenderJobRecord record;
            using (claim) record = await ProcessAsync(zip, ct).ConfigureAwait(false);
            taken++;
            // A job that ended takes its zip with it; one whose zip stays (a read-only share, a
            // lock held by a sync client) is not rendered again while the zip stays as it was.
            if (record.State != RenderJobState.Cancelled && Stamp(zip) is { } stamp)
            {
                _passed[zip] = stamp;
                _log?.Invoke($"{record.Id}: its zip could not be removed from the inbox; it is not taken again unless it changes");
            }
        }
        return taken;
    }

    /// <summary>The oldest zip of the inbox that can be taken now, with the lock that claims it
    /// (null where no lock can be made: a read-only inbox).</summary>
    private (string? Zip, IDisposable? Claim) Next()
    {
        foreach (string zip in Pending())
        {
            if (Stamp(zip) is not { } stamp) continue;
            if (_passed.TryGetValue(zip, out var passed))
            {
                if (passed == stamp) continue;
                _passed.Remove(zip);   // it changed: a new bundle under the same name
            }
            if (IsArriving(zip, stamp))
            {
                if (_arriving.Add(zip)) _log?.Invoke($"{JobId(zip)}: the zip is not whole yet; waiting for it to arrive");
                continue;
            }
            _arriving.Remove(zip);
            if (!TryClaim(zip, out var claim)) continue;   // another worker renders it
            if (File.Exists(zip)) return (zip, claim);
            claim?.Dispose();   // the worker that had it has just ended it
        }
        return (null, null);
    }

    /// <summary>A zip that cannot be read as one yet and was written to within
    /// <see cref="RenderWorkerOptions.Arrival"/>: an upload or a copy under way.</summary>
    private bool IsArriving(string zip, (long Size, DateTime WrittenUtc) stamp)
    {
        if (DateTime.UtcNow - stamp.WrittenUtc > _options.Arrival) return false;
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            return false;
        }
        catch (InvalidDataException) { return true; }
        catch (IOException) { return true; }   // on Windows: still open for writing
        catch (UnauthorizedAccessException) { return false; }   // refused when it is taken, with why
    }

    /// <summary>Takes the lock beside <paramref name="zip"/>; false when another worker holds it.</summary>
    private static bool TryClaim(string zip, out IDisposable? claim)
    {
        claim = null;
        string path = zip + LockSuffix;
        try
        {
            claim = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (UnauthorizedAccessException) { return true; }   // a read-only inbox: no lock to be had, nor needed by one worker
        catch (IOException) when (File.Exists(path)) { return false; }
        catch (IOException) { return true; }
    }

    private static (long, DateTime)? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Removes the scratch folders left untouched longer than
    /// <see cref="RenderWorkerOptions.KeepScratch"/>; at most once an hour.</summary>
    private void PruneScratch()
    {
        if (DateTime.UtcNow - _pruned < TimeSpan.FromHours(1)) return;
        _pruned = DateTime.UtcNow;
        foreach (var (name, bytes) in ScratchFolders.Prune(_options.Scratch, _options.KeepScratch, _ => true))
            _log?.Invoke($"{name}: its scratch folder was left untouched for {_options.KeepScratch.TotalDays:0.#} days; removed ({bytes / 1_000_000} MB)");
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
