// The render node protocol and the worker that speaks it: the messages and their schema, the
// rules (addresses, token file, waits), and the worker against a fake server on 127.0.0.1 with a
// fake Blender: a whole job, a bundle that is not the one described, a cancel at a heartbeat and a
// lost lease (both stop Blender), a job taken again that goes on with its frames, a server that
// fails and comes back, a token refused, and bundles that break each rule of a worker.

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

public sealed class RenderNodeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-node-{Guid.NewGuid():N}");
    // Made only by the tests that talk to it: each server is one more chance for its port to be taken.
    private readonly Lazy<FakeRenderServer> _lazyServer = new(() => new FakeRenderServer());
    private FakeRenderServer _server => _lazyServer.Value;
    private readonly List<string> _log = [];

    public RenderNodeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (_lazyServer.IsValueCreated) _server.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- messages and rules ----

    private static readonly object[] Messages =
    [
        new RenderNodeClaim { Node = "gpu-1", Worker = "1.0.0", BlenderVersions = ["4.5.16"], Device = "GPU (CUDA)", FreeDiskBytes = 1L << 40, KeptJobs = ["42"], WaitSeconds = 30 },
        new RenderNodeJob
        {
            JobId = "42", Attempt = 2, LeaseSeconds = 120, HeartbeatSeconds = 30, Name = "Invented tour", Plan = "draft", Container = "webm",
            Bundle = new RenderNodeBundle { Url = "/api/v1/flybys/7/bundle", Size = 1234, Sha256 = new string('a', 64) },
            Limits = new RenderNodeLimits { MaxHours = 4, MaxUnpackedBytes = 1L << 30, MaxTriangles = 1_000_000, MaxWork = 1e12, MaxResultBytes = 1L << 32 },
        },
        new RenderNodeHeartbeat { Attempt = 2, Phase = "rendering", Message = "frame 3", Segment = 1, SegmentCount = 2, Frame = 3, FrameCount = 80, SecondsPerFrame = 1.5, SecondsLeft = 115.5 },
        new RenderNodeHeartbeatReply { Action = "cancel", LeaseSeconds = 60 },
        new RenderNodeComplete
        {
            Attempt = 2, Device = "CPU", SecondsPerFrame = 0.4, Seconds = 33.2, FrameCount = 80, Notices = ["an untested Blender"],
            Files = [new RenderNodeFile { Name = "42.webm", Role = "movie", Size = 9000, Sha256 = new string('0', 64) }],
        },
        new RenderNodeFail { Attempt = 2, Reason = "refused", Message = "module folders", Retryable = false },
    ];

    private static string Def(object message) => message switch
    {
        RenderNodeClaim => "claim", RenderNodeJob => "job", RenderNodeHeartbeat => "heartbeat",
        RenderNodeHeartbeatReply => "heartbeatReply", RenderNodeComplete => "complete", _ => "fail",
    };

    internal static string SchemaOf(string def)
    {
        var schema = JsonNode.Parse(RenderNodeProtocol.Schema)!.AsObject();
        schema["$ref"] = "#/$defs/" + def;
        return schema.ToJsonString();
    }

    [Fact]
    public void EachMessage_GoesToJsonAndBack_AndHoldsToTheSchema()
    {
        foreach (object message in Messages)
        {
            string json = message switch
            {
                RenderNodeClaim m => RenderNodeProtocol.Write(m), RenderNodeJob m => RenderNodeProtocol.Write(m),
                RenderNodeHeartbeat m => RenderNodeProtocol.Write(m), RenderNodeHeartbeatReply m => RenderNodeProtocol.Write(m),
                RenderNodeComplete m => RenderNodeProtocol.Write(m), RenderNodeFail m => RenderNodeProtocol.Write(m), _ => "",
            };
            string again = message switch
            {
                RenderNodeClaim => RenderNodeProtocol.Write(RenderNodeProtocol.Read<RenderNodeClaim>(json)),
                RenderNodeJob => RenderNodeProtocol.Write(RenderNodeProtocol.Read<RenderNodeJob>(json)),
                RenderNodeHeartbeat => RenderNodeProtocol.Write(RenderNodeProtocol.Read<RenderNodeHeartbeat>(json)),
                RenderNodeHeartbeatReply => RenderNodeProtocol.Write(RenderNodeProtocol.Read<RenderNodeHeartbeatReply>(json)),
                RenderNodeComplete => RenderNodeProtocol.Write(RenderNodeProtocol.Read<RenderNodeComplete>(json)),
                _ => RenderNodeProtocol.Write(RenderNodeProtocol.Read<RenderNodeFail>(json)),
            };
            Assert.Equal(json, again);
            Assert.Empty(JsonSchemaSubset.Validate(SchemaOf(Def(message)), json));
        }
        // The smallest messages, as a worker or a server writes them, hold to it too.
        Assert.Empty(JsonSchemaSubset.Validate(SchemaOf("claim"), RenderNodeProtocol.Write(new RenderNodeClaim { Node = "n" })));
        Assert.Empty(JsonSchemaSubset.Validate(SchemaOf("job"), """{"protocol":1,"jobId":"7","attempt":1,"leaseSeconds":120,"plan":"full","bundle":{"url":"b","size":1,"sha256":"%"}}""".Replace("%", new string('f', 64))));
        Assert.NotEmpty(JsonSchemaSubset.Validate(SchemaOf("job"), """{"protocol":1,"jobId":"../x","attempt":1,"leaseSeconds":120,"plan":"full","bundle":{"url":"b","size":1,"sha256":"x"}}"""));
        Assert.Null(RenderNodeProtocol.Read<RenderNodeJob>("not json"));
    }

    [Fact]
    public void TheSchemaAndTheProtocol_NameTheSameContainersAndReasons_AsThePlayerWrites()
    {
        var defs = JsonNode.Parse(RenderNodeProtocol.Schema)!["$defs"]!;
        Assert.Equal(RenderNodeProtocol.Containers, defs["container"]!["enum"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(RenderNodeFailure.All, defs["fail"]!["properties"]!["reason"]!["enum"]!.AsArray().Select(n => (string)n!));
        string output = PlayerFiles.JobFiles.Single(f => f.Path.EndsWith("modules/output.py", StringComparison.Ordinal)).Text;
        var player = Regex.Matches(Regex.Match(output, @"CONTAINERS = \{(.*)\}").Groups[1].Value, "\"(\\w+)\": \\(").Select(m => m.Groups[1].Value);
        Assert.Equal(player.Order(), RenderNodeProtocol.Containers.Order());
        Assert.True(RenderNodeProtocol.TryContainer(null, out var fallback) && fallback == PlanContainer.Mp4);
        Assert.True(RenderNodeProtocol.TryContainer("webm", out var webm) && webm == PlanContainer.Webm);
        Assert.False(RenderNodeProtocol.TryContainer("avi", out _));
        Assert.False(RenderNodeProtocol.TryContainer("WEBM", out _));
    }

    [Fact]
    public void TheFailureConstants_AreTheSchemasEnum_InItsOrder()
    {
        string[] constants =
        [
            RenderNodeFailure.Refused, RenderNodeFailure.BadJob, RenderNodeFailure.Checksum, RenderNodeFailure.Download,
            RenderNodeFailure.Upload, RenderNodeFailure.Cancelled, RenderNodeFailure.TimedOut, RenderNodeFailure.BlenderNotFound,
            RenderNodeFailure.BlenderTooOld, RenderNodeFailure.DiskSpace, RenderNodeFailure.ScriptError, RenderNodeFailure.Crashed,
            RenderNodeFailure.NoOutput, RenderNodeFailure.Error,
        ];
        var defs = JsonNode.Parse(RenderNodeProtocol.Schema)!["$defs"]!;
        Assert.Equal(defs["fail"]!["properties"]!["reason"]!["enum"]!.AsArray().Select(n => (string)n!), constants);
        Assert.Equal(constants, RenderNodeFailure.All);
    }

    [Fact]
    public void Validate_AcceptsAGoodMessage_AndEveryMessageTheLibraryWrites()
    {
        foreach (object message in Messages)
        {
            string json = message switch
            {
                RenderNodeClaim m => RenderNodeProtocol.Write(m), RenderNodeJob m => RenderNodeProtocol.Write(m),
                RenderNodeHeartbeat m => RenderNodeProtocol.Write(m), RenderNodeHeartbeatReply m => RenderNodeProtocol.Write(m),
                RenderNodeComplete m => RenderNodeProtocol.Write(m), RenderNodeFail m => RenderNodeProtocol.Write(m), _ => "",
            };
            Assert.Empty(RenderNodeProtocol.Validate(Def(message), json));
        }
        Assert.Empty(RenderNodeProtocol.Validate("fail", """{"attempt":1,"reason":"diskSpace","message":"full","retryable":true}"""));
    }

    [Fact]
    public void Validate_ReportsAMissingRequiredProperty()
    {
        var problems = RenderNodeProtocol.Validate("fail", """{"attempt":1,"reason":"error","retryable":true}""");
        Assert.Contains(problems, p => p.Contains("'message' is missing"));
        Assert.NotEmpty(RenderNodeProtocol.Validate("fail", """{"attempt":1,"reason":"nonsense","message":"x","retryable":true}"""));
        Assert.NotEmpty(RenderNodeProtocol.Validate("fail", "not json"));
    }

    [Fact]
    public void Validate_AcceptsUnknownProperties_WhereTheSchemaIsClosed()
    {
        const string json = """{"attempt":1,"reason":"error","message":"x","retryable":true,"addedLater":{"a":1}}""";
        Assert.Empty(RenderNodeProtocol.Validate("fail", json));
        // The schema itself stays closed: it describes what a sender writes.
        Assert.NotEmpty(JsonSchemaSubset.Validate(SchemaOf("fail"), json));
        const string job = """{"protocol":1,"jobId":"7","attempt":1,"leaseSeconds":120,"plan":"full","extra":true,"bundle":{"url":"b","size":1,"sha256":"%","more":1}}""";
        Assert.Empty(RenderNodeProtocol.Validate("job", job.Replace("%", new string('f', 64))));
    }

    [Fact]
    public void Validate_RefusesAnotherProtocolVersion_AndAnUnknownKind()
    {
        var problems = RenderNodeProtocol.Validate("claim", """{"protocol":2,"node":"n","blenderVersions":[],"containers":["mp4"],"keptJobs":[],"waitSeconds":0}""");
        Assert.Contains(problems, p => p.Contains("$.protocol") && p.Contains("must be 1"));
        Assert.Throws<ArgumentException>(() => RenderNodeProtocol.Validate("bundle", "{}"));
    }

    [Theory]
    [InlineData("https://render.example.org/api/v1/render-nodes", true)]
    [InlineData("http://127.0.0.1:8080/nodes", true)]
    [InlineData("http://localhost/nodes", true)]
    [InlineData("http://[::1]:9000/", true)]
    [InlineData("http://render.example.org/nodes", false)]
    [InlineData("http://192.168.1.10/nodes", false)]
    [InlineData("ftp://render.example.org/", false)]
    [InlineData("https://user:secret@render.example.org/", false)]
    public void ThePlainHttpRule_AllowsThisMachineOnly(string url, bool allowed)
    {
        Assert.Equal(allowed, RenderNodeProtocol.IsAllowedAddress(new Uri(url), out string? problem));
        Assert.Equal(allowed, problem is null);
    }

    [Fact]
    public void TheTokenGoesToTheServersOwnOriginOnly()
    {
        var server = new Uri("https://render.example.org/api/");
        Assert.True(RenderNodeProtocol.IsSameOrigin(server, new Uri("https://render.example.org/files/b.zip")));
        Assert.False(RenderNodeProtocol.IsSameOrigin(server, new Uri("https://storage.example.org/b.zip")));
        Assert.False(RenderNodeProtocol.IsSameOrigin(server, new Uri("http://render.example.org/b.zip")));
        Assert.False(RenderNodeProtocol.IsSameOrigin(server, new Uri("https://render.example.org:8443/b.zip")));
    }

    [Fact]
    public void TheWaitAfterFailures_Doubles_StopsAtItsCeiling_AndIsSpread()
    {
        var first = TimeSpan.FromSeconds(2);
        var max = TimeSpan.FromMinutes(5);
        Assert.Equal(TimeSpan.Zero, RenderNodeProtocol.Backoff(0, first, max, 1));
        Assert.Equal([2.0, 4, 8, 16, 32, 64, 128, 256, 300, 300],
            Enumerable.Range(1, 10).Select(n => RenderNodeProtocol.Backoff(n, first, max, 1).TotalSeconds));
        Assert.Equal(1.0, RenderNodeProtocol.Backoff(1, first, max, 0).TotalSeconds);
        Assert.Equal(150.0, RenderNodeProtocol.Backoff(1000, first, max, 0).TotalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(40), RenderNodeProtocol.HeartbeatInterval(120, null));
        Assert.Equal(TimeSpan.FromSeconds(30), RenderNodeProtocol.HeartbeatInterval(120, 30));
        Assert.Equal(TimeSpan.FromSeconds(40), RenderNodeProtocol.HeartbeatInterval(120, 100));
        Assert.Equal(TimeSpan.FromSeconds(1), RenderNodeProtocol.HeartbeatInterval(2, null));
    }

    [Fact]
    public void ATokenFile_IsReadByItsOwnerOnly_AndHoldsOneToken()
    {
        string file = Path.Combine(_dir, "token");
        Assert.False(RenderNodeToken.TryRead(file, out _, out string? missing));
        Assert.Contains("not there", missing);
        File.WriteAllText(file, "  s3cret-token \n# a comment line\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            Assert.False(RenderNodeToken.TryRead(file, out _, out string? open));
            Assert.Contains("chmod 600", open);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            string link = Path.Combine(_dir, "token-link");
            File.CreateSymbolicLink(link, file);
            Assert.False(RenderNodeToken.TryRead(link, out _, out string? linked));
            Assert.Contains("link", linked);
        }
        Assert.True(RenderNodeToken.TryRead(file, out string token, out _));
        Assert.Equal("s3cret-token", token);
        File.WriteAllText(file, "two words");
        Assert.False(RenderNodeToken.TryRead(file, out _, out string? spaced));
        Assert.Contains("space", spaced);
        File.WriteAllText(file, "\n");
        Assert.False(RenderNodeToken.TryRead(file, out _, out _));
    }

    // ---- a fake Blender ----

    /// <summary>Plays a plan as far as the runner can tell: says each frame, writes a frame file for
    /// each into the frames folder, and the output the plan names. Slow ones take 100 ms a frame.</summary>
    private sealed class FakeBlender(int frames = 8, int delayMs = 0) : IBlenderProcessLauncher
    {
        public int Started;
        public int Killed;
        public string? Container;
        public int FramesThereAtStart = -1;

        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            Interlocked.Increment(ref Started);
            string plan = args[args.ToList().IndexOf("--plan") + 1];
            string framesDir = args[args.ToList().IndexOf("--frames") + 1];
            Directory.CreateDirectory(framesDir);
            FramesThereAtStart = Directory.GetFiles(framesDir).Length;
            var output = JsonNode.Parse(File.ReadAllText(plan))!["output"]!;
            Container = (string?)output["container"] ?? "mp4";
            string target = Path.Combine(cwd, (string?)output["directory"] ?? ".", (string)output["baseName"]! + "." + Container);
            return new Process(this, frames, delayMs, framesDir, target);
        }

        private sealed class Process(FakeBlender owner, int frames, int delayMs, string framesDir, string target) : IBlenderProcess
        {
            public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                yield return "THIDE:device=CPU";
                yield return $"THIDE:frames={frames}";
                for (int i = 1; i <= frames; i++)
                {
                    if (delayMs > 0) await Task.Delay(delayMs, ct);
                    ct.ThrowIfCancellationRequested();
                    File.WriteAllBytes(Path.Combine(framesDir, $"frame_{i:0000}.png"), [1]);
                    yield return $"THIDE:frame={i}/{frames}";
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, new byte[256]);
                yield return "THIDE:done=1";
            }

            public int ExitCode => 0;

            public void KillTree() => Interlocked.Increment(ref owner.Killed);

            public void Dispose() { }
        }
    }

    private sealed class FakeProbe : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => new BlenderVersion(4, 5, 16);
    }

    private RenderNodeWorker Worker(FakeBlender blender, string? token = null, RenderBundleLimits? limits = null) => new(
        new BlenderLocator(new FakeProbe()), new BlenderRunner(blender), new RenderNodeOptions
        {
            Server = _server.Url, Token = token ?? _server.Token, NodeName = "test-node", Scratch = Path.Combine(_dir, "scratch"),
            Poll = TimeSpan.FromMilliseconds(100), FirstBackoff = TimeSpan.FromMilliseconds(50), MaxBackoff = TimeSpan.FromMilliseconds(200),
            DownloadTries = 3, Limits = limits ?? new RenderBundleLimits(),
        },
        () => typeof(RenderNodeTests).Assembly.Location, line => { lock (_log) _log.Add(line); });

    private string Bundle(string name = "job", Action<ZipArchive, string>? change = null, bool draft = false)
    {
        string lox = Path.Combine(_dir, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        var source = new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var content = RenderBundleWriter.FromModel(source, PlanFixtures.Preset("Orbit showcase") with
        {
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "clip" },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = 1 },
        }, Path.Combine(_dir, "work-" + name), draft: draft);
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

    private string[] Uploaded(string job) => _server.Files.Keys.Where(k => k.StartsWith(job + "/", StringComparison.Ordinal))
        .Select(k => k[(job.Length + 1)..]).Order(StringComparer.Ordinal).ToArray();

    private void AssertNoTokenInTheLog()
    {
        lock (_log) Assert.DoesNotContain(_log, line => line.Contains(_server.Token, StringComparison.Ordinal));
    }

    // ---- a whole job ----

    [Theory]
    [InlineData(null, "mp4")]
    [InlineData("webm", "webm")]
    public async Task AJob_IsClaimed_Downloaded_Rendered_Uploaded_AndCompleted(string? container, string extension)
    {
        _server.Offer("101", Bundle(), container: container, lease: 3);
        var blender = new FakeBlender(frames: 30, delayMs: 100);
        using var worker = Worker(blender);
        var run = await worker.RunAsync(once: true, CancellationToken.None);

        Assert.Equal(new RenderNodeRun(1, false), run);
        Assert.Equal(extension, blender.Container);
        Assert.Equal(new[] { "101.log", "101." + extension, "101.status.json" }.Order(StringComparer.Ordinal), Uploaded("101"));
        var (job, complete) = Assert.Single(_server.Completed);
        Assert.Equal("101", job);
        Assert.Equal(1, complete.Attempt);
        Assert.Equal(["log", "movie", "status"], complete.Files.Select(f => f.Role).Order(StringComparer.Ordinal));
        Assert.Empty(_server.Failed);
        // Progress went with the heartbeats: frames of the whole job.
        Assert.Contains(_server.Beats, b => b is { Phase: "rendering", Frame: > 0, FrameCount: 30 });
        var status = RenderJobFile.ReadOne(Encoding.UTF8.GetString(_server.Files["101/101.status.json"]))!;
        Assert.Equal(RenderJobState.Succeeded, status.State);
        Assert.Equal(["101." + extension], status.Result!.Outputs);  // names, not the node's paths
        // The token went with every call to the server, and into no log line.
        Assert.All(_server.Requests.Where(r => r.Request.StartsWith("POST /api/", StringComparison.Ordinal)), r => Assert.True(r.Token));
        AssertNoTokenInTheLog();
        Assert.False(Directory.Exists(worker.JobFolder("101")), "a job that succeeded leaves nothing behind");
        var claim = Assert.Single(_server.Claims);
        Assert.Equal(("test-node", "4.5.16"), (claim.Node, claim.BlenderVersions.Single()));
    }

    [Fact]
    public async Task ABundleOnAnotherOrigin_IsDownloadedWithoutTheToken()
    {
        _server.Offer("102", Bundle(), host: "localhost");
        using var worker = Worker(new FakeBlender());
        await worker.RunAsync(once: true, CancellationToken.None);
        Assert.Single(_server.Completed);
        Assert.Contains(_server.Requests, r => r is { Request: "GET /bundles/102.zip", Token: false });
    }

    [Fact]
    public async Task ABundleThatIsNotTheOneDescribed_FailsTheJob_BeforeBlenderStarts()
    {
        _server.Offer("103", Bundle(), sha256: new string('0', 64));
        var blender = new FakeBlender();
        using var worker = Worker(blender);
        await worker.RunAsync(once: true, CancellationToken.None);
        var (_, fail) = Assert.Single(_server.Failed);
        Assert.Equal((RenderNodeFailure.Checksum, true), (fail.Reason, fail.Retryable));
        Assert.Equal(0, blender.Started);
        Assert.Empty(_server.Files);
        Assert.False(File.Exists(Path.Combine(worker.JobFolder("103"), "bundle.zip")));
    }

    // ---- stopping ----

    [Fact]
    public async Task ACancelAtAHeartbeat_KillsBlender_SaysSo_AndUploadsNothing()
    {
        _server.Offer("104", Bundle(), lease: 3);
        _server.Heartbeat = n => (200, n >= 2 ? RenderNodeProtocol.Cancel : RenderNodeProtocol.Continue);
        var blender = new FakeBlender(frames: 600, delayMs: 100);
        using var worker = Worker(blender);
        await worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(blender.Killed > 0, "Blender was not stopped");
        var (_, fail) = Assert.Single(_server.Failed);
        Assert.Equal(RenderNodeFailure.Cancelled, fail.Reason);
        Assert.Empty(_server.Files);
        Assert.Empty(_server.Completed);
        Assert.False(Directory.Exists(worker.JobFolder("104")));
    }

    [Theory]
    [InlineData(409)]
    [InlineData(503)]
    public async Task ALostLease_KillsBlender_KeepsTheFrames_AndAJobTakenAgainGoesOnWithThem(int heartbeat)
    {
        string zip = Bundle();
        _server.Offer("105", zip, lease: 3);
        // 409: the server gave the job to another; 503 for every heartbeat: the lease runs out.
        _server.Heartbeat = _ => (heartbeat, RenderNodeProtocol.Continue);
        var blender = new FakeBlender(frames: 600, delayMs: 100);
        using var worker = Worker(blender);
        await worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(blender.Killed > 0, "Blender was not stopped");
        Assert.Empty(_server.Failed);
        Assert.Empty(_server.Completed);
        Assert.Empty(_server.Files);
        int kept = Directory.GetFiles(Path.Combine(worker.JobFolder("105"), PlanOutputs.FramesFolderName)).Length;
        Assert.True(kept > 0, "the frames were not kept");

        // The same job comes back to this node: its claim says it holds frames of it, and the
        // render starts with them there (the player goes on from them).
        _server.Heartbeat = _ => (200, RenderNodeProtocol.Continue);
        _server.Offer("105", zip, lease: 3, attempt: 2);
        var again = new FakeBlender(frames: 8);
        using var second = Worker(again);
        await second.RunAsync(once: true, CancellationToken.None);
        Assert.Contains("105", _server.Claims.Last().KeptJobs);
        Assert.True(again.FramesThereAtStart >= kept, $"{again.FramesThereAtStart} frames there at the start, {kept} kept");
        var (_, complete) = Assert.Single(_server.Completed);
        Assert.Equal(2, complete.Attempt);
        Assert.Contains(_log, l => l.Contains("the bundle of an earlier try is there"));
    }

    [Fact]
    public async Task AServerThatFails_IsAskedAgain_AndTheJobGoesThrough()
    {
        int claims = 0, puts = 0, bundles = 0;
        _server.Fault = request => request switch
        {
            "POST /api/nodes/claim" when Interlocked.Increment(ref claims) <= 2 => 503,
            "GET /bundles/106.zip" when Interlocked.Increment(ref bundles) <= 1 => 502,
            _ when request.StartsWith("PUT ", StringComparison.Ordinal) && Interlocked.Increment(ref puts) <= 1 => 500,
            _ => null,
        };
        _server.Offer("106", Bundle());
        using var worker = Worker(new FakeBlender());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var running = worker.RunAsync(once: false, stop.Token);
        for (int i = 0; i < 300 && _server.Completed.IsEmpty; i++) await Task.Delay(50);
        stop.Cancel();
        var run = await running;
        Assert.Equal(1, run.Jobs);
        Assert.Single(_server.Completed);
        Assert.Equal(3, Uploaded("106").Length);
        Assert.True(claims >= 3);
    }

    [Fact]
    public async Task ATokenTheServerRefuses_StopsTheWorker()
    {
        _server.Offer("107", Bundle());
        using var worker = Worker(new FakeBlender(), token: "a-token-the-server-does-not-know");
        var run = await worker.RunAsync(once: false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(new RenderNodeRun(0, true), run);
        Assert.Single(_server.Requests);
        lock (_log) Assert.DoesNotContain(_log, l => l.Contains("a-token-the-server-does-not-know", StringComparison.Ordinal));
    }

    // ---- bundles that break the rules ----

    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "zip-slip", "refused", "leaves the bundle's folder" },
        { "symlink", "refused", "is a link" },
        { "module-folders", "refused", "module folders" },
        { "unknown-feature", "refused", "'evil' is not one of the player's own" },
        { "outside-path", "refused", "models[0].path" },
        { "saves-a-blend", "refused", "saves a Blender file" },
        { "too-many-triangles", "refused", "triangles, more than" },
        { "not-a-zip", "refused", "not a zip file" },
        { "no-draft", "refused", "has no scene-plan.draft.json" },
        { "unknown-container", "badJob", "'avi'" },
        { "unknown-plan", "badJob", "not full or draft" },
        { "too-large", "badJob", "more than the" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ABundleOrJobThatBreaksARule_FailsTheJob_WithWhy_AndUploadsNothing(string rule, string reason, string message)
    {
        string zip = rule switch
        {
            "zip-slip" => Bundle(rule, (a, top) => Add(a, top + "../../escaped.txt", "x")),
            "symlink" => Bundle(rule, (a, top) => Add(a, top + "model-link.ply", "/etc/passwd", unixMode: 0xA1FF)),
            "module-folders" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["moduleFolders"] = new JsonArray("modules"))),
            "unknown-feature" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["features"]!.AsArray().Insert(0, "evil"))),
            "outside-path" => Bundle(rule, (a, top) => EditPlan(a, top, p => p["models"]![0]!["path"] = "../outside.ply")),
            "saves-a-blend" => Bundle(rule, (a, top) => EditPlan(a, top, p =>
            {
                p["features"]!.AsArray().Insert(p["features"]!.AsArray().Count - 1, ScenePlan.BlendFileFeature);
                p["blend"] = new JsonObject { ["path"] = "scene.blend" };
            })),
            _ => Bundle(rule),
        };
        if (rule == "not-a-zip") File.WriteAllText(zip, "not a zip");
        var job = _server.Offer("r-" + rule, zip,
            plan: rule switch { "no-draft" => "draft", "unknown-plan" => "best", _ => "full" },
            container: rule == "unknown-container" ? "avi" : null,
            limits: rule == "too-many-triangles" ? new RenderNodeLimits { MaxTriangles = 10 } : null);
        var blender = new FakeBlender();
        using var worker = Worker(blender, limits: rule == "too-large" ? new RenderBundleLimits { MaxZipBytes = 100 } : null);
        await worker.RunAsync(once: true, CancellationToken.None);

        var (failed, fail) = Assert.Single(_server.Failed);
        Assert.Equal(job.JobId, failed);
        Assert.Equal(reason, fail.Reason);
        Assert.Contains(message, fail.Message);
        Assert.False(fail.Retryable);
        Assert.Equal(0, blender.Started);
        Assert.Empty(_server.Files);
        Assert.Empty(_server.Completed);
        Assert.DoesNotContain(_server.Requests, r => r.Request.StartsWith("PUT ", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
        Assert.False(Directory.Exists(worker.JobFolder(job.JobId)) && Directory.EnumerateFiles(worker.JobFolder(job.JobId), "*.py", SearchOption.AllDirectories).Any(),
            "the job's folder holds code");
    }

    [Fact]
    public async Task ADraftJob_PlaysTheDraftPlan()
    {
        _server.Offer("108", Bundle(draft: true), plan: "draft", container: "webm");
        var blender = new FakeBlender();
        using var worker = Worker(blender);
        await worker.RunAsync(once: true, CancellationToken.None);
        var (_, complete) = Assert.Single(_server.Completed);
        Assert.Contains(complete.Files, f => f.Name == "108.webm");
        Assert.Equal("webm", blender.Container);
    }

    // ---- a slow complete, huge limits, a bad claim, a stalled download ----

    /// <summary>Holds the answer to `complete` (a server that checks big files, a slow link), and
    /// can put a body of its own in place of the claim's answer.</summary>
    private sealed class Tweak : DelegatingHandler
    {
        public TimeSpan HoldComplete = TimeSpan.Zero;
        public bool StallBundle;
        public string? ClaimBody;
        public Tweak() : base(new SocketsHttpHandler { AllowAutoRedirect = false }) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (ClaimBody is { } body && request.RequestUri!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal))
            {
                ClaimBody = null;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
            var response = await base.SendAsync(request, ct);
            if (StallBundle && request.RequestUri!.AbsolutePath.StartsWith("/bundles/", StringComparison.Ordinal))
                response.Content = new StreamContent(new StallingStream());
            if (request.RequestUri!.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal)) await Task.Delay(HoldComplete, ct);
            return response;
        }
    }

    private RenderNodeWorker Worker(FakeBlender blender, Tweak tweak, TimeSpan? maxTime = null) => new(
        new BlenderLocator(new FakeProbe()), new BlenderRunner(blender), new RenderNodeOptions
        {
            Server = _server.Url, Token = _server.Token, NodeName = "test-node", Scratch = Path.Combine(_dir, "scratch"),
            Poll = TimeSpan.FromMilliseconds(100), FirstBackoff = TimeSpan.FromMilliseconds(50), MaxBackoff = TimeSpan.FromMilliseconds(200),
            DownloadTries = 3, MaxTime = maxTime ?? TimeSpan.FromHours(72), StallTimeout = TimeSpan.FromSeconds(1),
        },
        () => typeof(RenderNodeTests).Assembly.Location, line => { lock (_log) _log.Add(line); }, tweak);

    [Fact]
    public async Task AHeartbeatThatMeetsTheCompletedJob_DoesNotTurnItsSuccessIntoALostLease()
    {
        _server.Offer("201", Bundle(), lease: 3);
        // A server answers 409 to a heartbeat of a job that is no longer running: it has completed.
        _server.Heartbeat = _ => (_server.Completed.IsEmpty ? 200 : 409, RenderNodeProtocol.Continue);
        using var worker = Worker(new FakeBlender(frames: 4), new Tweak { HoldComplete = TimeSpan.FromSeconds(2.5) });
        await worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(_server.Completed);
        lock (_log) Assert.DoesNotContain(_log, l => l.Contains("lease lost", StringComparison.Ordinal));
        Assert.False(Directory.Exists(worker.JobFolder("201")), "a job that succeeded leaves its scratch folder behind");
    }

    [Fact]
    public async Task AJobWhoseTimeLimitIsHuge_DoesNotStopTheWorker()
    {
        _server.Offer("202", Bundle(), limits: new RenderNodeLimits { MaxHours = 1e12 });
        using var worker = Worker(new FakeBlender(frames: 2), new Tweak());
        var ex = await Record.ExceptionAsync(() => worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Null(ex);
    }

    [Fact]
    public async Task AWorkerWithALongTimeLimit_RendersItsJob()
    {
        _server.Offer("203", Bundle());
        using var worker = Worker(new FakeBlender(frames: 2), new Tweak(), maxTime: TimeSpan.FromDays(60));
        var ex = await Record.ExceptionAsync(() => worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Null(ex);
        Assert.Single(_server.Completed);
    }

    [Fact]
    public async Task AClaimAnsweredWithANullBundle_IsNotTakenAndDoesNotStopTheWorker()
    {
        var tweak = new Tweak { ClaimBody = "{\"protocol\":1,\"jobId\":\"204\",\"attempt\":1,\"leaseSeconds\":6,\"plan\":\"full\",\"bundle\":null}" };
        using var worker = Worker(new FakeBlender(frames: 2), tweak);
        var ex = await Record.ExceptionAsync(() => worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Null(ex);
        var (job, fail) = Assert.Single(_server.Failed);
        Assert.Equal(("204", RenderNodeFailure.BadJob), (job, fail.Reason));
    }

    /// <summary>A body whose first bytes come and then nothing more, with the connection open.</summary>
    private sealed class StallingStream : Stream
    {
        private bool _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_sent) { _sent = true; buffer.Span[0] = 0x50; return 1; }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ADownloadThatStalls_EndsTheJob_ThoughTheHeartbeatsGetThrough()
    {
        _server.Offer("205", Bundle(), lease: 3);
        using var worker = Worker(new FakeBlender(frames: 2), new Tweak { StallBundle = true });
        var run = worker.RunAsync(once: true, CancellationToken.None);
        var ended = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(12)));
        Assert.True(ended == run, $"still downloading after 12 s, {_server.Beats.Count} heartbeats answered 200, the last '{_server.Beats.LastOrDefault()?.Phase}'");
        var (_, fail) = Assert.Single(_server.Failed);
        Assert.Equal(RenderNodeFailure.Download, fail.Reason);
        Assert.Contains("nothing moved", fail.Message);
    }

    [Fact]
    public async Task ABundleWhosePlanHasNullForASection_IsRefused_NotRetried()
    {
        string zip = Bundle();
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = archive.Entries.Single(e => e.FullName.EndsWith("/" + Plan.ScenePlanSerializer.FileName, StringComparison.Ordinal) && e.FullName.Count(c => c == '/') == 1);
            string name = entry.FullName;
            System.Text.Json.Nodes.JsonObject plan;
            using (var reader = new StreamReader(entry.Open())) plan = System.Text.Json.Nodes.JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            entry.Delete();
            plan["look"] = null;
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(plan.ToJsonString());
        }
        _server.Offer("206", zip);
        using var worker = Worker(new FakeBlender(frames: 2), new Tweak());
        await worker.RunAsync(once: true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        var (_, fail) = Assert.Single(_server.Failed);
        Assert.Equal(RenderNodeFailure.Refused, fail.Reason);
        Assert.False(fail.Retryable);
    }
}
