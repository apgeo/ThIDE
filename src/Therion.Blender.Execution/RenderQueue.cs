// Renders one at a time, in the order they were asked for. Blender takes every core it is given
// (and a GPU), so two renders at once only slow each other down: a second render waits for the
// first instead of being refused. The jobs are kept as records (RenderJobRecord) in a small file,
// so that after a restart the jobs that had not ended are known, and whoever keeps the queue can
// start them again; their frames are still there, so they go on where they stopped.
//
// One queue at a time keeps the file: it holds a lock file beside it for as long as it lives. A
// second queue made on the same file (a second ThIDE of the same user) finds it held, and keeps
// its jobs in memory only: it neither starts again the jobs the first is running nor writes over
// its file.

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

    /// <summary>The suffix of the lock file kept beside the state file.</summary>
    public const string LockSuffix = ".lock";

    private readonly object _lock = new();
    private readonly List<Entry> _entries = [];   // in the order they were queued
    private readonly string? _stateFile;
    private readonly List<RenderJobRecord> _interrupted;
    private FileStream? _held;
    private Entry? _running;
    private DateTimeOffset _written;
    private bool _disposed;

    private sealed class Entry(RenderJobRecord record)
    {
        public RenderJobRecord Record = record;
        public readonly TaskCompletionSource Turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Cancel = new();
        public Action? Moved;
    }

    /// <param name="stateFile">The file the jobs are kept in; null keeps them in memory alone.
    /// What the file holds when the queue is made is read: the jobs that had not ended are
    /// <see cref="Interrupted"/>. When another queue holds the file (<see cref="HeldElsewhere"/>),
    /// it is neither read nor written.</param>
    public RenderQueue(string? stateFile = null)
    {
        _interrupted = [];
        if (stateFile is null) return;
        switch (TryHold(stateFile, out _held))
        {
            case false:
                HeldElsewhere = true;
                return;
            case null:
                break;   // no lock can be made there: the file is kept as it was before there was one
        }
        _stateFile = stateFile;
        if (!File.Exists(stateFile)) return;
        IReadOnlyList<RenderJobRecord> jobs;
        try { jobs = RenderJobFile.Restarted(RenderJobFile.Read(File.ReadAllText(stateFile))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        _interrupted.AddRange(jobs.Where(j => !j.IsFinished));
        foreach (var ended in jobs.Where(j => j.IsFinished).TakeLast(KeptEnded))
            _entries.Add(new Entry(ended));
    }

    /// <summary>Takes the lock beside <paramref name="stateFile"/>: true when this queue holds it,
    /// false when another does, null when no lock can be made there.</summary>
    private static bool? TryHold(string stateFile, out FileStream? held)
    {
        held = null;
        string path = Path.GetFullPath(stateFile) + LockSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // FileShare.None: on Windows a share lock, elsewhere an advisory lock (flock); either
            // goes with the process, however it ends, so a lock is never left behind.
            held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) when (File.Exists(path)) { return false; }
        catch (IOException) { return null; }
    }

    /// <summary>Another queue (another ThIDE) keeps the state file this queue was made on: this
    /// one keeps its jobs in memory only, starts again none of the file's, and its jobs do not go
    /// on after a restart.</summary>
    public bool HeldElsewhere { get; }

    /// <summary>The queue keeps its jobs in a file, so that those a restart cuts short (or
    /// <see cref="Shutdown"/> stops) go on at the next start.</summary>
    public bool KeepsJobs => _stateFile is not null;

    /// <summary>Whether the queue was shut down (<see cref="Shutdown"/>, <see cref="Dispose"/>).</summary>
    public bool IsShutDown { get { lock (_lock) return _disposed; } }

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
        RaiseChanged();

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
            RaiseChanged();
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
            End(entry, RenderJobResult.Failure(RenderNodeFailure.Cancelled, "The render was cancelled."), cancelled: true);
            throw;
        }
        catch (Exception ex)
        {
            End(entry, RenderJobResult.Failure(RenderNodeFailure.Error, ex.Message));
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
    /// request cannot be replayed (null from <paramref name="replay"/>) ends as failed, with why
    /// when its model is no longer there. Returns the started jobs' tasks; each ends as the job
    /// does and never throws.
    /// </summary>
    public IReadOnlyList<Task> Resume(Func<RenderJobRequest, Func<IProgress<RenderProgress>?, CancellationToken, Task<RenderResult>>?> replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        List<RenderJobRecord> jobs;
        lock (_lock)
        {
            if (_disposed) return [];
            jobs = [.. _interrupted];
            _interrupted.Clear();
        }
        var tasks = new List<Task>();
        var dropped = new List<RenderJobRecord>();
        foreach (var job in jobs)
        {
            if (replay(job.Request) is not { } work)
            {
                string why = job.Request.Model is { } model && !File.Exists(model)
                    ? $"The render could not go on after the restart: the model is no longer at {model}."
                    : "The render could not go on after the restart: what it was asked to render can no longer be read.";
                var ended = job.Ended(RenderJobResult.Failure(RenderNodeFailure.Error, why), DateTimeOffset.UtcNow);
                lock (_lock) _entries.Add(new Entry(ended));
                dropped.Add(ended);
                continue;
            }
            // Queued here, one after the other, so that they keep their order.
            tasks.Add(Swallow(RunAsync(job.Request with { Owner = RestartOwner }, work, null, CancellationToken.None, job.Id)));
        }
        Save(force: true);
        if (dropped.Count > 0)
        {
            RaiseChanged();
            foreach (var job in dropped) RaiseEnded(job);
        }
        return tasks;
    }

    private static async Task Swallow(Task job)
    {
        try { await job.ConfigureAwait(false); }
        catch (Exception) { }  // the job's record says how it ended
    }

    /// <summary>
    /// Stops every job that has not ended (Blender with the running one) and lets the state file
    /// go, for closing the application. A queue that keeps its jobs in a file
    /// (<see cref="KeepsJobs"/>) leaves them in it as they stood, so that the next queue made on
    /// the file starts them again (<see cref="Interrupted"/>) and they go on with their frames; in
    /// memory they end as cancelled. Waits at most <paramref name="wait"/> for the running job's
    /// work to return. Returns how many jobs it stopped. Nothing can be queued after.
    /// </summary>
    public int Shutdown(TimeSpan wait)
    {
        List<Entry> open;
        Entry? running;
        lock (_lock)
        {
            if (_disposed) return 0;
            // The file as it stands now is what the next start goes on from; the cancels below
            // end the jobs in memory only.
            Save(force: true);
            _disposed = true;
            open = _entries.Where(e => !e.Record.IsFinished).ToList();
            running = _running;
        }
        foreach (var entry in open)
        {
            try { entry.Cancel.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        if (running is not null && wait > TimeSpan.Zero)
        {
            try { running.Ended.Task.Wait(wait); }
            catch (AggregateException) { }
        }
        lock (_lock)
        {
            _held?.Dispose();
            _held = null;
        }
        return open.Count;
    }

    /// <summary><see cref="Shutdown"/> without waiting: stops every job that has not ended; a
    /// queue kept in a file keeps them in it for the next start.</summary>
    public void Dispose() => Shutdown(TimeSpan.Zero);

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
        entry.Ended.TrySetResult();
        foreach (var report in moved)
        {
            try { report(); }
            catch (Exception) { }   // a listener's fault is not the queue's
        }
        RaiseChanged();
        RaiseEnded(entry.Record);
    }

    /// <summary>Raises <see cref="Changed"/>: a handler that throws stops neither the others nor
    /// the queue (a job left running for ever, with every later one waiting behind it).</summary>
    private void RaiseChanged()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { }
        }
    }

    private void RaiseEnded(RenderJobRecord record)
    {
        if (JobEnded is not { } handlers) return;
        foreach (EventHandler<RenderJobRecord> handler in handlers.GetInvocationList())
        {
            try { handler(this, record); }
            catch (Exception) { }
        }
    }

    /// <summary>Keeps the jobs in the state file: at once for a change of state, at most once a
    /// second for progress. A file that cannot be written is not worth failing a render for.</summary>
    private void Save(bool force)
    {
        if (_stateFile is null) return;
        string text;
        lock (_lock)
        {
            if (_disposed) return;   // the file keeps the jobs as they stood when the queue shut down
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
