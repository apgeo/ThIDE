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
        await using var host = NewHost(settings, out _);

        // No explicit ApplySettingAsync here: only the settings.Changed the Preferences toggle raises.
        settings.SetMcpEnabled(true);
        await WaitUntilAsync(() => host.IsListening, TimeSpan.FromSeconds(15));
        Assert.True(host.IsListening);

        settings.SetMcpEnabled(false);
        await WaitUntilAsync(() => !host.IsListening, TimeSpan.FromSeconds(15));
        Assert.False(host.IsListening);
    }

    [Fact]
    public void Discovery_file_path_lives_under_the_app_data_ThIDE_dir()
    {
        var path = McpHostService.DiscoveryFilePath();
        Assert.EndsWith(Path.Combine("ThIDE", "mcp-endpoint.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }
}
