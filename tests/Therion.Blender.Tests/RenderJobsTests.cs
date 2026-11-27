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
        // ThIDE closes here: the queue stops the jobs, and nothing marks them ended in the file.
        before.Dispose();

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
        Assert.All(after.Jobs.Where(j => j.State == RenderJobState.Succeeded), j => Assert.Equal(RenderQueue.RestartOwner, j.Request.Owner));
        // A job that cannot go on ends as failed, with why: it does not leave the history unseen.
        var never = Assert.Single(after.Jobs, j => j.Request.Name == "never");
        Assert.Equal(RenderJobState.Failed, never.State);
        Assert.Contains("could not go on", never.Result!.Error);
        // The file says so too: a third start has nothing to go on with.
        after.Dispose();
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

    // ---- a restart, a second ThIDE, a worker's inbox ----

    [Fact]
    public async Task ARenderARestartCutShort_GoesOnWithTheBuilderAndPartsItWasAskedFor()
    {
        string file = Path.Combine(_dir, "render-queue.json");
        string model = Path.Combine(_dir, "a.lox");
        File.WriteAllText(model, "invented");
        var spec = SceneSpecTests.ValidSpec();
        var source = new RenderSource(new ResolvedModelSource { Path = model, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
        var before = new RenderQueue(file);
        var gated = new GatedService();
        var first = new QueuedRenderService(gated, before, Path.Combine(_dir, "queue"));
        _ = first.RenderAsync(spec, source, new RenderOptions { Builder = SceneBuilder.Generator, Parts = WallPartsMode.On, Owner = "panel" });
        await WaitFor(() => gated.Started == 1);

        // ThIDE ends in the middle of the render; the next start finds it cut short.
        before.Dispose();
        var after = new RenderQueue(file);
        var replayed = new GatedService();
        var second = new QueuedRenderService(replayed, after, Path.Combine(_dir, "queue"));
        Assert.Equal(1, second.ResumeInterrupted());
        await WaitFor(() => replayed.Started == 1);
        var options = replayed.Options.Single();
        Assert.Equal((SceneBuilder.Generator, WallPartsMode.On), (options.Builder, options.Parts));
    }

    [Fact]
    public async Task AResumedRender_RemovesTheFramesFolderTheQueueGaveIt_WhenItEnds()
    {
        string file = Path.Combine(_dir, "render-queue.json");
        string model = Path.Combine(_dir, "a.lox");
        File.WriteAllText(model, "invented");
        var spec = SceneSpecTests.ValidSpec();
        var source = new RenderSource(new ResolvedModelSource { Path = model, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
        string root = Path.Combine(_dir, "queue");
        var gated = new GatedService();
        var before = new RenderQueue(file);
        _ = new QueuedRenderService(gated, before, root).RenderAsync(spec, source, new RenderOptions { Owner = "panel" });
        await WaitFor(() => gated.Started == 1);
        string frames = gated.Options.Single().FramesDirectory!;
        Directory.CreateDirectory(frames);
        File.WriteAllBytes(Path.Combine(frames, "frame_0001.png"), [1]);
        before.Dispose();
        await Task.Delay(100);
        Assert.True(File.Exists(Path.Combine(frames, "frame_0001.png")), "closing ThIDE keeps the frames, for the next start to go on with");

        var after = new RenderQueue(file);
        var replayed = new GatedService();
        var second = new QueuedRenderService(replayed, after, root);
        Assert.Equal(1, second.ResumeInterrupted());
        await WaitFor(() => replayed.Started == 1);
        Assert.Equal(frames, replayed.Options.Single().FramesDirectory);
        replayed.Release();
        await WaitFor(() => after.Running is null);
        await Task.Delay(100);
        Assert.False(Directory.Exists(Path.GetDirectoryName(frames)), "the frames folder of a resumed job that ended is left behind");
    }

    [Fact]
    public async Task ASecondThIDE_DoesNotStartTheRenderTheFirstIsRunning()
    {
        string file = Path.Combine(_dir, "render-queue.json");
        string model = Path.Combine(_dir, "a.lox");
        File.WriteAllText(model, "invented");
        var source = new RenderSource(new ResolvedModelSource { Path = model, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
        var running = new GatedService();
        using var firstQueue = new RenderQueue(file);
        _ = new QueuedRenderService(running, firstQueue, Path.Combine(_dir, "queue")).RenderAsync(SceneSpecTests.ValidSpec(), source, new RenderOptions { Owner = "panel" });
        await WaitFor(() => running.Started == 1);

        // A second ThIDE starts while the first renders: the same job file, the same folders.
        using var secondQueue = new RenderQueue(file);
        var second = new QueuedRenderService(new GatedService(), secondQueue, Path.Combine(_dir, "queue"));
        Assert.Equal(0, second.ResumeInterrupted());
        Assert.True(secondQueue.HeldElsewhere);
        Assert.False(firstQueue.HeldElsewhere);
        Assert.False(secondQueue.KeepsJobs);

        // Its renders are its own: they do not reach the first one's file.
        var other = new GatedService();
        var mine = new QueuedRenderService(other, secondQueue, Path.Combine(_dir, "queue")).RenderAsync(
            SceneSpecTests.ValidSpec(), source, new RenderOptions { JobName = "second's", Owner = "panel" });
        await WaitFor(() => other.Started == 1);
        Assert.DoesNotContain("second's", File.ReadAllText(file));
        other.Release();
        await mine;

        // When the first ThIDE closes, the next one to start has the file again.
        firstQueue.Dispose();
        using var third = new RenderQueue(file);
        Assert.False(third.HeldElsewhere);
        Assert.Single(third.Interrupted);
    }

    [Fact]
    public void TheOptionsOfARender_RoundTripWholeThroughItsRecord()
    {
        // Every option set to what is not its default: one added later and not kept fails here.
        var options = new RenderOptions
        {
            Builder = SceneBuilder.Generator, Draft = true, KeepFrames = true, ComposeOnly = true, FramesDirectory = "/frames/here",
            FrameRange = new FrameRange(3, 9), Parts = WallPartsMode.Off, JobName = "named", Owner = "agent",
        };
        var defaults = new RenderOptions();
        foreach (var property in typeof(RenderOptions).GetProperties())
            Assert.False(Equals(property.GetValue(options), property.GetValue(defaults)), $"the test sets no value of its own for {property.Name}");

        var job = new RenderJobRecord { Id = "j", Request = new RenderJobRequest { Name = "n", Options = options } };
        var read = RenderJobFile.Read(RenderJobFile.Write([job])).Single();
        Assert.Equal(options, read.Request.Options);
        // A record from before the options were kept reads with none.
        Assert.Null(RenderJobFile.ReadOne("{\"id\":\"old\",\"request\":{\"name\":\"n\",\"draft\":true}}")!.Request.Options);
    }

    [Fact]
    public async Task TheWorker_TakesAJobOnce_WhenItsZipCannotBeRemoved()
    {
        if (OperatingSystem.IsWindows()) return;
        string inbox = Path.Combine(_dir, "inbox"), outbox = Path.Combine(_dir, "outbox");
        Directory.CreateDirectory(inbox);
        File.Move(Bundle("bad", (a, top) => EditPlan(a, top, p => p["moduleFolders"] = new JsonArray("modules"))), Path.Combine(inbox, "bad.zip"));
        File.SetUnixFileMode(inbox, UnixFileMode.UserRead | UnixFileMode.UserExecute);   // a share the worker may read only
        try
        {
            var worker = new RenderWorker(
                new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
                new RenderWorkerOptions { Inbox = inbox, Outbox = outbox, Scratch = Path.Combine(_dir, "scratch") });
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            int taken = await worker.RunAsync(once: true, limit.Token);
            Assert.Equal(1, taken);
        }
        finally { File.SetUnixFileMode(inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public async Task TheWorker_LeavesAZipThatIsStillArriving_AndTakesItOnceItHasArrived()
    {
        string inbox = Path.Combine(_dir, "inbox"), outbox = Path.Combine(_dir, "outbox");
        Directory.CreateDirectory(inbox);
        byte[] whole = File.ReadAllBytes(Bundle("tour"));
        string zip = Path.Combine(inbox, "tour.zip");
        // An SFTP upload or a synced share half way: the first half of the zip is there.
        using (var arriving = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            arriving.Write(whole, 0, whole.Length / 2);
            arriving.Flush();
            var worker = new RenderWorker(
                new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
                new RenderWorkerOptions { Inbox = inbox, Outbox = outbox, Scratch = Path.Combine(_dir, "scratch") });
            Assert.Equal(0, await worker.RunAsync(once: true, CancellationToken.None));
            arriving.Write(whole, whole.Length / 2, whole.Length - whole.Length / 2);
        }
        string status = Path.Combine(outbox, "tour" + RenderWorker.StatusSuffix);
        string said = File.Exists(status) ? RenderJobFile.ReadOne(File.ReadAllText(status))!.Result?.Error ?? "" : "";
        Assert.True(File.Exists(zip), $"the zip that was arriving is gone; its status says: {said}");
        Assert.False(File.Exists(status), "a zip that is arriving is not a job yet");

        // A zip that stays unreadable once it is old enough is taken, and refused.
        File.WriteAllBytes(zip, whole[..(whole.Length / 2)]);
        var settled = new RenderWorker(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
            new RenderWorkerOptions { Inbox = inbox, Outbox = outbox, Scratch = Path.Combine(_dir, "scratch"), Arrival = TimeSpan.Zero });
        Assert.Equal(1, await settled.RunAsync(once: true, CancellationToken.None));
        Assert.Equal("refused", RenderJobFile.ReadOne(File.ReadAllText(status))!.Result!.FailureKind);
    }

    [Fact]
    public async Task TwoWorkersOnOneInbox_DoNotTakeTheSameJob()
    {
        string inbox = Path.Combine(_dir, "inbox"), outbox = Path.Combine(_dir, "outbox");
        Directory.CreateDirectory(inbox);
        File.Move(Bundle("bad", (a, top) => EditPlan(a, top, p => p["moduleFolders"] = new JsonArray("modules"))), Path.Combine(inbox, "bad.zip"));
        // Another worker has taken it: it holds the lock beside the zip.
        using (new FileStream(Path.Combine(inbox, "bad.zip" + RenderWorker.LockSuffix), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var worker = new RenderWorker(
                new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
                new RenderWorkerOptions { Inbox = inbox, Outbox = outbox, Scratch = Path.Combine(_dir, "scratch") });
            Assert.Equal(0, await worker.RunAsync(once: true, CancellationToken.None));
        }
        Assert.True(File.Exists(Path.Combine(inbox, "bad.zip")));
    }

    [Theory]
    [InlineData("look")]
    [InlineData("camera")]
    public async Task ABundleWhosePlanHasNullForASection_IsRefused_NotFailedAsWorthAnotherTry(string section)
    {
        string inbox = Path.Combine(_dir, "inbox"), outbox = Path.Combine(_dir, "outbox");
        Directory.CreateDirectory(inbox);
        File.Move(Bundle("nulled", (a, top) => EditPlan(a, top, p => p[section] = null)), Path.Combine(inbox, "nulled.zip"));
        var worker = new RenderWorker(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
            new RenderWorkerOptions { Inbox = inbox, Outbox = outbox, Scratch = Path.Combine(_dir, "scratch") });
        Assert.Equal(1, await worker.RunAsync(once: true, CancellationToken.None));
        var status = RenderJobFile.ReadOne(File.ReadAllText(Path.Combine(outbox, "nulled" + RenderWorker.StatusSuffix)))!;
        Assert.Equal("refused", status.Result!.FailureKind);
        Assert.False(RenderNodeFailure.IsRetryable(status.Result.FailureKind!));
    }

    [Fact]
    public void TheScratchOfAJobLeftUntouchedTooLong_IsRemoved_AndNothingElse()
    {
        string scratch = Path.Combine(_dir, "scratch");
        string old = Path.Combine(scratch, "old"), fresh = Path.Combine(scratch, "fresh"), other = Path.Combine(scratch, "notes");
        foreach (string job in new[] { old, fresh })
        {
            Directory.CreateDirectory(Path.Combine(job, PlanOutputs.FramesFolderName));
            File.WriteAllBytes(Path.Combine(job, PlanOutputs.FramesFolderName, "frame_0001.png"), new byte[1000]);
        }
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "mine.txt"), "not a job's");
        var longAgo = DateTime.UtcNow.AddDays(-30);
        foreach (string folder in new[] { old, other })
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Append(new DirectoryInfo(folder)))
                entry.LastWriteTimeUtc = longAgo;

        var removed = ScratchFolders.Prune(scratch, TimeSpan.FromDays(14), _ => true);
        Assert.Equal([("old", 1000L)], removed);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh), "a job written to lately keeps its frames");
        Assert.True(Directory.Exists(other), "a folder that is not a job's is left alone");
    }
}
