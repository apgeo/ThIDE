// Renders one at a time, in the order they were asked for. Blender takes every core it is given
// (and a GPU), so two renders at once only slow each other down: a second render waits for the
// first instead of being refused. The jobs are kept as records (RenderJobRecord) in a small file,
// so that after a restart the jobs that had not ended are known, and whoever keeps the queue can
// start them again; their frames are still there, so they go on where they stopped.

using Therion.Blender;

namespace Therion.Blender.Execution;

/// <summary>A FIFO queue of renders, one running at a time.</summary>
public sealed class RenderQueue : IDisposable
{
    /// <summary>How many ended jobs are remembered.</summary>
    public const int KeptEnded = 20;

    /// <summary>The owner of a job that <see cref="Resume"/> started again: whoever asked for it
    /// first is gone.</summary>
    public const string RestartOwner = "restart";

    private readonly object _lock = new();
    private readonly List<Entry> _entries = [];   // in the order they were queued
    private readonly string? _stateFile;
    private readonly List<RenderJobRecord> _interrupted;
    private Entry? _running;
    private DateTimeOffset _written;
    private bool _disposed;

    private sealed class Entry(RenderJobRecord record)
    {
        public RenderJobRecord Record = record;
        public readonly TaskCompletionSource Turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Cancel = new();
        public Action? Moved;
    }

    /// <param name="stateFile">The file the jobs are kept in; null keeps them in memory alone.
    /// What the file holds when the queue is made is read: the jobs that had not ended are
    /// <see cref="Interrupted"/>.</param>
    public RenderQueue(string? stateFile = null)
    {
        _stateFile = stateFile;
        _interrupted = [];
        if (stateFile is null || !File.Exists(stateFile)) return;
        IReadOnlyList<RenderJobRecord> jobs;
        try { jobs = RenderJobFile.Restarted(RenderJobFile.Read(File.ReadAllText(stateFile))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        _interrupted.AddRange(jobs.Where(j => !j.IsFinished));
        foreach (var ended in jobs.Where(j => j.IsFinished).TakeLast(KeptEnded))
            _entries.Add(new Entry(ended));
    }

    /// <summary>The queue changed: a job was queued, started, moved on or ended. Raised on the
    /// thread that changed it.</summary>
    public event EventHandler? Changed;

    /// <summary>A job ended. Raised on the thread that ran it.</summary>
    public event EventHandler<RenderJobRecord>? JobEnded;

    /// <summary>The jobs that had not ended when the queue was last kept (a restart cut them
    /// short), oldest first, as <see cref="Resume"/> takes them; empty after it has.</summary>
    public IReadOnlyList<RenderJobRecord> Interrupted { get { lock (_lock) return [.. _interrupted]; } }

    /// <summary>Every job the queue knows: the ended ones it remembers, the running one and the
    /// queued ones, in the order they were queued.</summary>
    public IReadOnlyList<RenderJobRecord> Jobs { get { lock (_lock) return _entries.Select(e => e.Record).ToList(); } }

    /// <summary>The job running now, or null.</summary>
    public RenderJobRecord? Running { get { lock (_lock) return _running?.Record; } }

    /// <summary>How many jobs wait before the one with <paramref name="id"/> (0 when it runs or
    /// is next); -1 when it is not waiting.</summary>
    public int Ahead(string id)
    {
        lock (_lock)
        {
            int index = _entries.FindIndex(e => e.Record.Id == id && e.Record.State == RenderJobState.Queued);
            if (index < 0) return -1;
            return _entries.Take(index).Count(e => e.Record.State is RenderJobState.Queued or RenderJobState.Running);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> when every job queued before it has ended, and returns what
    /// it returns. While it waits, <paramref name="progress"/> hears <see cref="RenderPhase.Queued"/>
    /// with how many jobs are ahead; then it hears the work's own progress.
    /// </summary>
    /// <param name="id">The job's id; null makes one. A job that is started again after a
    /// restart keeps its id.</param>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled, or the
    /// job was (<see cref="Cancel"/>), while it waited or ran.</exception>
    public async Task<RenderResult> RunAsync(
        RenderJobRequest request,
        Func<IProgress<RenderProgress>?, CancellationToken, Task<RenderResult>> work,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default,
        string? id = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        var entry = new Entry(new RenderJobRecord
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..12],
            Request = request,
            State = RenderJobState.Queued,
            QueuedUtc = DateTimeOffset.UtcNow,
        });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, entry.Cancel.Token);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _entries.Add(entry);
            _interrupted.RemoveAll(j => j.Id == entry.Record.Id);
            if (_running is null) Start(entry);
        }
        Save(force: true);
        Changed?.Invoke(this, EventArgs.Empty);

        void Waiting()
        {
            int ahead = Ahead(entry.Record.Id);
            if (ahead >= 0)
                progress?.Report(new RenderProgress(RenderPhase.Queued, ahead == 1 ? "Waiting for 1 render to end" : $"Waiting for {ahead} renders to end"));
        }
        entry.Moved = Waiting;
        Waiting();

        RenderResult? result = null;
        double? perFrame = null;
        try
        {
            using (linked.Token.Register(() => entry.Turn.TrySetCanceled()))
                await entry.Turn.Task.ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            Changed?.Invoke(this, EventArgs.Empty);
            var relay = new Relay(tick =>
            {
                if (tick.SecondsPerFrame is { } s) perFrame = s;
                lock (_lock) entry.Record = entry.Record with { Progress = RenderJobProgress.From(tick) };
                Save(force: false);
                progress?.Report(tick);
            });
            result = await work(relay, linked.Token).ConfigureAwait(false);
            End(entry, RenderJobResult.From(result, perFrame), cancelled: result.FailureKind == RenderFailureKind.Cancelled);
            return result;
        }
        catch (OperationCanceledException)
        {
            End(entry, RenderJobResult.Failure("cancelled", "The render was cancelled."), cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            End(entry, RenderJobResult.Failure("error", ex.Message));
            throw;
        }
    }

    /// <summary>Cancels the job with <paramref name="id"/>: a queued one leaves the queue, a
    /// running one is stopped (Blender with it). False when there is no such job that has not ended.</summary>
    public bool Cancel(string id)
    {
        Entry? entry;
        lock (_lock) entry = _entries.FirstOrDefault(e => e.Record.Id == id && !e.Record.IsFinished);
        if (entry is null) return false;
        try { entry.Cancel.Cancel(); }
        catch (ObjectDisposedException) { return false; }
        return true;
    }

    /// <summary>
    /// Starts again every job a restart cut short (<see cref="Interrupted"/>), in their order,
    /// each through <paramref name="replay"/>, which turns a request into its render. A job whose
    /// request cannot be replayed (null from <paramref name="replay"/>'s caller) is dropped.
    /// Returns the jobs' tasks; each ends as the job does and never throws.
    /// </summary>
    public IReadOnlyList<Task> Resume(Func<RenderJobRequest, Func<IProgress<RenderProgress>?, CancellationToken, Task<RenderResult>>?> replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        List<RenderJobRecord> jobs;
        lock (_lock)
        {
            jobs = [.. _interrupted];
            _interrupted.Clear();
        }
        var tasks = new List<Task>();
        foreach (var job in jobs)
        {
            if (replay(job.Request) is not { } work) continue;
            // Queued here, one after the other, so that they keep their order.
            tasks.Add(Swallow(RunAsync(job.Request with { Owner = RestartOwner }, work, null, CancellationToken.None, job.Id)));
        }
        Save(force: true);
        return tasks;
    }

    private static async Task Swallow(Task job)
    {
        try { await job.ConfigureAwait(false); }
        catch (Exception) { }  // the job's record says how it ended
    }

    /// <summary>Cancels every job that has not ended.</summary>
    public void Dispose()
    {
        List<Entry> open;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            open = _entries.Where(e => !e.Record.IsFinished).ToList();
        }
        foreach (var entry in open)
        {
            try { entry.Cancel.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private void Start(Entry entry)
    {
        _running = entry;
        entry.Record = entry.Record with { State = RenderJobState.Running, StartedUtc = DateTimeOffset.UtcNow };
        entry.Turn.TrySetResult();
    }

    private void End(Entry entry, RenderJobResult result, bool cancelled = false)
    {
        List<Action> moved;
        lock (_lock)
        {
            entry.Record = entry.Record.Ended(result, DateTimeOffset.UtcNow, cancelled);
            if (ReferenceEquals(_running, entry))
            {
                _running = null;
                if (!_disposed && _entries.FirstOrDefault(e => e.Record.State == RenderJobState.Queued && !e.Cancel.IsCancellationRequested) is { } next)
                    Start(next);
            }
            // Forget the oldest ended jobs beyond those that are remembered.
            var ended = _entries.Where(e => e.Record.IsFinished).ToList();
            foreach (var old in ended.Take(Math.Max(0, ended.Count - KeptEnded))) _entries.Remove(old);
            moved = _entries.Where(e => e.Record.State == RenderJobState.Queued && e.Moved is not null).Select(e => e.Moved!).ToList();
        }
        Save(force: true);
        foreach (var report in moved) report();
        Changed?.Invoke(this, EventArgs.Empty);
        JobEnded?.Invoke(this, entry.Record);
    }

    /// <summary>Keeps the jobs in the state file: at once for a change of state, at most once a
    /// second for progress. A file that cannot be written is not worth failing a render for.</summary>
    private void Save(bool force)
    {
        if (_stateFile is null) return;
        string text;
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (!force && now - _written < TimeSpan.FromSeconds(1)) return;
            _written = now;
            text = RenderJobFile.Write([.. _entries.Select(e => e.Record), .. _interrupted]);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_stateFile))!);
                string temporary = _stateFile + ".tmp";
                File.WriteAllText(temporary, text);
                File.Move(temporary, _stateFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class Relay(Action<RenderProgress> report) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => report(value);
    }
}
