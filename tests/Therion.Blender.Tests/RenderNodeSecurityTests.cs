// What a render node does with a server and a machine it does not trust, without Blender: plain
// HTTP to this machine only and never through a proxy, a bundle from the server's own origin only,
// fail messages that say no length, address or path of the node, answers read no further than a
// bound, a lease held to a day, a token file that is the user's alone, and scratch folders that
// another user cannot have made or swapped. Fake servers on 127.0.0.1, invented tokens and bundles.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Therion.Blender.Execution;
using Therion.Blender.Player;
using Therion.Blender.Tests.Player;
using Xunit.Abstractions;

namespace Therion.Blender.Tests;

public sealed class RenderNodeSecurityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"node-security-{Guid.NewGuid():N}")).FullName;
    private readonly List<string> _log = [];

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- addresses and proxies ----

    [Theory]
    [InlineData("http://127.0.0.1:8080/api/", true)]
    [InlineData("http://127.1.2.3/api/", true)]
    [InlineData("http://[::1]:8080/api/", true)]
    [InlineData("http://localhost/api/", true)]
    [InlineData("http://LOCALHOST:9/api/", true)]
    [InlineData("http://loopback:8080/api/", false)]
    [InlineData("http://localhost.:8080/api/", false)]
    [InlineData("http://127.0.0.1.nip.io:8080/api/", false)]
    [InlineData("http://0.0.0.0:8080/api/", false)]
    [InlineData("http://[::ffff:10.0.0.1]:8080/api/", false)]
    [InlineData("http://render.example.org/api/", false)]
    public void PlainHttp_IsForThisMachineOnly_NamedAsSuch(string url, bool allowed)
        => Assert.Equal(allowed, RenderNodeProtocol.IsAllowedAddress(new Uri(url), out _));

    [Fact]
    public void TheEnvironmentsProxy_IsForHttpsToOtherMachines_Only()
    {
        var proxy = new WebProxy("http://proxy.invalid:3128");
        var wrapped = new RenderNodeWorker.NoProxyInTheClear(proxy);
        Assert.True(wrapped.IsBypassed(new Uri("http://127.0.0.1:8080/api/claim")));
        Assert.True(wrapped.IsBypassed(new Uri("https://localhost:8443/api/claim")));
        Assert.True(wrapped.IsBypassed(new Uri("http://render.example.org/api/claim")));
        Assert.Null(wrapped.GetProxy(new Uri("http://[::1]/api/claim")));
        Assert.False(wrapped.IsBypassed(new Uri("https://render.example.org/api/claim")));
        Assert.Equal(new Uri("http://proxy.invalid:3128/"), wrapped.GetProxy(new Uri("https://render.example.org/api/claim")));
    }

    [Fact]
    public async Task AProxyOfTheEnvironment_SeesNoCallToAServerOnThisMachine()
    {
        using var proxy = new TokenCatcher();
        using var server = new RawServer();
        // As a plain handler with this proxy would, the token goes to the proxy: the test sees it.
        using (var plain = new HttpClient(new SocketsHttpHandler { Proxy = new WebProxy(proxy.Url), UseProxy = true }))
        {
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.Url, "claim")) { Content = new StringContent("{}") };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invented-node-token-77");
            try { using var _ = await plain.SendAsync(request); } catch (HttpRequestException) { }
        }
        Assert.Contains(proxy.Seen, s => s.Contains("invented-node-token-77", StringComparison.Ordinal));
        proxy.Seen.Clear();

        using var worker = Worker(server.Url, handler: RenderNodeWorker.Handler(new WebProxy(proxy.Url)));
        await worker.RunAsync(once: true, CancellationToken.None);
        Assert.Empty(proxy.Seen);
        Assert.Contains(server.Requests, r => r.StartsWith("POST /api/nodes/claim auth=yes", StringComparison.Ordinal));
    }

    // ---- the bundle's address, and what a failure tells the server ----

    [Fact]
    public async Task ABundleOnAnotherOrigin_IsRefused_AndNothingThereIsFetched()
    {
        // A service of this machine's that the server cannot reach: it holds an invented secret.
        using var local = new RawServer { Extra = c => { RawServer.Answer(c, 200, "invented local secret " + new string('x', 1211)); return true; } };
        using var server = new RawServer();
        using var worker = Worker(server.Url);
        server.Job = JobJson($"http://127.0.0.1:{local.Port}/internal/report", 4L * 1024 * 1024 * 1024 - 1);
        await worker.RunAsync(once: true, CancellationToken.None);
        server.Job = JobJson($"https://storage.invalid/b.zip", 1000);
        await worker.RunAsync(once: true, CancellationToken.None);

        Assert.Empty(local.Requests);
        var fails = server.Fails.Select(f => RenderNodeProtocol.Read<RenderNodeFail>(f)!).ToList();
        Assert.Equal(2, fails.Count);
        Assert.All(fails, f => Assert.Equal(RenderNodeFailure.BadJob, f.Reason));
        Assert.All(fails, f => Assert.Contains("not on the server's own origin", f.Message));
    }

    [Fact]
    public async Task ABundleOfAnotherSize_TellsTheServerNoLength()
    {
        using var server = new RawServer();
        string body = "invented bundle " + new string('y', 1217);
        server.Extra = c =>
        {
            if (!c.Request.Url!.AbsolutePath.EndsWith("/b.zip", StringComparison.Ordinal)) return false;
            RawServer.Answer(c, 200, body);
            return true;
        };
        using var worker = Worker(server.Url);
        server.Job = JobJson("/b.zip", 4L * 1024 * 1024 * 1024 - 1);
        await worker.RunAsync(once: true, CancellationToken.None);
        var fail = RenderNodeProtocol.Read<RenderNodeFail>(Assert.Single(server.Fails))!;
        output.WriteLine(fail.Message);
        Assert.Equal(RenderNodeFailure.Checksum, fail.Reason);
        Assert.DoesNotContain(Encoding.UTF8.GetByteCount(body).ToString(), fail.Message);
        Assert.DoesNotContain("4294967295", fail.Message);
    }

    [Fact]
    public async Task ABundleThatCannotBeReached_TellsTheServerNoAddress()
    {
        using var server = new RawServer();
        // The network's own words for a failed connection name the address.
        using var worker = Worker(server.Url, handler: new FailingDownloads(server.Port));
        server.Job = JobJson("/b.zip", 1000);
        await worker.RunAsync(once: true, CancellationToken.None);
        var fail = RenderNodeProtocol.Read<RenderNodeFail>(Assert.Single(server.Fails))!;
        output.WriteLine(fail.Message);
        Assert.Equal(RenderNodeFailure.Download, fail.Reason);
        Assert.DoesNotContain("127.0.0.1", fail.Message);
        Assert.DoesNotContain(server.Port.ToString(), fail.Message);
    }

    [Fact]
    public async Task AHugeClaimAnswer_IsNotReadIntoMemory()
    {
        using var server = new RawServer();
        server.Extra = c =>
        {
            if (!c.Request.Url!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal)) return false;
            c.Response.StatusCode = 200;
            c.Response.SendChunked = true;
            var chunk = Encoding.ASCII.GetBytes(new string(' ', 1 << 20));
            try
            {
                c.Response.OutputStream.Write("{\"protocol\":1,\"name\":\""u8);
                for (int i = 0; i < 300; i++) c.Response.OutputStream.Write(chunk);
                c.Response.OutputStream.Write("\"}"u8);
            }
            catch { /* the worker stops reading */ }
            try { c.Response.Close(); } catch { }
            return true;
        };
        using var worker = Worker(server.Url);
        long before = GC.GetTotalAllocatedBytes(precise: true);
        await worker.RunAsync(once: true, CancellationToken.None);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        output.WriteLine($"a 300 MB claim answer: the worker allocated {allocated / 1_000_000} MB");
        Assert.True(allocated < 64L * 1024 * 1024, $"{allocated / 1_000_000} MB");
        lock (_log) Assert.Contains(_log, l => l.StartsWith("claim failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ALeaseOfTheServer_IsHeldToADay()
    {
        Assert.Equal(RenderNodeProtocol.MaxLeaseSeconds, RenderNodeProtocol.LeaseSeconds(int.MaxValue));
        Assert.Equal(3, RenderNodeProtocol.LeaseSeconds(-5));
        Assert.Equal(120, RenderNodeProtocol.LeaseSeconds(120));
        Assert.Equal(TimeSpan.FromHours(8), RenderNodeProtocol.HeartbeatInterval(int.MaxValue, null));
    }

    [Fact]
    public async Task AJobWithALeaseOfDecades_IsTakenAsOneOfADay_AndTheWorkerGoesOn()
    {
        using var server = new RawServer();
        server.Job = JobJson("/b.zip", 10).Replace("\"leaseSeconds\":30", "\"leaseSeconds\":2147483647");
        using var worker = Worker(server.Url);
        var e = await Record.ExceptionAsync(() => worker.RunAsync(once: true, CancellationToken.None));
        Assert.Null(e);
        Assert.Single(server.Fails);  // the bundle of 10 bytes is not one; the job fails, the worker stays
    }

    // ---- what the node prints, and what it sends ----

    [Fact]
    public async Task AnEntryNameWithTerminalEscapes_ReachesTheOperatorsLogOnOneLine()
    {
        string zip = RenderBundleReaderSecurityTests.Craft(_dir, "escapes");
        string evil = "escapes/\u001b]0;invented title\u0007\u001b[2J\ninvented: succeeded/../x.txt";
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Update))
        using (var s = archive.CreateEntry(evil).Open()) s.WriteByte(1);
        using var server = new FakeRenderServer();
        server.Offer("escapes", zip);
        using var worker = new RenderNodeWorker(new BlenderLocator(new FakeProbe()), new BlenderRunner(new NoBlender()),
            new RenderNodeOptions { Server = server.Url, Token = server.Token, Scratch = Path.Combine(_dir, "scratch-esc"), Poll = TimeSpan.FromMilliseconds(100) },
            () => "/usr/bin/true", line => { lock (_log) _log.Add(line); });
        await worker.RunAsync(once: true, CancellationToken.None);
        string? line;
        lock (_log) line = _log.FirstOrDefault(l => l.Contains("failed (refused)"));
        Assert.NotNull(line);
        Assert.DoesNotContain(line, char.IsControl);
        var (_, fail) = Assert.Single(server.Failed);
        Assert.DoesNotContain(fail.Message, char.IsControl);
    }

    [Fact]
    public void WhatTheNodeSends_NamesNoPathOfItsOwn()
    {
        string scratch = Path.Combine(_dir, "scratch-redact");
        using var worker = Worker(new Uri("http://127.0.0.1:9/api/nodes/"), scratch: scratch);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string text = $"Traceback: {Path.Combine(scratch, "j1", "bundle", "render.py")} and {Path.Combine(home, "blender", "x.py")}";
        string shown = worker.Redact(text);
        Assert.DoesNotContain(scratch, shown);
        Assert.Contains(Path.Combine("<scratch>", "j1", "bundle", "render.py"), shown);
        // A path that only starts the same is another path.
        Assert.Equal(scratch + "-other", worker.Redact(scratch + "-other"));
        Assert.Equal("'<scratch>'", worker.Redact($"'{scratch}'"));
        if (home.Length > 1 && !scratch.StartsWith(home, StringComparison.Ordinal)) Assert.DoesNotContain(home, shown);
    }

    // ---- the token file ----

    [Fact]
    public void ATokenFile_ThatItsGroupMayRead_IsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        string file = Token(Path.Combine(_dir, "private"), "invented-token");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.False(RenderNodeToken.TryRead(file, out _, out string? problem));
        Assert.Contains("chmod 600", problem);
    }

    [Fact]
    public void ATokenFile_InAFolderAnyoneMayWrite_IsRefused_AndInAStickyOne_IsRead()
    {
        if (OperatingSystem.IsWindows()) return;
        string open = Directory.CreateDirectory(Path.Combine(_dir, "open")).FullName;
        File.SetUnixFileMode(open, (UnixFileMode)0x1FF);
        string file = Token(open, "invented-token");
        Assert.False(RenderNodeToken.TryRead(file, out _, out string? problem));
        Assert.Contains("anyone may write the folder", problem);
        File.SetUnixFileMode(open, (UnixFileMode)0x3FF);  // 1777, as /tmp
        Assert.True(RenderNodeToken.TryRead(file, out string token, out problem), problem);
        Assert.Equal("invented-token", token);
    }

    [Fact]
    public void ATokenFile_ThatIsAnotherUsers_IsRefused()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        // A file of the system's, readable by its owner only: /etc/shadow where there is one.
        string? theirs = new[] { "/etc/shadow", "/etc/master.passwd" }.FirstOrDefault(File.Exists);
        if (theirs is null) return;
        Assert.False(RenderNodeToken.TryRead(theirs, out _, out string? problem));
        output.WriteLine(problem);
    }

    [Fact]
    public void ATokenFile_ThatOthersMayRead_IsRefused_OnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Token(_dir, "invented-token");
        var info = new FileInfo(file);
        // The user's alone, as a file in the user's profile is.
        var security = new System.Security.AccessControl.FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);
        Assert.True(RenderNodeToken.TryRead(file, out _, out string? mine), mine);
        security = info.GetAccessControl();
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
            System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);
        Assert.False(RenderNodeToken.TryRead(file, out _, out string? problem));
        Assert.Contains("others may read the token file", problem);
    }

    // ---- private folders ----

    [Fact]
    public void AFolderThatIsNotThere_IsMadeForTheUserAlone()
    {
        string folder = Path.Combine(_dir, "made", "deeper");
        Assert.Equal(Path.GetFullPath(folder), PrivateFolders.Ensure(folder));
        Assert.True(Directory.Exists(folder));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(folder)!));
        }
    }

    [Fact]
    public void AFolderOthersMayWrite_OrInAFolderAnyoneMayWrite_OrALink_IsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        string open = Directory.CreateDirectory(Path.Combine(_dir, "open-scratch")).FullName;
        File.SetUnixFileMode(open, (UnixFileMode)0x1FF);
        Assert.Contains("Others may write the folder", Assert.Throws<UnsafeFolderException>(() => PrivateFolders.Ensure(open)).Message);
        // ThIDE's own folder is made private instead.
        PrivateFolders.Ensure(open, tighten: true);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(open));

        string shared = Directory.CreateDirectory(Path.Combine(_dir, "shared")).FullName;
        File.SetUnixFileMode(shared, (UnixFileMode)0x1FF);
        Assert.Contains("Anyone may write the folder", Assert.Throws<UnsafeFolderException>(() => PrivateFolders.Ensure(Path.Combine(shared, "scratch"))).Message);
        File.SetUnixFileMode(shared, (UnixFileMode)0x3FF);  // 1777: each keeps to their own
        PrivateFolders.Ensure(Path.Combine(shared, "scratch"));

        string link = Path.Combine(_dir, "linked");
        Directory.CreateSymbolicLink(link, open);
        Assert.Contains("is a link", Assert.Throws<UnsafeFolderException>(() => PrivateFolders.Ensure(link)).Message);
    }

    [Fact]
    public void AFolderOfAnotherUser_IsRefused()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        // The root folder is the superuser's, and the user cannot change its mode.
        Assert.Contains("belongs to another user", Assert.Throws<UnsafeFolderException>(() => PrivateFolders.Ensure("/")).Message);
    }

    [Fact]
    public async Task AWorkerWhoseScratchOthersMayWrite_DoesNotStart()
    {
        if (OperatingSystem.IsWindows()) return;
        // As another user would leave /tmp/thide-render-node: made first, open to all.
        string scratch = Directory.CreateDirectory(Path.Combine(_dir, "shared-tmp", "thide-render-node")).FullName;
        File.SetUnixFileMode(scratch, (UnixFileMode)0x1FF);
        using var server = new RawServer();
        using var worker = Worker(server.Url, scratch: scratch);
        await Assert.ThrowsAsync<UnsafeFolderException>(() => worker.RunAsync(once: true, CancellationToken.None));
        Assert.Empty(server.Requests);

        var folderWorker = new RenderWorker(new BlenderLocator(new FakeProbe()), new BlenderRunner(new NoBlender()),
            new RenderWorkerOptions { Inbox = Directory.CreateDirectory(Path.Combine(_dir, "in")).FullName, Outbox = Path.Combine(_dir, "out"), Scratch = scratch });
        await Assert.ThrowsAsync<UnsafeFolderException>(() => folderWorker.RunAsync(once: true, CancellationToken.None));
    }

    [Fact]
    public void TheDefaultRoot_IsUnderTheUsersOwnCacheFolder()
    {
        string home = Path.Combine(_dir, "home"), local = Path.Combine(_dir, "local");
        string? Env(string name) => name == "XDG_CACHE_HOME" ? Path.Combine(_dir, "xdg") : null;
        string? Relative(string name) => name == "XDG_CACHE_HOME" ? "relative/cache" : null;
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(local, "ThIDE"), PrivateFolders.RootFor(Env, home, local));
        else if (OperatingSystem.IsMacOS())
            Assert.Equal(Path.Combine(home, "Library", "Caches", "ThIDE"), PrivateFolders.RootFor(Env, home, local));
        else
        {
            Assert.Equal(Path.Combine(_dir, "xdg", "ThIDE"), PrivateFolders.RootFor(Env, home, local));
            Assert.Equal(Path.Combine(home, ".cache", "ThIDE"), PrivateFolders.RootFor(Relative, home, local));
            Assert.Equal(Path.Combine(home, ".cache", "ThIDE"), PrivateFolders.RootFor(_ => null, home, local));
        }
    }

    // ---- the render queue's file, moved from the shared temp folder ----

    private (string Old, string New) QueueFolders()
    {
        string old = Directory.CreateDirectory(Path.Combine(_dir, "tmp", "ThIDE", "blender-jobs")).FullName;
        string now = PrivateFolders.Ensure(Path.Combine(_dir, "cache", "ThIDE", "blender-jobs"));
        var record = new JsonObject
        {
            ["id"] = "q1",
            ["framesDirectory"] = Path.Combine(old, "queue", "q1", "frames"),
            ["model"] = Path.Combine(_dir, "cave", "invented.lox"),
            ["list"] = new JsonArray(Path.Combine(old, "queue", "q1", "x.png")),
        };
        File.WriteAllText(Path.Combine(old, "render-queue.json"), new JsonArray(record).ToJsonString());
        return (old, now);
    }

    [Fact]
    public void TheQueuesFile_IsMovedOnce_WithItsPathsUnderTheNewFolder()
    {
        var (old, now) = QueueFolders();
        Assert.True(PrivateFolders.MoveQueueFile(old, now, "render-queue.json"));
        Assert.False(File.Exists(Path.Combine(old, "render-queue.json")));
        var moved = JsonNode.Parse(File.ReadAllText(Path.Combine(now, "render-queue.json")))![0]!;
        Assert.Equal(Path.Combine(now, "queue", "q1", "frames"), (string?)moved["framesDirectory"]);
        Assert.Equal(Path.Combine(now, "queue", "q1", "x.png"), (string?)moved["list"]![0]);
        Assert.Equal(Path.Combine(_dir, "cave", "invented.lox"), (string?)moved["model"]);
        // Once: a file in the new place is never replaced.
        File.WriteAllText(Path.Combine(old, "render-queue.json"), "[]");
        Assert.False(PrivateFolders.MoveQueueFile(old, now, "render-queue.json"));
    }

    [Fact]
    public void TheQueuesFile_ThatOthersMayWrite_OrThatIsALink_IsLeftWhereItIs()
    {
        if (OperatingSystem.IsWindows()) return;
        var (old, now) = QueueFolders();
        string file = Path.Combine(old, "render-queue.json");
        File.SetUnixFileMode(file, (UnixFileMode)0x1B6);  // 666
        Assert.False(PrivateFolders.MoveQueueFile(old, now, "render-queue.json"));
        Assert.False(File.Exists(Path.Combine(now, "render-queue.json")));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(file, file + ".real");
        File.CreateSymbolicLink(file, file + ".real");
        Assert.False(PrivateFolders.MoveQueueFile(old, now, "render-queue.json"));
    }

    // ---- Blender as a batch file ----

    [Theory]
    [InlineData("C:\\x\\blender.cmd")]
    [InlineData("C:\\x\\blender.BAT")]
    [InlineData("C:\\x\\blender.cmd. ")]
    public void ABatchFile_IsNeverStartedAsBlender_OnWindows(string path)
    {
        Assert.Equal(OperatingSystem.IsWindows(), BlenderLocator.IsBatchFile(path));
        if (!OperatingSystem.IsWindows()) return;
        var located = new BlenderLocator(new FakeProbe()).Locate(path);
        Assert.False(located.IsUsable);
        Assert.Contains("batch file", located.Detail);
        Assert.Throws<InvalidOperationException>(() => new RealBlenderProcessLauncher().Start(path, ["--version"], _dir));
    }

    // ---- helpers ----

    private static string Token(string folder, string token)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "token");
        File.WriteAllText(path, token + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    private RenderNodeWorker Worker(Uri server, HttpMessageHandler? handler = null, string? scratch = null) => new(
        new BlenderLocator(new FakeProbe()), new BlenderRunner(new NoBlender()), new RenderNodeOptions
        {
            Server = server, Token = "invented-node-token-77", NodeName = "security", Scratch = scratch ?? Path.Combine(_dir, "scratch"),
            Poll = TimeSpan.FromMilliseconds(100), FirstBackoff = TimeSpan.FromMilliseconds(20), MaxBackoff = TimeSpan.FromMilliseconds(50), DownloadTries = 1,
        },
        () => "/usr/bin/true", line => { lock (_log) _log.Add(line); }, handler);

    private static string JobJson(string url, long size)
        => $"{{\"protocol\":1,\"jobId\":\"j1\",\"attempt\":1,\"leaseSeconds\":30,\"plan\":\"full\","
           + $"\"bundle\":{{\"url\":{JsonValue.Create(url)!.ToJsonString()},\"size\":{size},\"sha256\":\"{new string('0', 64)}\"}}}}";

    private sealed class FakeProbe : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => new BlenderVersion(4, 5, 16);
    }

    private sealed class NoBlender : IBlenderProcessLauncher
    {
        public IBlenderProcess Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
            => throw new InvalidOperationException("no Blender in this test");
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Sends every request as a plain handler does, but fails the download of a bundle
    /// as a refused connection does, in the words that name its address.</summary>
    private sealed class FailingDownloads(int port) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => request.RequestUri!.AbsolutePath.EndsWith("/b.zip", StringComparison.Ordinal)
                ? throw new HttpRequestException($"Connection refused (127.0.0.1:{port})", new SocketException((int)SocketError.ConnectionRefused))
                : base.SendAsync(request, ct);
    }

    /// <summary>A proxy on 127.0.0.1 that keeps the head of each request it gets and answers none.</summary>
    private sealed class TokenCatcher : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public ConcurrentBag<string> Seen { get; } = [];

        public Uri Url { get; }

        public TokenCatcher()
        {
            _listener.Start();
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        var buffer = new byte[8192];
                        int n = await client.GetStream().ReadAsync(buffer, _stop.Token);
                        Seen.Add(Encoding.ASCII.GetString(buffer, 0, n));
                    }
                    catch { if (_stop.IsCancellationRequested) return; }
                }
            });
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    /// <summary>A server on 127.0.0.1 that answers the claim with <see cref="Job"/> as it is, and
    /// keeps the bodies of fail.</summary>
    private sealed class RawServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _loop;

        public int Port { get; }
        public string Job { get; set; } = "";
        public ConcurrentQueue<string> Fails { get; } = new();
        public ConcurrentQueue<string> Requests { get; } = new();
        public Func<HttpListenerContext, bool>? Extra { get; set; }

        public RawServer()
        {
            // A free port may be taken by another test before it is listened on: another one then.
            for (int tries = 1; ; tries++)
            {
                Port = FreePort();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                try
                {
                    _listener.Start();
                    break;
                }
                catch (HttpListenerException) when (tries < 20) { _listener.Close(); }
            }
            _loop = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext c;
                    try { c = await _listener.GetContextAsync(); } catch { return; }
                    try
                    {
                        Requests.Enqueue($"{c.Request.HttpMethod} {c.Request.Url!.AbsolutePath} auth={(c.Request.Headers["Authorization"] is null ? "no" : "yes")}");
                        if (Extra?.Invoke(c) == true) continue;
                        string body = new StreamReader(c.Request.InputStream).ReadToEnd();
                        string path = c.Request.Url!.AbsolutePath;
                        if (path.EndsWith("/claim", StringComparison.Ordinal)) Answer(c, Job.Length > 0 ? 200 : 204, Job);
                        else if (path.EndsWith("/fail", StringComparison.Ordinal)) { Fails.Enqueue(body); Answer(c, 200, ""); }
                        else if (path.EndsWith("/heartbeat", StringComparison.Ordinal)) Answer(c, 200, "{\"action\":\"continue\"}");
                        else Answer(c, 404, "");
                    }
                    catch { /* a client that went away */ }
                }
            });
        }

        public Uri Url => new($"http://127.0.0.1:{Port}/api/nodes/");

        public static void Answer(HttpListenerContext c, int status, string text)
        {
            c.Response.StatusCode = status;
            byte[] b = Encoding.UTF8.GetBytes(text);
            c.Response.ContentLength64 = b.Length;
            if (b.Length > 0) c.Response.OutputStream.Write(b);
            c.Response.Close();
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
            try { _loop.Wait(2000); } catch { }
        }
    }
}
