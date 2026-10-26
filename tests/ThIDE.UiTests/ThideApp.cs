using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Therion.Mcp;

namespace ThIDE.UiTests;

/// <summary>Runs a UI test only when THIDE_UI_TESTS=1 (see tests/run-ui-tests.sh).</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("THIDE_UI_TESTS") != "1")
            Skip = "UI test: set THIDE_UI_TESTS=1 or run tests/run-ui-tests.sh.";
        else if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            Skip = "UI test: no X display (run under tests/run-ui-tests.sh, which starts Xvfb).";
    }
}

/// <summary>
/// One running ThIDE process with the UI-automation server on, a throw-away profile, and an MCP client.
/// Selectors and tools are documented on the server (ui_* tools in ThIDE/Services/UiAutomation).
/// </summary>
public sealed class ThideApp : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _home;
    private readonly StringBuilder _output = new();
    private McpClient _client = null!;

    private ThideApp(Process process, string home)
    {
        _process = process;
        _home = home;
    }

    public static string FixturesDir => Metadata("UiFixtures");

    /// <summary>Copies a fixture project (tests/ui-fixtures/&lt;name&gt;) to a temp dir and returns the dir.</summary>
    public static string CopyFixture(string name)
    {
        var dst = Path.Combine(Path.GetTempPath(), "thide-ui-" + Guid.NewGuid().ToString("N")[..8], name);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(FixturesDir, name), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(Path.Combine(FixturesDir, name), f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
        return dst;
    }

    /// <summary>Starts ThIDE with <paramref name="files"/> (opened relative to <paramref name="cwd"/>).</summary>
    public static async Task<ThideApp> StartAsync(string? cwd = null, params string[] files)
    {
        var home = Path.Combine(Path.GetTempPath(), "thide-ui-profile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(home);
        var endpointFile = Path.Combine(home, "ui-automation-endpoint.json");

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = cwd ?? home,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(Metadata("ThIDEDll"));
        foreach (var f in files) psi.ArgumentList.Add(f);
        psi.Environment["THIDE_UI_AUTOMATION"] = "1";
        psi.Environment["THIDE_UI_AUTOMATION_ENDPOINT"] = endpointFile;
        // A fresh profile so settings/layout never leak between tests or into the developer's own.
        // (.NET maps ApplicationData to XDG_CONFIG_HOME on Linux; Windows/macOS use the real profile.)
        psi.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, "config");
        psi.Environment["XDG_DATA_HOME"] = Path.Combine(home, "data");
        psi.Environment["XDG_CACHE_HOME"] = Path.Combine(home, "cache");

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ThIDE.");
        var app = new ThideApp(process, home);
        process.OutputDataReceived += (_, e) => { lock (app._output) app._output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { lock (app._output) app._output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var sw = Stopwatch.StartNew();
            McpEndpoint? endpoint;
            while ((endpoint = McpEndpoint.TryRead(endpointFile)) is null)
            {
                if (process.HasExited) throw new InvalidOperationException("ThIDE exited during startup:\n" + app.Output);
                if (sw.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("No automation endpoint:\n" + app.Output);
                await Task.Delay(200);
            }
            app._client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint.Url),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + endpoint.Token },
            }));
            await app.WaitAsync("role:Window;state:showing", timeoutMs: 60_000);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public string Output { get { lock (_output) return _output.ToString(); } }

    /// <summary>Calls a ui_* tool and returns its text; a tool error becomes an exception with the app log.</summary>
    public async Task<string> CallAsync(string tool, object? args = null)
    {
        var dict = args is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args))!;
        var result = await _client.CallToolAsync(tool, dict);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (result.IsError == true) throw new UiToolException($"{tool} failed: {text}");
        return text;
    }

    public Task<string> TreeAsync(string? selector = null) => CallAsync("ui_tree", new { selector });
    public Task<string> FindAsync(string selector) => CallAsync("ui_find", new { selector });
    public Task<string> InvokeAsync(string selector, string action = "default") => CallAsync("ui_invoke", new { selector, action });
    public Task<string> MenuAsync(string path) => CallAsync("ui_menu", new { path });
    public Task<string> KeyAsync(string selector, params string[] keys) => CallAsync("ui_key", new { keys, selector });
    public Task<string> TypeAsync(string text, string? selector = null) => CallAsync("ui_type", new { text, selector });
    public Task<string> SetTextAsync(string selector, string text) => CallAsync("ui_set_text", new { selector, text });
    public Task<string> WaitAsync(string selector, bool gone = false, int timeoutMs = 20_000) =>
        CallAsync("ui_wait", new { selector, gone, timeoutMs });

    /// <summary>ui_get as JSON.</summary>
    public async Task<JsonElement> GetAsync(string selector, params string[] properties) =>
        JsonDocument.Parse(await CallAsync("ui_get", new { selector, properties })).RootElement;

    public async Task<string> TextOfAsync(string selector) =>
        (await GetAsync(selector)).GetProperty("text").GetString() ?? "";

    public async Task<bool> ExistsAsync(string selector) =>
        !(await FindAsync(selector)).StartsWith("No element", StringComparison.Ordinal);

    /// <summary>Saves a PNG of a window/element (rendered by the app itself).</summary>
    public async Task<byte[]> ScreenshotAsync(string? selector = null)
    {
        var dict = new Dictionary<string, object?> { ["selector"] = selector };
        var result = await _client.CallToolAsync("ui_screenshot", dict);
        if (result.IsError == true) throw new UiToolException("ui_screenshot failed");
        return result.Content.OfType<ImageContentBlock>().First().DecodedData.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { });
        _process.Dispose();
        try { Directory.Delete(_home, recursive: true); } catch { /* best effort */ }
    }

    private static string Metadata(string key) =>
        Path.GetFullPath(typeof(ThideApp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == key).Value!);
}

public sealed class UiToolException(string message) : Exception(message);
