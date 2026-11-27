// The Blender render tools as an agent reaches them: through the in-app MCP host over HTTP, with the
// app's own renderer, model sources and presets behind them. Blender itself is a fake process that
// writes the image the plan or the script asks for; one opt-in test runs the real Blender. Every model
// is invented.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Therion.Blender.Execution;
using Therion.Blender.Presets;
using Therion.Blender.Tests;
using Therion.Build;
using Therion.Mcp;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public sealed class McpBlenderRenderTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "ThIDE-test", "mcp-blender-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public McpBlenderRenderTests()
    {
        _project = Path.Combine(_scratch, "project");
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "project.thconfig"), "source cave.th\n");
        File.WriteAllText(Path.Combine(_project, "cave.th"), """
            survey cave
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
                2 3 8.0 0 -10
              endcentreline
            endsurvey
            """);
        File.WriteAllBytes(Path.Combine(_project, "cave.3d"), InventedModel());
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Three stations, two legs with passage sizes: enough for walls and a camera to frame.</summary>
    private static byte[] InventedModel() =>
        Survex3dFixtureBuilder.V8("cave", "EPSG:31700", '.')
            .V8Label(0, 0, "cave.1", 0, 0, 0)
            .V8Label(0, 1, "2", 10, 0, 0)
            .V8Label(0, 1, "3", 10, 8, -1.4)
            .V8XSect(1, "1", 1, 1, 1.5, 0.5)
            .V8XSect(1, "2", 1.2, 0.8, 2, 0.5)
            .V8XSect(1, "3", 1, 1, 1, 0.5, last: true)
            .V8Style(0x00)
            .V8Move(0, 0, 0)
            .V8Line(0, (6, "cave"), 10, 0, 0)
            .V8Line(0, null, 10, 8, -1.4)
            .V8Stop()
            .Build();

    private McpBlenderRenderHost RenderHost(BlenderLocator locator, IBlenderProcessLauncher launcher) => new(
        new BlenderRenderService(locator, new BlenderRunner(launcher, _ => 50_000_000_000L), Path.Combine(_scratch, "jobs")),
        new BlenderSourceProvider(() => _project, new OutputArtifactCollector()),
        new PresetStore(Path.Combine(_scratch, "presets")));

    private static BlenderLocator FakeBlender() =>
        new(new FakeProbe(new BlenderVersion(5, 0, 1)), () => ["/fake/blender"]);

    [Fact]
    public async Task An_agent_renders_a_still_into_the_workspace_through_the_in_app_host()
    {
        var launcher = new FakeLauncher();
        await using var session = await Session.StartAsync(_scratch, RenderHost(FakeBlender(), launcher));
        var client = session.Client;
        await CallAsync(client, "load_workspace", new() { ["path"] = Path.Combine(_project, "project.thconfig") });

        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name);
        Assert.True(tools["list_render_presets"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tools["render_status"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tools["start_render"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tools["start_render"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.False(tools["cancel_render"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tools["export_render_script"].ProtocolTool.Annotations!.DestructiveHint);

        var presets = await CallAsync(client, "list_render_presets");
        Assert.Contains(presets.GetProperty("presets").EnumerateArray(), p => p.GetProperty("name").GetString() == "Orbit showcase");

        // (An empty title: over a model with a name, the preset would write it over the still, in
        // a second stage.)
        var started = await CallAsync(client, "start_render", new()
        {
            ["model"] = "cave.3d", ["outputKind"] = "still", ["width"] = 64, ["height"] = 48, ["samples"] = 1,
            ["outputDirectory"] = "renders", ["title"] = "",
        });
        var done = await FinishedAsync(client, started.GetProperty("jobId").GetString()!);

        Assert.Equal("succeeded", done.GetProperty("state").GetString());
        Assert.Equal("renders/cave-render.png", Assert.Single(done.GetProperty("outputs").EnumerateArray()).GetString());
        Assert.True(File.Exists(Path.Combine(_project, "renders", "cave-render.png")));
        Assert.Equal("cave.3d", done.GetProperty("model").GetString());
        Assert.Equal("CPU", done.GetProperty("device").GetString());
        // The player built the scene: it carries all of this preset.
        Assert.Equal("player", done.GetProperty("builder").GetString());
        Assert.False(done.GetProperty("draft").GetBoolean());
        // Blender ran the player's script, with factory settings, and nothing else.
        var run = Assert.Single(launcher.Runs);
        Assert.Contains("--factory-startup", run);
        Assert.EndsWith("render.py", run[^1]);
        Assert.Equal([true], launcher.Played);
    }

    [Fact]
    public async Task An_agent_picks_the_builder_and_renders_a_draft_first()
    {
        var launcher = new FakeLauncher();
        await using var session = await Session.StartAsync(_scratch, RenderHost(FakeBlender(), launcher));
        var client = session.Client;
        await CallAsync(client, "load_workspace", new() { ["path"] = Path.Combine(_project, "project.thconfig") });
        var still = new Dictionary<string, object?>
        {
            ["model"] = "cave.3d", ["outputKind"] = "still", ["width"] = 64, ["height"] = 48, ["samples"] = 1, ["outputDirectory"] = "renders",
            ["title"] = "",
        };

        // A draft: the player's, under its own name beside where the render goes.
        var started = await CallAsync(client, "start_render", new(still) { ["draft"] = true, ["builder"] = "generator" });
        Assert.True(started.GetProperty("draft").GetBoolean());
        var draft = await FinishedAsync(client, started.GetProperty("jobId").GetString()!);
        Assert.Equal("succeeded", draft.GetProperty("state").GetString());
        Assert.Equal("renders/cave-render-draft.png", Assert.Single(draft.GetProperty("outputs").EnumerateArray()).GetString());
        Assert.Equal("player", draft.GetProperty("builder").GetString());

        // The script generator, asked for by name.
        started = await CallAsync(client, "start_render", new(still) { ["builder"] = "generator" });
        var generated = await FinishedAsync(client, started.GetProperty("jobId").GetString()!);
        Assert.Equal("succeeded", generated.GetProperty("state").GetString());
        Assert.Equal("generator", generated.GetProperty("builder").GetString());
        Assert.Equal([true, false], launcher.Played);

        // A title: the still is made in two stages, its one frame and then the title over it.
        started = await CallAsync(client, "start_render", new(still) { ["title"] = "Invented Cave" });
        var titled = await FinishedAsync(client, started.GetProperty("jobId").GetString()!);
        Assert.Equal("succeeded", titled.GetProperty("state").GetString());
        Assert.Equal(4, launcher.Runs.Count);
        Assert.Equal(["--stage", "frames"], launcher.Runs[2].SkipWhile(a => a != "--").Skip(1).Take(2));
        Assert.Equal(["--stage", "compose"], launcher.Runs[3].SkipWhile(a => a != "--").Skip(1).Take(2));

        // No title given: the preset writes the cave's name where the model has one. This model's
        // title is only its file's name ("cave" of cave.3d), which is no name: nothing is written,
        // and the still is made in one stage.
        still.Remove("title");
        started = await CallAsync(client, "start_render", new(still));
        var unnamed = await FinishedAsync(client, started.GetProperty("jobId").GetString()!);
        Assert.Equal("succeeded", unnamed.GetProperty("state").GetString());
        Assert.Equal(5, launcher.Runs.Count);
        Assert.DoesNotContain("--stage", launcher.Runs[4]);
    }

    [Fact]
    public async Task An_agent_exports_the_script_into_the_workspace_without_running_blender()
    {
        var launcher = new FakeLauncher();
        await using var session = await Session.StartAsync(_scratch, RenderHost(FakeBlender(), launcher));
        await CallAsync(session.Client, "load_workspace", new() { ["path"] = Path.Combine(_project, "project.thconfig") });

        var exported = await CallAsync(session.Client, "export_render_script", new()
        {
            ["outputDirectory"] = "blender", ["model"] = "cave.3d", ["preset"] = "Documentation stills",
        });

        Assert.Equal("blender/render.py", exported.GetProperty("script").GetString());
        Assert.Equal(4, exported.GetProperty("frames").GetInt32());
        Assert.True(File.Exists(Path.Combine(_project, "blender", "render.py")));
        Assert.True(File.Exists(Path.Combine(_project, "blender", "model.ply")));
        Assert.Empty(launcher.Runs);
    }

    [Fact]
    public async Task Without_a_blender_the_in_app_host_offers_no_render_tools()
    {
        await using var session = await Session.StartAsync(_scratch, blender: null);

        var names = (await session.Client.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        Assert.Contains("run_build", names);
        Assert.DoesNotContain("start_render", names);
        Assert.DoesNotContain("list_render_presets", names);
    }

    [Fact]
    public async Task With_the_blender_module_off_agents_get_no_render_tools_until_it_is_turned_on()
    {
        var launcher = new FakeLauncher();
        await using var session = await Session.StartAsync(_scratch, RenderHost(FakeBlender(), launcher), blenderModule: false);

        var names = (await session.Client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Contains("run_build", names);
        Assert.DoesNotContain("start_render", names);
        Assert.DoesNotContain("write_tour_storyboard", names);

        // Turned on in Preferences: the server comes back with them, at a new endpoint.
        await session.SetBlenderModuleAsync(true);
        names = (await session.Client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Contains("start_render", names);
        Assert.Contains("export_render_script", names);

        await session.SetBlenderModuleAsync(false);
        names = (await session.Client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("start_render", names);
        Assert.Empty(launcher.Runs);
    }

    /// <summary>The same agent render with the real Blender (THIDE_BLENDER_PATH pins one).</summary>
    [BlenderFact]
    public async Task An_agent_renders_a_still_with_the_real_blender()
    {
        var locator = new BlenderLocator(new ProcessBlenderProbe());
        await using var session = await Session.StartAsync(_scratch, new McpBlenderRenderHost(
            new BlenderRenderService(locator, new BlenderRunner(new RealBlenderProcessLauncher()), Path.Combine(_scratch, "jobs"),
                blenderOverride: () => Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH")),
            new BlenderSourceProvider(() => _project, new OutputArtifactCollector()),
            new PresetStore(Path.Combine(_scratch, "presets"))));
        await CallAsync(session.Client, "load_workspace", new() { ["path"] = Path.Combine(_project, "project.thconfig") });

        var started = await CallAsync(session.Client, "start_render", new()
        {
            ["model"] = "cave.3d", ["outputKind"] = "still", ["width"] = 64, ["height"] = 48, ["samples"] = 1,
            ["outputDirectory"] = "renders",
        });
        var done = await FinishedAsync(session.Client, started.GetProperty("jobId").GetString()!, rounds: 10);

        Assert.True(done.GetProperty("state").GetString() == "succeeded", done.ToString());
        var png = Path.Combine(_project, "renders", "cave-render.png");
        Assert.True(new FileInfo(png).Length > 0);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?>? args = null)
    {
        var call = await client.CallToolAsync(tool, args);
        var payload = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(call.Content)).Text).RootElement;
        Assert.True(payload.GetProperty("ok").GetBoolean(), $"{tool}: {payload}");
        return payload.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> FinishedAsync(McpClient client, string jobId, int rounds = 3)
    {
        for (int i = 0; i < rounds; i++)
        {
            var status = await CallAsync(client, "render_status", new() { ["jobId"] = jobId, ["waitSeconds"] = 60 });
            if (status.GetProperty("state").GetString() != "running") return status;
        }
        throw new TimeoutException("the render did not finish");
    }

    /// <summary>The in-app host, listening, with an MCP client connected over HTTP and its bearer token.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private McpHostService _host = null!;
        private Settings _settings = null!;
        public McpClient Client { get; private set; } = null!;

        public static async Task<Session> StartAsync(string scratch, IBlenderRenderHost? blender, bool blenderModule = true)
        {
            var settings = new Settings();
            var locator = new ExternalToolLocator();
            var session = new Session
            {
                _settings = settings,
                _host = new McpHostService(settings, new LogService(), new UiBridge(), locator, new TherionCompiler(locator),
                    session: null, buffers: null, Path.Combine(scratch, "endpoint", "mcp-endpoint.json"), blender: blender),
            };
            settings.Save(settings.Current with { EnableMcpServer = true, EnableBlenderAnimation = blenderModule });
            await session._host.ApplySettingAsync();
            await session.ConnectAsync();
            return session;
        }

        /// <summary>Turns the Blender module on or off, as Preferences does, and connects to the server again.</summary>
        public async Task SetBlenderModuleAsync(bool on)
        {
            _settings.Save(_settings.Current with { EnableBlenderAnimation = on });
            await _host.ApplySettingAsync();
            await Client.DisposeAsync();
            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            var endpoint = _host.Endpoint ?? throw new InvalidOperationException("the in-app host did not start");
            Client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint.Url),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {endpoint.Token}" },
                Name = "render test",
            }));
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _host.StopAsync();
            await _host.DisposeAsync();
        }
    }

    private sealed class Settings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler? Changed;
        public void Save(AppSettings settings) { Current = settings; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeProbe(BlenderVersion version) : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => version;
    }

    /// <summary>Stands in for Blender: writes the still the script names and prints what the script would.</summary>
    private sealed class FakeLauncher : IBlenderProcessLauncher
    {
        public List<IReadOnlyList<string>> Runs { get; } = [];

        /// <summary>For each run, whether Blender was given a scene plan to play (true) or a
        /// generated script (false).</summary>
        public List<bool> Played { get; } = [];

        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            Runs.Add(args);
            var arguments = args.ToList();
            string scriptPath = arguments[arguments.IndexOf("--python") + 1];
            string planPath = Path.Combine(cwd, "scene-plan.json");
            byte[] png = [0x89, 0x50, 0x4e, 0x47];
            if (File.Exists(planPath))
            {
                // The player: it writes what its plan names, or, in the first of two stages, the
                // frame the second is made from.
                Played.Add(true);
                var plan = Therion.Blender.Plan.ScenePlanSerializer.ReadFile(planPath);
                int frames = arguments.IndexOf("--frames");
                if (arguments.Contains("frames") && frames > 0)
                {
                    Directory.CreateDirectory(arguments[frames + 1]);
                    File.WriteAllBytes(Path.Combine(arguments[frames + 1], "frame_0001.png"), png);
                }
                else
                {
                    Directory.CreateDirectory(plan.Output.Directory);
                    File.WriteAllBytes(Path.Combine(plan.Output.Directory, plan.Output.BaseName + ".png"), png);
                }
                return new FakeProcess();
            }
            Played.Add(false);
            var script = File.ReadAllText(scriptPath);
            var outDir = JsonSerializer.Deserialize<string>(Line(script, "OUT_DIR = "))!;
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, "cave-render.png"), png);
            return new FakeProcess();
        }

        private static string Line(string text, string prefix) =>
            text.Split('\n').First(l => l.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Trim();
    }

    private sealed class FakeProcess : IBlenderProcess
    {
        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var line in new[] { "THIDE:device=CPU", "THIDE:frame=1/1", "THIDE:done=1" })
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }

        public int ExitCode => 0;
        public void KillTree() { }
        public void Dispose() { }
    }
}
