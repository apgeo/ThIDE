// Render jobs without Blender: the records and their file, the queue (one at a time, in order,
// waiting instead of refusing, cancel, a restart that goes on), and the checks a render worker
// makes of a bundle from elsewhere before any Blender starts. Bundles are of invented caves.

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

public sealed class RenderJobsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-jobs-{Guid.NewGuid():N}");

    public RenderJobsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- records ----

    [Fact]
    public void TheRecords_RoundTripThroughTheirFile_AndARestartQueuesTheRunningJobAgain()
    {
        var now = new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);
        RenderJobRecord[] jobs =
        [
            new() { Id = "b", Request = new RenderJobRequest { Name = "second", Spec = "{}" }, State = RenderJobState.Queued, QueuedUtc = now.AddMinutes(2) },
            new()
            {
                Id = "a", Request = new RenderJobRequest { Name = "first", Model = "/m.lox", Leads = [new SourceLead("s1", "continuation")] },
                State = RenderJobState.Running, QueuedUtc = now, StartedUtc = now,
                Progress = RenderJobProgress.From(new RenderProgress(RenderPhase.Rendering, "frame", 0.5, 3, 6, SecondsPerFrame: 1.25, SecondsLeft: 3.75, Segment: 1, SegmentCount: 2)),
            },
            new RenderJobRecord { Id = "c", QueuedUtc = now.AddMinutes(-5) }.Ended(RenderJobResult.Failure("refused", "no"), now),
        ];
        var read = RenderJobFile.Read(RenderJobFile.Write(jobs));
        Assert.Equal(jobs.Select(j => (j.Id, j.State, j.Request.Name)), read.Select(j => (j.Id, j.State, j.Request.Name)));
        Assert.Equal("rendering", read[1].Progress!.Phase);
        Assert.Equal((1, 2, 3, 6, 1.25, 3.8), (read[1].Progress!.Segment, read[1].Progress!.SegmentCount, read[1].Progress!.Frame,
            read[1].Progress!.FrameCount, read[1].Progress!.SecondsPerFrame, read[1].Progress!.SecondsLeft));
        Assert.Equal("s1", Assert.Single(read[1].Request.Leads).Station);
        Assert.Equal(RenderJobState.Failed, read[2].State);
        Assert.Contains("\"state\": \"running\"", RenderJobFile.Write(jobs));

        var restarted = RenderJobFile.Restarted(read);
        Assert.Equal(["c", "a", "b"], restarted.Select(j => j.Id));
        Assert.Equal(RenderJobState.Queued, restarted[1].State);
        Assert.Null(restarted[1].Progress);

        Assert.Empty(RenderJobFile.Read("not json"));
        Assert.Empty(RenderJobFile.Read("{\"version\": 99, \"jobs\": []}"));
        Assert.Equal("a", RenderJobFile.ReadOne(RenderJobFile.WriteOne(jobs[1]))!.Id);
    }

    [Fact]
    public void AJobEnds_AsItsResultSays()
    {
        var job = new RenderJobRecord { Id = "x" };
        var at = DateTimeOffset.UtcNow;
        Assert.Equal(RenderJobState.Succeeded, job.Ended(RenderJobResult.From(new RenderResult { Succeeded = true }), at).State);
        Assert.Equal(RenderJobState.Failed, job.Ended(RenderJobResult.From(new RenderResult { FailureKind = RenderFailureKind.Crashed }), at).State);
        Assert.Equal(RenderJobState.Cancelled, job.Ended(RenderJobResult.From(new RenderResult { FailureKind = RenderFailureKind.Cancelled }), at).State);
        Assert.Equal("crashed", job.Ended(RenderJobResult.From(new RenderResult { FailureKind = RenderFailureKind.Crashed }), at).Result!.FailureKind);
    }

    // ---- the queue ----

    private static RenderResult Done => new() { Succeeded = true };

    [Fact]
    public async Task TheQueue_RunsOneJobAtATime_InTheOrderTheyCame_AndTheOthersWait()
    {
        using var queue = new RenderQueue();
        var gates = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource<RenderResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var order = new List<int>();
        var heard = new List<RenderProgress>();
        Task<RenderResult> Run(int i, IProgress<RenderProgress>? progress = null) => queue.RunAsync(
            new RenderJobRequest { Name = $"job {i}" }, (_, _) => { lock (order) order.Add(i); return gates[i].Task; }, progress);

        var first = Run(0);
        var second = Run(1);
        var third = Run(2, new SyncProgress<RenderProgress>(heard.Add));
        Assert.Equal([0], order);
        Assert.Equal([RenderJobState.Running, RenderJobState.Queued, RenderJobState.Queued], queue.Jobs.Select(j => j.State));
        Assert.Equal(2, queue.Ahead(queue.Jobs[2].Id));  // the running one and the one before it
        Assert.Equal(RenderPhase.Queued, Assert.Single(heard).Phase);
        Assert.Contains("2 renders", heard[0].Message);

        gates[0].SetResult(Done);
        await first;
        await WaitFor(() => order.Count == 2);
        Assert.Equal([0, 1], order);
        Assert.Contains(heard, p => p.Phase == RenderPhase.Queued && p.Message.Contains("1 render"));
        gates[1].SetResult(Done);
        gates[2].SetResult(Done);
        await Task.WhenAll(second, third);
        Assert.Equal([0, 1, 2], order);
        Assert.All(queue.Jobs, j => Assert.Equal(RenderJobState.Succeeded, j.State));
        Assert.Null(queue.Running);
    }

    [Fact]
    public async Task AQueuedJobThatIsCancelled_LeavesTheQueue_AndARunningOneIsStopped()
    {
        using var queue = new RenderQueue();
        var gate = new TaskCompletionSource<RenderResult>();
        bool sawCancel = false;
        var running = queue.RunAsync(new RenderJobRequest { Name = "running" }, async (_, ct) =>
        {
            try { return await gate.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { sawCancel = true; return new RenderResult { FailureKind = RenderFailureKind.Cancelled }; }
        });
        bool ranWaiting = false;
        var waiting = queue.RunAsync(new RenderJobRequest { Name = "waiting" }, (_, _) => { ranWaiting = true; return Task.FromResult(Done); });

        Assert.True(queue.Cancel(queue.Jobs[1].Id));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(ranWaiting);
        Assert.Equal(RenderJobState.Cancelled, queue.Jobs[1].State);

        Assert.True(queue.Cancel(queue.Jobs[0].Id));
        var result = await running;
        Assert.True(sawCancel);
        Assert.Equal(RenderFailureKind.Cancelled, result.FailureKind);
        Assert.Equal(RenderJobState.Cancelled, queue.Jobs[0].State);
        Assert.False(queue.Cancel(queue.Jobs[0].Id));
    }

    [Fact]
    public async Task AfterARestart_TheJobsThatHadNotEnded_GoOnInTheirOrder()
    {
        string file = Path.Combine(_dir, "queue.json");
        var gate = new TaskCompletionSource<RenderResult>();
        var before = new RenderQueue(file);
        _ = before.RunAsync(new RenderJobRequest { Name = "cut short", Spec = "{\"a\":1}" }, (_, _) => gate.Task);
        _ = before.RunAsync(new RenderJobRequest { Name = "waiting" }, (_, _) => Task.FromResult(Done));
        _ = before.RunAsync(new RenderJobRequest { Name = "never" }, (_, _) => Task.FromResult(Done));
        string ids = string.Join(",", before.Jobs.Select(j => j.Id));
        // The process ends here without a word: nothing marks the jobs ended in the file.

        var after = new RenderQueue(file);
        Assert.Equal(["cut short", "waiting", "never"], after.Interrupted.Select(j => j.Request.Name));
        Assert.All(after.Interrupted, j => Assert.Equal(RenderJobState.Queued, j.State));
        Assert.Equal(ids, string.Join(",", after.Interrupted.Select(j => j.Id)));

        var replayed = new List<string>();
        var tasks = after.Resume(request => request.Name == "never"
            ? null
            : (_, _) => { lock (replayed) replayed.Add(request.Name); return Task.FromResult(Done); });
        await Task.WhenAll(tasks);
        Assert.Equal(["cut short", "waiting"], replayed);
        Assert.Empty(after.Interrupted);
        Assert.Equal(2, after.Jobs.Count(j => j.State == RenderJobState.Succeeded));
        // The file says so too: a third start has nothing to go on with.
        Assert.Empty(new RenderQueue(file).Interrupted);
        gate.SetResult(Done);
    }

    [Fact]
    public async Task TheQueuedService_WaitsItsTurn_AndGivesEachRenderAFramesFolderOfItsOwn()
    {
        var inner = new GatedService();
        using var queue = new RenderQueue();
        string root = Path.Combine(_dir, "queue");
        var service = new QueuedRenderService(inner, queue, root);
        var spec = SceneSpecTests.ValidSpec();
        var source = new RenderSource(new ResolvedModelSource { Path = "/caves/a.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });

        var first = service.RenderAsync(spec, source, new RenderOptions { Owner = "panel" });
        var heard = new List<RenderProgress>();
        var second = service.RenderAsync(spec, source, new RenderOptions { JobName = "agent's", Owner = "agent" }, new SyncProgress<RenderProgress>(heard.Add));
        Assert.Equal(1, inner.Started);
        Assert.Equal(RenderPhase.Queued, heard.Single().Phase);
        var options = inner.Options.Single();
        Assert.StartsWith(root, options.FramesDirectory);
        Assert.Equal(["panel", "agent"], queue.Jobs.Select(j => j.Request.Owner));
        Assert.Equal("agent's", queue.Jobs[1].Request.Name);
        Assert.Equal("/caves/a.lox", queue.Jobs[0].Request.Model);
        Assert.Equal(spec.Name, SceneSpecSerializer.Read(queue.Jobs[0].Request.Spec!).Name);

        Directory.CreateDirectory(options.FramesDirectory!);
        inner.Release();
        await first;
        Assert.False(Directory.Exists(Path.GetDirectoryName(options.FramesDirectory)), "the job's frames folder goes when it ends");
        await WaitFor(() => inner.Started == 2);
        inner.Release();
        await second;

        // Frames the caller names, or asks to keep, stay where the caller has them.
        var kept = service.RenderAsync(spec, source, new RenderOptions { KeepFrames = true });
        inner.Release();
        await kept;
        Assert.Null(inner.Options[^1].FramesDirectory);
    }

    private sealed class GatedService : IBlenderRenderService
    {
        private readonly SemaphoreSlim _release = new(0);
        public int Started;
        public List<RenderOptions> Options { get; } = [];

        public void Release() => _release.Release();

        public async Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options,
            IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            lock (Options) { Options.Add(options); Started++; }
            await _release.WaitAsync(ct);
            return new RenderResult { Succeeded = true };
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir,
            IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
            => throw new NotSupportedException();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    // ---- a bundle from elsewhere ----

    private string Bundle(string name = "job", Action<ZipArchive, string>? change = null, SceneSpec? spec = null)
    {
        string lox = Path.Combine(_dir, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        var source = new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var content = RenderBundleWriter.FromModel(source, spec ?? PlanFixtures.Preset("Orbit showcase") with
        {
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "clip" },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = 1 },
        }, Path.Combine(_dir, "work-" + name));
        string zip = Path.Combine(_dir, name + ".zip");
        RenderBundleWriter.WriteZip(content, zip);
        if (change is not null)
        {
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
            change(archive, name + "/");
        }
        return zip;
    }

    private static void EditPlan(ZipArchive archive, string top, Action<JsonObject> edit)
    {
        var entry = archive.GetEntry(top + ScenePlanSerializer.FileName)!;
        JsonObject plan;
        using (var reader = new StreamReader(entry.Open())) plan = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        entry.Delete();
        edit(plan);
        using var writer = new StreamWriter(archive.CreateEntry(top + ScenePlanSerializer.FileName).Open(), new UTF8Encoding(false));
        writer.Write(plan.ToJsonString());
    }

    private static void Add(ZipArchive archive, string name, string text, int? unixMode = null)
    {
        var entry = archive.CreateEntry(name);
        if (unixMode is { } mode) entry.ExternalAttributes = mode << 16;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private string Folder(string name) => Path.Combine(_dir, "unpacked-" + name);

    [Fact]
    public void ABundle_IsUnpackedAsItsPlansAndTheirFiles_AndNothingOfItsCode()
    {
        string zip = Bundle("good", (archive, top) => Add(archive, top + "extra.py", "import os  # never run"));
        var bundle = RenderBundleReader.Unpack(zip, Folder("good"));
        Assert.Null(bundle.Movie);
        var files = Directory.EnumerateFiles(bundle.Folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(bundle.Folder, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["model.ply", ScenePlanSerializer.FileName], files);
        Assert.True(bundle.Triangles > 0);
        Assert.Equal(8.0 * 96 * 64 * bundle.Scene.Look.Samples, bundle.Work, 3);

        // The worker plays it with its own player, written beside the plans.
        PlayerWriter.WritePlayer(bundle.Scenes, bundle.Folder);
        Assert.Equal(PlayerFiles.JobFiles.Single(f => f.Path == PlayerFiles.EntryFileName).Text,
            File.ReadAllText(Path.Combine(bundle.Folder, PlayerFiles.EntryFileName)));
    }

    [Fact]
    public void AFilmBundle_IsUnpackedWithThePlansOfItsSegments()
    {
        string lox = Path.Combine(_dir, "small.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        var source = new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var storyboard = new Storyboard
        {
            Fps = 8,
            Shots =
            [
                new ShotSpec { Preset = "Orbit showcase", Seconds = 1 },
                new ShotSpec { Preset = "Orbit showcase", Seconds = 1, In = new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 0.25 } },
            ],
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "film" },
        };
        var content = RenderBundleWriter.FromStoryboard(source, storyboard, Path.Combine(_dir, "work-film"));
        string zip = Path.Combine(_dir, "film.zip");
        RenderBundleWriter.WriteZip(content, zip);

        var bundle = RenderBundleReader.Unpack(zip, Folder("film"));
        Assert.NotNull(bundle.Movie);
        Assert.Equal(2, bundle.Segments.Count);
        Assert.Equal(bundle.Segments, bundle.Scenes);
        Assert.True(File.Exists(Path.Combine(bundle.Folder, bundle.Movie!.Segments[1].Plan)));
    }

    public static TheoryData<string, string> Refusals() => new()
    {
        { "zip-slip", "leaves the bundle's folder" },
        { "absolute", "not a plain path" },
        { "symlink", "is a link" },
        { "module-folders", "module folders" },
        { "unknown-feature", "'evil' is not one of the player's own" },
        { "outside-path", "models[0].path" },
        { "missing-file", "has no" },
        { "oversize", "more than the" },
        { "too-much-work", "frames × pixels × samples" },
        { "too-many-triangles", "triangles, more than" },
        { "saves-a-blend", "saves a Blender file" },
        { "not-a-zip", "not a zip file" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ABundleThatBreaksARule_IsRefused_BeforeAnyBlenderStarts(string rule, string message)
    {
        var limits = new RenderBundleLimits();
        string zip = rule switch
        {
            "zip-slip" => Bundle(rule, (a, top) => Add(a, top + "../../escaped.txt", "x")),
            "absolute" => Bundle(rule, (a, _) => Add(a, "/etc/escaped", "x")),
            "symlink" => Bundle(rule, (a, top) => Add(a, top + "model-link.ply", "/etc/passwd", unixMode: 0xA1FF)),
            "module-folders" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["moduleFolders"] = new JsonArray("modules"))),
            "unknown-feature" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["features"]!.AsArray().Insert(0, "evil"))),
            "outside-path" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["models"]![0]!["path"] = "../outside.ply")),
            "missing-file" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["models"]![0]!["path"] = "elsewhere.ply")),
            "saves-a-blend" => Bundle(rule, (a, top) => EditPlan(a, top, p =>
            {
                p["features"]!.AsArray().Insert(p["features"]!.AsArray().Count - 1, ScenePlan.BlendFileFeature);
                p["blend"] = new JsonObject { ["path"] = "scene.blend" };
            })),
            _ => Bundle(rule),
        };
        limits = rule switch
        {
            "oversize" => limits with { MaxZipBytes = 1000 },
            "too-much-work" => limits with { MaxWork = 1000 },
            "too-many-triangles" => limits with { MaxTriangles = 10 },
            _ => limits,
        };
        if (rule == "not-a-zip") File.WriteAllText(zip, "not a zip");

        var refused = Assert.Throws<RenderBundleRefusedException>(() => RenderBundleReader.Unpack(zip, Folder(rule), limits));
        Assert.Contains(message, refused.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Folder(rule), PlayerFiles.EntryFileName)));
    }

    [Fact]
    public void ALinkInPlaceOfTheBundle_IsRefused()
    {
        string zip = Bundle("real");
        string link = Path.Combine(_dir, "link.zip");
        File.CreateSymbolicLink(link, zip);
        Assert.Contains("is a link", Assert.Throws<RenderBundleRefusedException>(() => RenderBundleReader.Unpack(link, Folder("link"))).Message);
    }

    [Fact]
    public async Task TheWorker_RefusesABadBundle_SaysWhyInItsStatus_AndTakesTheNextOne()
    {
        string inbox = Path.Combine(_dir, "inbox"), outbox = Path.Combine(_dir, "outbox");
        Directory.CreateDirectory(inbox);
        File.Move(Bundle("bad", (a, top) => EditPlan(a, top, p => p["moduleFolders"] = new JsonArray("modules"))), Path.Combine(inbox, "bad.zip"));
        File.WriteAllText(Path.Combine(inbox, "stopped.zip"), "x");
        File.WriteAllText(Path.Combine(inbox, "stopped.cancel"), "");
        var worker = new RenderWorker(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
            new RenderWorkerOptions { Inbox = inbox, Outbox = outbox, Scratch = Path.Combine(_dir, "scratch") });

        Assert.Equal([Path.Combine(inbox, "bad.zip")], worker.Pending());
        Assert.Equal(1, await worker.RunAsync(once: true, CancellationToken.None));
        var status = RenderJobFile.ReadOne(File.ReadAllText(Path.Combine(outbox, "bad" + RenderWorker.StatusSuffix)))!;
        Assert.Equal(RenderJobState.Failed, status.State);
        Assert.Equal("refused", status.Result!.FailureKind);
        Assert.Contains("module folders", status.Result.Error);
        Assert.False(File.Exists(Path.Combine(inbox, "bad.zip")), "a refused bundle leaves the inbox");
        Assert.False(Directory.Exists(Path.Combine(_dir, "scratch", "bad")));
        Assert.Equal(["bad" + RenderWorker.StatusSuffix], Directory.GetFiles(outbox).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(inbox, "stopped.zip")), "a job with a cancel file waits");
    }
}
