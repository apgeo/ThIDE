// A render worker that takes its jobs from a server (a render node of a server such as SilexGIS):
// it claims a job, downloads its bundle (its size and SHA-256 checked before anything of it is
// read), renders it with the loop every worker shares (BundleJobRunner, and every check of
// RenderBundleReader), keeps its lease with heartbeats that say how far it got, uploads only the
// files it names and says the job is complete, or that it failed and why. The protocol is
// RenderNodeProtocol's (docs/dev/render-worker-protocol.md).
//
// The node token goes to the server's own origin only, over HTTPS (plain HTTP to this machine
// only), and into no log. A job that the server cancels, or whose lease is lost, stops Blender's
// process tree; a job taken again on the same node goes on with the frames under its scratch folder.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Therion.Blender.Player;

namespace Therion.Blender.Execution;

/// <summary>How a <see cref="RenderNodeWorker"/> runs.</summary>
public sealed record RenderNodeOptions
{
    /// <summary>The server URL the endpoints are relative to.</summary>
    public required Uri Server { get; init; }

    /// <summary>The node token, as read from the operator's token file.</summary>
    public required string Token { get; init; }

    /// <summary>The node's name, as the server shows it.</summary>
    public string NodeName { get; init; } = Environment.MachineName;

    /// <summary>Where the bundles are downloaded and unpacked and their frames are kept: one
    /// folder for each job, named after its id.</summary>
    public required string Scratch { get; init; }

    /// <summary>What a bundle may hold at most; a job's own limits can only lower these.</summary>
    public RenderBundleLimits Limits { get; init; } = new();

    /// <summary>The longest a job may run; a job's own limit can only shorten it.</summary>
    public TimeSpan MaxTime { get; init; } = TimeSpan.FromHours(72);

    /// <summary>Extra module folders the operator trusts; a bundle cannot add any.</summary>
    public IReadOnlyList<string> ModuleFolders { get; init; } = [];

    /// <summary>How long to wait for a job: the server may hold a claim open that long, and
    /// a claim that comes back with none is made again after it.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The first wait after a failed call, doubled with each failure after it, up to <see cref="MaxBackoff"/>.</summary>
    public TimeSpan FirstBackoff { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times a bundle download is tried before the job fails.</summary>
    public int DownloadTries { get; init; } = 5;

    /// <summary>The worker's version, sent with each claim.</summary>
    public string? WorkerVersion { get; init; }
}

/// <summary>The server refused the node token (401 or 403): the worker stops.</summary>
public sealed class RenderNodeUnauthorizedException(string message) : Exception(message);

/// <summary>How a <see cref="RenderNodeWorker"/> run ended.</summary>
/// <param name="Jobs">How many jobs it took.</param>
/// <param name="Unauthorized">It stopped because the server refused its token.</param>
public sealed record RenderNodeRun(int Jobs, bool Unauthorized);

/// <summary>Takes render jobs from a server and renders them, one at a time.</summary>
public sealed class RenderNodeWorker : IDisposable
{
    private readonly BundleJobRunner _core;
    private readonly BlenderLocator _locator;
    private readonly Func<string?>? _blender;
    private readonly RenderNodeOptions _options;
    private readonly Action<string>? _log;
    private readonly HttpClient _http;
    private readonly Uri _server;
    private string? _device;

    /// <param name="blender">The operator's Blender, if named.</param>
    /// <param name="log">Hears one line for each thing the worker does; never the token.</param>
    /// <param name="handler">The HTTP handler, for tests; a handler that follows no redirect otherwise.</param>
    public RenderNodeWorker(BlenderLocator locator, BlenderRunner runner, RenderNodeOptions options,
        Func<string?>? blender = null, Action<string>? log = null, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        if (!RenderNodeProtocol.IsAllowedAddress(options.Server, out string? problem))
            throw new ArgumentException($"The server URL cannot be used: {problem}.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Token)) throw new ArgumentException("The node token is empty.", nameof(options));
        _options = options;
        _locator = locator;
        _blender = blender;
        _log = log;
        _core = new BundleJobRunner(locator, runner, options.ModuleFolders, blender);
        // The endpoints are relative to the server URL, which is a folder.
        string url = options.Server.AbsoluteUri;
        _server = new Uri(url.EndsWith('/') ? url : url + "/");
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => _http.Dispose();

    /// <summary>The scratch folder of a job.</summary>
    public string JobFolder(string jobId) => Path.Combine(_options.Scratch, jobId);

    /// <summary>
    /// Claims jobs and renders them one after the other until <paramref name="ct"/> is
    /// cancelled or the server refuses the token; with <paramref name="once"/>, makes one claim
    /// and renders what it brings. A failed claim is made again after a wait that grows.
    /// </summary>
    public async Task<RenderNodeRun> RunAsync(bool once, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.Scratch);
        var locate = _locator.Locate(_blender?.Invoke());
        string[] versions = locate.IsUsable ? [locate.Installation!.Version.ToString()] : [];
        if (!locate.IsUsable) Log($"no usable Blender: {locate.Detail}");
        Log($"render node '{_options.NodeName}': {_server.GetLeftPart(UriPartial.Path)}{(once ? " (one claim)" : "")}");
        int jobs = 0, failures = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var clock = Stopwatch.StartNew();
                RenderNodeJob? job;
                try
                {
                    job = await ClaimAsync(versions, ct).ConfigureAwait(false);
                    failures = 0;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    // Whatever the server answers a claim with, other than a job, none or a refused
                    // token, is asked again after a wait.
                    failures++;
                    Log($"claim failed: {Describe(ex)}");
                    if (once) break;
                    await Wait(RenderNodeProtocol.Backoff(failures, _options.FirstBackoff, _options.MaxBackoff, Random.Shared.NextDouble()), ct).ConfigureAwait(false);
                    continue;
                }
                if (job is null)
                {
                    if (once) break;
                    await Wait(_options.Poll - clock.Elapsed, ct).ConfigureAwait(false);
                    continue;
                }
                jobs++;
                try { await ProcessAsync(job, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log($"{job.JobId}: the node could not go on with the job: {Describe(ex)}");
                }
                if (once) break;
            }
        }
        catch (RenderNodeUnauthorizedException ex)
        {
            Log(ex.Message);
            return new RenderNodeRun(jobs, Unauthorized: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        return new RenderNodeRun(jobs, Unauthorized: false);
    }

    /// <summary>Asks the server for a job; null when it has none (204).</summary>
    internal async Task<RenderNodeJob?> ClaimAsync(IReadOnlyList<string> blenderVersions, CancellationToken ct)
    {
        int wait = (int)Math.Max(0, Math.Round(_options.Poll.TotalSeconds));
        var claim = new RenderNodeClaim
        {
            Node = _options.NodeName,
            Worker = _options.WorkerVersion,
            BlenderVersions = blenderVersions,
            Device = _device,
            FreeDiskBytes = DiskPreflight.TryGetFreeBytes(_options.Scratch),
            KeptJobs = KeptJobs(),
            WaitSeconds = wait,
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(wait + 30));
        using var response = await SendAsync(() => Post(RenderNodeProtocol.ClaimPath, RenderNodeProtocol.Write(claim)), timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"the claim was answered {(int)response.StatusCode}", null, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return RenderNodeProtocol.Read<RenderNodeJob>(body) ?? throw new HttpRequestException("the claim was answered with something that is not a job");
    }

    /// <summary>The jobs whose frames this node still holds, so that the server may give them back to it.</summary>
    private IReadOnlyList<string> KeptJobs()
    {
        if (!Directory.Exists(_options.Scratch)) return [];
        return new DirectoryInfo(_options.Scratch).EnumerateDirectories()
            .Where(d => RenderNodeProtocol.IsJobId(d.Name) && Directory.Exists(Path.Combine(d.FullName, PlanOutputs.FramesFolderName)))
            .OrderByDescending(d => d.LastWriteTimeUtc).Take(100).Select(d => d.Name).ToList();
    }

    /// <summary>
    /// Renders the job the server gave and tells it how that ended. Returns the job's record,
    /// or null when the job could not be taken or stopped being this node's (the lease was lost,
    /// or <paramref name="ct"/> stopped the worker: its frames stay for the same job taken again).
    /// </summary>
    /// <exception cref="RenderNodeUnauthorizedException">The server refused the token.</exception>
    internal async Task<RenderJobRecord?> ProcessAsync(RenderNodeJob job, CancellationToken ct)
    {
        string id = job.JobId;
        if (!RenderNodeProtocol.IsJobId(id))
        {
            Log("the server gave a job whose id is not a safe name; it is not taken");
            return null;
        }
        Log($"{id}: claimed (attempt {job.Attempt}{(job.Name is { Length: > 0 } name ? ", " + Clean(name) : "")})");
        using var lease = new Lease(job, ct);
        if (Check(job) is { } badJob)
        {
            await FailAsync(lease, RenderNodeFailure.BadJob, badJob).ConfigureAwait(false);
            return null;
        }
        string scratch = JobFolder(id);
        Directory.CreateDirectory(scratch);
        var beats = Task.Run(() => HeartbeatsAsync(lease));
        try
        {
            lease.Report("downloading");
            string zip = Path.Combine(scratch, "bundle.zip");
            string? download = await DownloadAsync(job, zip, lease).ConfigureAwait(false);
            if (lease.Stopped is { } early) return await StoppedAsync(lease, early, scratch).ConfigureAwait(false);
            if (download is not null)
            {
                bool mismatch = download.StartsWith(RenderNodeFailure.Checksum + ":", StringComparison.Ordinal);
                await FailAsync(lease, mismatch ? RenderNodeFailure.Checksum : RenderNodeFailure.Download,
                    mismatch ? download[(RenderNodeFailure.Checksum.Length + 1)..] : download).ConfigureAwait(false);
                return null;
            }

            lease.Report("checking");
            string outbox = Path.Combine(scratch, "out");
            if (Directory.Exists(outbox)) Directory.Delete(outbox, recursive: true);
            RenderNodeProtocol.TryContainer(job.Container, out var container);
            var record = new RenderJobRecord
            {
                Id = id,
                Request = new RenderJobRequest { Name = job.Name ?? id, Owner = _options.Server.Host, Bundle = "bundle.zip", Draft = job.Plan == RenderNodeProtocol.DraftPlan },
                State = RenderJobState.Running,
                QueuedUtc = DateTimeOffset.UtcNow,
                StartedUtc = DateTimeOffset.UtcNow,
            };
            var outcome = await _core.RunAsync(new BundleJob
            {
                Id = id, ZipPath = zip, Scratch = scratch, Outbox = outbox,
                Choice = RenderBundleChoice.Of(job.Plan == RenderNodeProtocol.DraftPlan, container),
                Limits = Limits(job.Limits), MaxTime = MaxTime(job.Limits),
            }, record, r => lease.Report(r.Progress), lease.Token).ConfigureAwait(false);
            record = outcome.Record;
            if (record.Result?.Device is { } device) _device = device;
            if (lease.Stopped is { } stopped) return await StoppedAsync(lease, stopped, scratch).ConfigureAwait(false);

            if (record.State == RenderJobState.Succeeded)
            {
                lease.Report("uploading");
                string? problem = await UploadAsync(lease, record, outbox).ConfigureAwait(false);
                if (lease.Stopped is { } late) return await StoppedAsync(lease, late, scratch).ConfigureAwait(false);
                if (problem is not null)
                {
                    await FailAsync(lease, RenderNodeFailure.Upload, problem).ConfigureAwait(false);
                    TryDeleteFolder(Path.Combine(scratch, "bundle"));
                    return record;
                }
                Log($"{id}: succeeded");
                TryDeleteFolder(scratch);
                return record;
            }

            string reason = record.Result?.FailureKind is { } kind && RenderNodeFailure.All.Contains(kind) ? kind : RenderNodeFailure.Error;
            await FailAsync(lease, reason, record.Result?.Error ?? "The render failed.").ConfigureAwait(false);
            // A refused bundle is done with; a render that failed keeps its frames for another try here.
            if (outcome.Refused) TryDeleteFolder(scratch);
            else TryDeleteFolder(Path.Combine(scratch, "bundle"));
            return record;
        }
        finally
        {
            lease.End();
            await beats.ConfigureAwait(false);
        }
    }

    /// <summary>What is wrong with a job the worker cannot take; null when it can take it.</summary>
    private string? Check(RenderNodeJob job)
    {
        if (job.Protocol != RenderNodeProtocol.Version) return $"The job is of protocol {job.Protocol}; this worker speaks {RenderNodeProtocol.Version}.";
        if (job.Attempt < 1) return "The job's attempt is not a number from 1.";
        if (job.Plan is not (RenderNodeProtocol.FullPlan or RenderNodeProtocol.DraftPlan)) return $"The job's plan is '{Clean(job.Plan)}', not full or draft.";
        if (!RenderNodeProtocol.TryContainer(job.Container, out _))
            return $"The job asks for the container '{Clean(job.Container)}', which the player does not write (it writes {string.Join(", ", RenderNodeProtocol.Containers)}).";
        if (!RenderNodeProtocol.IsSha256(job.Bundle.Sha256)) return "The bundle's SHA-256 is not 64 lower-case hex digits.";
        if (job.Bundle.Size <= 0) return "The bundle's size is not a number above 0.";
        if (job.Bundle.Size > _options.Limits.MaxZipBytes) return $"The bundle is {job.Bundle.Size} bytes, more than the {_options.Limits.MaxZipBytes} allowed.";
        if (string.IsNullOrWhiteSpace(job.Bundle.Url) || !Uri.TryCreate(_server, job.Bundle.Url, out var url))
            return "The bundle's address is not one.";
        if (!RenderNodeProtocol.IsAllowedAddress(url, out string? problem)) return $"The bundle's address cannot be used: {problem}.";
        return null;
    }

    private RenderBundleLimits Limits(RenderNodeLimits? limits) => limits is null ? _options.Limits : _options.Limits with
    {
        MaxUnpackedBytes = Math.Min(_options.Limits.MaxUnpackedBytes, limits.MaxUnpackedBytes ?? long.MaxValue),
        MaxTriangles = Math.Min(_options.Limits.MaxTriangles, limits.MaxTriangles ?? long.MaxValue),
        MaxWork = Math.Min(_options.Limits.MaxWork, limits.MaxWork ?? double.MaxValue),
    };

    private TimeSpan MaxTime(RenderNodeLimits? limits) =>
        limits?.MaxHours is { } hours && hours > 0 && TimeSpan.FromHours(hours) < _options.MaxTime ? TimeSpan.FromHours(hours) : _options.MaxTime;

    // ---- the lease ----

    private enum Stop { LeaseLost, ServerCancelled, Unauthorized, WorkerStopped }

    /// <summary>A job's lease: until when it is the node's, how far the job got, and why it stopped.</summary>
    private sealed class Lease(RenderNodeJob job, CancellationToken worker) : IDisposable
    {
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(worker);
        private readonly CancellationTokenSource _end = new();
        private readonly Stopwatch _renewed = Stopwatch.StartNew();
        private readonly object _lock = new();
        private RenderNodeHeartbeat _beat = new() { Attempt = job.Attempt, Phase = "claimed" };
        private Stop? _why;

        public RenderNodeJob Job => job;
        public int Seconds { get; set; } = Math.Max(3, job.LeaseSeconds);
        public CancellationToken Token => _stop.Token;
        public CancellationToken Ended => _end.Token;

        /// <summary>Why the job stopped being rendered here; null while it goes on.</summary>
        public Stop? Stopped
        {
            get
            {
                lock (_lock) return _why ?? (worker.IsCancellationRequested ? Stop.WorkerStopped : null);
            }
        }

        public bool Expired => _renewed.Elapsed > TimeSpan.FromSeconds(Seconds);

        public void Renewed() => _renewed.Restart();

        public void StopFor(Stop why)
        {
            lock (_lock) _why ??= why;
            try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void Report(string phase)
        {
            lock (_lock) _beat = new RenderNodeHeartbeat { Attempt = job.Attempt, Phase = phase };
        }

        public void Report(RenderJobProgress? progress)
        {
            if (progress is null) return;
            lock (_lock)
                _beat = new RenderNodeHeartbeat
                {
                    Attempt = job.Attempt, Phase = progress.Phase.Length > 0 ? progress.Phase : "rendering", Message = progress.Message,
                    Segment = progress.Segment, SegmentCount = progress.SegmentCount, Frame = progress.Frame, FrameCount = progress.FrameCount,
                    SecondsPerFrame = progress.SecondsPerFrame, SecondsLeft = progress.SecondsLeft,
                };
        }

        public RenderNodeHeartbeat Beat { get { lock (_lock) return _beat; } }

        public void End()
        {
            try { _end.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _stop.Dispose();
            _end.Dispose();
        }
    }

    /// <summary>Sends a heartbeat every third of the lease until the job ends: it renews the lease,
    /// and its answer may cancel the job. A lease that runs out unrenewed is lost.</summary>
    private async Task HeartbeatsAsync(Lease lease)
    {
        string path = RenderNodeProtocol.HeartbeatPath(lease.Job.JobId);
        while (!lease.Ended.IsCancellationRequested && lease.Stopped is null)
        {
            var interval = RenderNodeProtocol.HeartbeatInterval(lease.Seconds, lease.Job.HeartbeatSeconds);
            try { await Task.Delay(interval, lease.Ended).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Ended);
                timeout.CancelAfter(interval);
                using var response = await SendAsync(() => Post(path, RenderNodeProtocol.Write(lease.Beat)), timeout.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    Log($"{lease.Job.JobId}: the server says the job is no longer this node's ({(int)response.StatusCode}); stopping it");
                    lease.StopFor(Stop.LeaseLost);
                    return;
                }
                if (response.IsSuccessStatusCode)
                {
                    lease.Renewed();
                    var reply = RenderNodeProtocol.Read<RenderNodeHeartbeatReply>(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                    if (reply?.LeaseSeconds is { } seconds && seconds >= 3) lease.Seconds = seconds;
                    if (reply?.Action == RenderNodeProtocol.Cancel)
                    {
                        Log($"{lease.Job.JobId}: cancelled by the server");
                        lease.StopFor(Stop.ServerCancelled);
                        return;
                    }
                    continue;
                }
                Log($"{lease.Job.JobId}: heartbeat answered {(int)response.StatusCode}");
            }
            catch (RenderNodeUnauthorizedException ex)
            {
                Log(ex.Message);
                lease.StopFor(Stop.Unauthorized);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                if (lease.Ended.IsCancellationRequested) return;
                Log($"{lease.Job.JobId}: heartbeat failed: {Describe(ex)}");
            }
            if (lease.Expired)
            {
                Log($"{lease.Job.JobId}: the lease ran out without a heartbeat getting through; stopping the job");
                lease.StopFor(Stop.LeaseLost);
                return;
            }
        }
    }

    /// <summary>A job that stopped being rendered here: a cancelled one is said to be so and is
    /// done with; a lost one keeps its frames (and its bundle) for when it is claimed again.</summary>
    private async Task<RenderJobRecord?> StoppedAsync(Lease lease, Stop why, string scratch)
    {
        string id = lease.Job.JobId;
        switch (why)
        {
            case Stop.ServerCancelled:
                await FailAsync(lease, RenderNodeFailure.Cancelled, "The server cancelled the job; Blender was stopped.", lenient: true).ConfigureAwait(false);
                TryDeleteFolder(scratch);
                break;
            case Stop.Unauthorized:
                throw new RenderNodeUnauthorizedException("The server refused the node token; the worker stops.");
            case Stop.LeaseLost:
                Log($"{id}: lease lost; its frames are kept");
                TryDeleteFolder(Path.Combine(scratch, "bundle"));
                break;
            default:
                Log($"{id}: stopped with the worker; its frames are kept");
                break;
        }
        return null;
    }

    // ---- the bundle ----

    /// <summary>Downloads the bundle to <paramref name="zip"/>, streamed to disk, and checks its
    /// size and SHA-256; null when it is the one the claim describes, or why not (a mismatch as
    /// "checksum:…"). A bundle already there from an earlier try that checks is kept.</summary>
    private async Task<string?> DownloadAsync(RenderNodeJob job, string zip, Lease lease)
    {
        var bundle = job.Bundle;
        if (File.Exists(zip) && new FileInfo(zip).Length == bundle.Size && await Sha256Async(zip, lease.Token).ConfigureAwait(false) == bundle.Sha256)
        {
            Log($"{job.JobId}: the bundle of an earlier try is there");
            return null;
        }
        var url = new Uri(_server, bundle.Url);
        string partial = zip + ".part";
        string? last = null;
        for (int tries = 1; tries <= Math.Max(1, _options.DownloadTries); tries++)
        {
            if (lease.Stopped is not null) return null;
            try
            {
                TryDelete(zip);
                using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), lease.Token, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long length = 0;
                    await using (var source = await response.Content.ReadAsStreamAsync(lease.Token).ConfigureAwait(false))
                    await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, lease.Token).ConfigureAwait(false)) > 0)
                        {
                            length += read;
                            if (length > bundle.Size) break;
                            sha.AppendData(buffer, 0, read);
                            await target.WriteAsync(buffer.AsMemory(0, read), lease.Token).ConfigureAwait(false);
                        }
                    }
                    string hex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (length != bundle.Size || hex != bundle.Sha256)
                    {
                        TryDelete(partial);
                        return length != bundle.Size
                            ? $"{RenderNodeFailure.Checksum}:The bundle downloaded is {(length > bundle.Size ? "larger than" : length + " bytes, not")} the {bundle.Size} bytes the job says."
                            : $"{RenderNodeFailure.Checksum}:The bundle downloaded does not have the SHA-256 the job says.";
                    }
                    File.Move(partial, zip, overwrite: true);
                    Log($"{job.JobId}: bundle downloaded ({length} bytes, checked)");
                    return null;
                }
                last = $"the download was answered {(int)response.StatusCode}";
                if (!IsPassing(response.StatusCode)) break;
            }
            catch (Exception ex) when (IsPassing(ex, lease.Token))
            {
                last = Describe(ex);
            }
            catch (OperationCanceledException) when (lease.Stopped is not null)
            {
                TryDelete(partial);
                return null;
            }
            Log($"{job.JobId}: download failed: {last}");
            if (tries < _options.DownloadTries)
                await Wait(RenderNodeProtocol.Backoff(tries, _options.FirstBackoff, _options.MaxBackoff, Random.Shared.NextDouble()), lease.Token, quiet: true).ConfigureAwait(false);
        }
        TryDelete(partial);
        return $"The bundle could not be downloaded: {last}.";
    }

    // ---- the result ----

    /// <summary>The role of a result file the worker names; null for any other file.</summary>
    internal static string? RoleOf(string name)
    {
        if (name.EndsWith(RenderWorker.StatusSuffix, StringComparison.Ordinal)) return "status";
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".webm" => "movie",
            ".png" => "still",
            ".srt" or ".vtt" => "subtitles",
            RenderWorker.LogSuffix => "log",
            _ => null,
        };
    }

    /// <summary>Uploads the job's outputs, its log and its status, one PUT each with its SHA-256,
    /// and says the job is complete; null when the server took it, or why not.</summary>
    private async Task<string?> UploadAsync(Lease lease, RenderJobRecord record, string outbox)
    {
        string id = lease.Job.JobId;
        var result = record.Result!;
        var names = result.Outputs.Select(Path.GetFileName).OfType<string>().ToList();
        string log = Path.Combine(outbox, id + RenderWorker.LogSuffix);
        if (File.Exists(log)) names.Add(id + RenderWorker.LogSuffix);
        // The status as the server sees it: the files by their names, not this node's paths.
        string status = id + RenderWorker.StatusSuffix;
        var shown = record with { Result = result with { Outputs = result.Outputs.Select(o => Path.GetFileName(o)).ToList(), Log = File.Exists(log) ? Path.GetFileName(log) : null } };
        File.WriteAllText(Path.Combine(outbox, status), RenderJobFile.WriteOne(shown), new UTF8Encoding(false));
        names.Add(status);

        var files = new List<RenderNodeFile>();
        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            string path = Path.Combine(outbox, name);
            if (RoleOf(name) is not { } role || !RenderNodeProtocol.IsFileName(name) || new FileInfo(path).LinkTarget is not null) continue;
            long size = new FileInfo(path).Length;
            if (lease.Job.Limits?.MaxResultBytes is { } max && size > max)
                return $"{name} is {size} bytes, more than the {max} the server takes.";
            files.Add(new RenderNodeFile { Name = name, Role = role, Size = size, Sha256 = await Sha256Async(path, lease.Token).ConfigureAwait(false) });
        }
        foreach (var file in files)
        {
            string path = Path.Combine(outbox, file.Name);
            string? problem = await CallAsync(lease, () =>
            {
                var put = new HttpRequestMessage(HttpMethod.Put, new Uri(_server, RenderNodeProtocol.FilePath(id, file.Name, lease.Job.Attempt)))
                {
                    Content = new StreamContent(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)),
                };
                put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                put.Content.Headers.ContentLength = file.Size;
                put.Headers.TryAddWithoutValidation(RenderNodeProtocol.Sha256Header, file.Sha256);
                return put;
            }).ConfigureAwait(false);
            if (problem is not null) return $"{file.Name}: {problem}";
            Log($"{id}: uploaded {file.Name} ({file.Size} bytes)");
        }
        var complete = new RenderNodeComplete
        {
            Attempt = lease.Job.Attempt, Files = files, Device = result.Device, SecondsPerFrame = result.SecondsPerFrame,
            Seconds = result.Seconds, FrameCount = record.Progress?.FrameCount, Notices = result.Notices,
        };
        return await CallAsync(lease, () => Post(RenderNodeProtocol.CompletePath(id), RenderNodeProtocol.Write(complete))).ConfigureAwait(false);
    }

    /// <summary>Tells the server the job failed. Retried while the lease holds; <paramref name="lenient"/>
    /// for a job the server already ended (an answer of 409 is then as good as a yes).</summary>
    private async Task FailAsync(Lease lease, string reason, string message, bool lenient = false)
    {
        Log($"{lease.Job.JobId}: failed ({reason}): {message}");
        var fail = new RenderNodeFail { Attempt = Math.Max(1, lease.Job.Attempt), Reason = reason, Message = message, Retryable = RenderNodeFailure.IsRetryable(reason) };
        string? problem = await CallAsync(lease, () => Post(RenderNodeProtocol.FailPath(lease.Job.JobId), RenderNodeProtocol.Write(fail)), ignoreStop: true).ConfigureAwait(false);
        if (problem is not null && !lenient) Log($"{lease.Job.JobId}: the server did not take the failure: {problem}");
    }

    /// <summary>
    /// Makes a call that changes the job on the server, again after a wait that grows when the
    /// network or the server fails it, for as long as the job is the node's. Null when the server
    /// took it; why not otherwise. Calls are idempotent: the same call made twice does the same.
    /// </summary>
    private async Task<string?> CallAsync(Lease lease, Func<HttpRequestMessage> make, bool ignoreStop = false)
    {
        var token = ignoreStop ? lease.Ended : lease.Token;
        for (int failures = 1; ; failures++)
        {
            string problem;
            try
            {
                using var response = await SendAsync(make, ignoreStop ? CancellationToken.None : token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return null;
                problem = $"answered {(int)response.StatusCode}";
                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    if (!ignoreStop) lease.StopFor(Stop.LeaseLost);
                    return problem;
                }
                if (!IsPassing(response.StatusCode)) return problem;
            }
            catch (RenderNodeUnauthorizedException)
            {
                lease.StopFor(Stop.Unauthorized);
                return "the server refused the node token";
            }
            catch (Exception ex) when (IsPassing(ex, token))
            {
                problem = Describe(ex);
            }
            catch (OperationCanceledException)
            {
                return "stopped";
            }
            if (lease.Expired || (!ignoreStop && lease.Stopped is not null)) return problem;
            var wait = RenderNodeProtocol.Backoff(failures, _options.FirstBackoff, _options.MaxBackoff, Random.Shared.NextDouble());
            await Wait(wait, ignoreStop ? CancellationToken.None : token, quiet: true).ConfigureAwait(false);
        }
    }

    // ---- HTTP ----

    private HttpRequestMessage Post(string path, string json) => new(HttpMethod.Post, new Uri(_server, path))
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>Sends a request, with the node token when it goes to the server's own origin.</summary>
    /// <exception cref="RenderNodeUnauthorizedException">401 or 403.</exception>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = make();
        if (RenderNodeProtocol.IsSameOrigin(_server, request.RequestUri!))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        var response = await _http.SendAsync(request, completion, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new RenderNodeUnauthorizedException($"The server refused the node token ({(int)response.StatusCode}); the worker stops.");
        }
        return response;
    }

    /// <summary>A status that another try may get past: the server busy or failing, or a timeout.</summary>
    private static bool IsPassing(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    private static bool IsPassing(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => IsPassing(status),
        HttpRequestException or IOException => true,
        OperationCanceledException => !ct.IsCancellationRequested,  // a timeout of the call itself
        _ => false,
    };

    private static string Describe(Exception ex) => ex is OperationCanceledException ? "timed out" : Clean(ex.Message);

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async Task Wait(TimeSpan delay, CancellationToken ct, bool quiet = false)
    {
        if (delay <= TimeSpan.Zero) return;
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (quiet) { }
    }

    /// <summary>One line for the log, the token never in it.</summary>
    private void Log(string line) => _log?.Invoke(line.Replace(_options.Token, "***", StringComparison.Ordinal));

    /// <summary>Text from the server on one line, shortened.</summary>
    private static string Clean(string? text)
    {
        string one = new((text ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return one.Length > 200 ? one[..200] + "…" : one;
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

/// <summary>Reads a node token from the file its operator keeps it in.</summary>
public static class RenderNodeToken
{
    /// <summary>
    /// The token in <paramref name="path"/>: its first line, trimmed. Refused (with why) when the
    /// file is missing, is a link, others may read or write it (on Unix: no permission for
    /// "others"), or the token is empty or holds a space or a control character.
    /// </summary>
    public static bool TryRead(string path, out string token, out string? problem)
    {
        token = "";
        problem = null;
        var info = new FileInfo(path);
        if (!info.Exists) problem = "the token file is not there";
        else if (info.LinkTarget is not null) problem = "the token file is a link";
        else if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            problem = "others may read the token file; make it readable by its owner only (chmod 600)";
        else if (info.Length > 4096) problem = "the token file is larger than a token";
        else
        {
            string first = File.ReadLines(path).FirstOrDefault()?.Trim() ?? "";
            if (first.Length == 0) problem = "the token file is empty";
            else if (first.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) problem = "the token holds a space or a control character";
            else token = first;
        }
        return problem is null;
    }
}
