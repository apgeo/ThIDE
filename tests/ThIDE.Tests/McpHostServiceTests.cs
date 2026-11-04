// T-03.1: the in-app MCP host lifecycle. Proves the EnableMcpServer toggle actually starts and stops
// a real loopback Kestrel listener, that the discovery file carries a usable port + token + pid, and
// that the bearer token is enforced (no token / wrong token → 401; correct token passes the gate).
// This automates what the plan lists as a manual smoke, so it stays a regression guard.
//
// Each test uses a temp discovery path (the internal test-seam ctor), so a run never touches the
// developer's real %AppData%/ThIDE/mcp-endpoint.json.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using Therion.Build;
using Therion.Mcp;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public class McpHostServiceTests
{
    /// <summary>An IAppSettingsService whose value and Changed event a test drives directly.</summary>
    private sealed class FakeSettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler? Changed;
        public void Save(AppSettings settings) { Current = settings; Changed?.Invoke(this, EventArgs.Empty); }
        public void SetMcpEnabled(bool on) => Save(Current with { EnableMcpServer = on });
    }

    private static McpHostService NewHost(FakeSettings settings, out string discoveryPath)
    {
        discoveryPath = Path.Combine(
            Path.GetTempPath(), "ThIDE-test", Guid.NewGuid().ToString("N"), "mcp-endpoint.json");
        var locator = new ExternalToolLocator();
        return new McpHostService(
            settings, new LogService(), new UiBridge(),
            locator, new TherionCompiler(locator), session: null, buffers: null, discoveryPath);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < timeout) await Task.Delay(50);
    }

    /// <summary>Path to the built <c>therion-mcp.dll</c>, injected by the csproj (the shim test spawns it).</summary>
    private static string ServerDll =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .First(a => a.Key == "TherionMcpServerDll").Value
        ?? throw new InvalidOperationException("TherionMcpServerDll metadata is missing.");

    /// <summary>
    /// The owed T-03.6 smoke, automated: start the in-app host, then reach it through the
    /// <c>therion-mcp --connect</c> shim over stdio (as a stdio-only host like LM Studio would). Seeing an
    /// R3 tool (<c>get_ui_state</c>) through the shim proves the relay bridged stdio to the *live* HTTP host —
    /// a headless server never exposes R3. No GUI needed: the host runs in-process.
    /// </summary>
    [Fact]
    public async Task The_connect_shim_bridges_a_stdio_client_to_the_running_in_app_host()
    {
        var settings = new FakeSettings();
        await using var host = NewHost(settings, out var discoveryPath);

        settings.SetMcpEnabled(true);
        await host.ApplySettingAsync();
        await WaitUntilAsync(() => host.IsListening && File.Exists(discoveryPath), TimeSpan.FromSeconds(20));
        Assert.True(host.IsListening, "the in-app host did not start");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "therion-mcp --connect (test)",
            Command = "dotnet",
            Arguments = [ServerDll, "--connect", discoveryPath],
            StandardErrorLines = line => Console.Error.WriteLine($"[shim] {line}"),
        });

        await using (var client = await McpClient.CreateAsync(transport))
        {
            var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
            Assert.Contains("server_info", tools);    // R1
            Assert.Contains("rename_symbol", tools);  // R2 (full profile)
            Assert.Contains("get_ui_state", tools);   // R3 — only the in-app host has it, so the shim reached it
        }

        settings.SetMcpEnabled(false);
        await host.StopAsync();
    }

    [Fact]
    public async Task Applying_the_setting_starts_the_listener_writes_the_discovery_file_and_enforces_the_token()
    {
        var settings = new FakeSettings();
        await using var host = NewHost(settings, out var discoveryPath);
        int stateChanges = 0;
        host.StateChanged += (_, _) => Interlocked.Increment(ref stateChanges);

        // Off by default: no listener, no discovery file.
        await host.ApplySettingAsync();
        Assert.False(host.IsListening);
        Assert.False(File.Exists(discoveryPath));

        // Turn it on.
        settings.SetMcpEnabled(true);
        await host.ApplySettingAsync();
        Assert.True(host.IsListening);
        Assert.NotNull(host.Port);
        Assert.True(File.Exists(discoveryPath));

        var info = McpEndpoint.TryRead(discoveryPath)!;
        Assert.Equal(host.Port, info.Port);
        Assert.False(string.IsNullOrWhiteSpace(info.Token));
        Assert.Equal(Environment.ProcessId, info.Pid);

        using var http = new HttpClient();

        // No token → rejected before reaching the MCP endpoint.
        var noToken = await http.GetAsync(info.Url);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

        // Wrong token → rejected.
        var wrong = new HttpRequestMessage(HttpMethod.Get, info.Url);
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(wrong)).StatusCode);

        // Correct token → passes the gate (the endpoint itself may answer any non-401 status).
        var ok = new HttpRequestMessage(HttpMethod.Get, info.Url);
        ok.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await http.SendAsync(ok)).StatusCode);

        // A real MCP client, over HTTP + the bearer token, handshakes and lists the catalog — the
        // acceptance smoke, automated. The in-app host also exposes ring R3 (a real UiBridge is present),
        // so the catalog is a superset of the stdio host's.
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(info.Url),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {info.Token}" },
            Name = "therion-mcp (in-app test)",
        });
        await using (var client = await McpClient.CreateAsync(transport))
        {
            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, t => t.Name == "server_info");   // R1
            Assert.Contains(tools, t => t.Name == "rename_symbol"); // R2 (Full profile)
            Assert.Contains(tools, t => t.Name == "get_ui_state");  // R3 (in-app only — T-03.3)
        }

        // Turn it off: the listener and its discovery file are both gone.
        settings.SetMcpEnabled(false);
        await host.ApplySettingAsync();
        Assert.False(host.IsListening);
        Assert.False(File.Exists(discoveryPath));

        Assert.True(stateChanges >= 2, $"expected at least a start and a stop, saw {stateChanges}");
    }

    [Fact]
    public async Task The_changed_event_alone_starts_and_stops_the_listener()
    {
        var settings = new FakeSettings();
        var log = new LogService();
        var errors = new List<string>();
        log.EntryAdded += (_, e) => { if (e.Level == LogVerbosity.Error) lock (errors) errors.Add(e.Message); };
        var discoveryPath = Path.Combine(Path.GetTempPath(), "ThIDE-test", Guid.NewGuid().ToString("N"), "mcp-endpoint.json");
        var locator = new ExternalToolLocator();
        await using var host = new McpHostService(
            settings, log, new UiBridge(), locator, new TherionCompiler(locator), session: null, buffers: null, discoveryPath);

        // No explicit ApplySettingAsync here: only the settings.Changed the Preferences toggle raises.
        // The host raises StateChanged when a start or a stop has finished, failed starts included, so
        // the test waits for that rather than polling against a deadline a loaded machine can miss.
        Assert.True(await StateAfterChangeAsync(host, () => settings.SetMcpEnabled(true), () => host.IsListening),
            $"the listener did not start: {string.Join(" | ", errors)}");

        Assert.True(await StateAfterChangeAsync(host, () => settings.SetMcpEnabled(false), () => !host.IsListening),
            "the listener did not stop");
    }

    /// <summary>Runs <paramref name="change"/>, then waits for the StateChanged it causes and reports <paramref name="expected"/>.</summary>
    private static async Task<bool> StateAfterChangeAsync(McpHostService host, Action change, Func<bool> expected)
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onState = (_, _) => settled.TrySetResult();
        host.StateChanged += onState;
        try
        {
            change();
            // The bound only stops a broken host from hanging the run; a healthy one answers in well under a second.
            await Task.WhenAny(settled.Task, Task.Delay(TimeSpan.FromMinutes(2)));
            return expected();
        }
        finally { host.StateChanged -= onState; }
    }

    [Fact]
    public void The_in_app_host_does_not_watch_its_configuration_files()
    {
        // A file watcher per host start costs an inotify instance on Linux, and creating one throws once the
        // per-user limit is reached, so the server failed to start whenever other watchers had used them up.
        var builder = McpHostService.CreateLoopbackBuilder();
        var watched = ((Microsoft.Extensions.Configuration.IConfigurationBuilder)builder.Configuration).Sources
            .OfType<Microsoft.Extensions.Configuration.FileConfigurationSource>()
            .Where(s => s.ReloadOnChange)
            .Select(s => s.Path)
            .ToList();
        Assert.Empty(watched);
    }

    [Fact]
    public void Discovery_file_path_lives_under_the_app_data_ThIDE_dir()
    {
        var path = McpHostService.DiscoveryFilePath();
        Assert.EndsWith(Path.Combine("ThIDE", "mcp-endpoint.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void The_discovery_file_is_owner_only_from_the_moment_it_exists()
    {
        if (OperatingSystem.IsWindows()) return;   // %AppData% is user-scoped; no POSIX modes there
        var dir = Path.Combine(Path.GetTempPath(), "ThIDE-test", Guid.NewGuid().ToString("N"), "ThIDE");
        var path = Path.Combine(dir, "mcp-endpoint.json");
        try
        {
            McpHostService.WriteOwnerOnly(path, "first");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));

            // A file left world-readable by an older version is replaced, not rewritten in place.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            McpHostService.WriteOwnerOnly(path, "second");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal("second", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); }
    }
}
