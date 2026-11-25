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
        if (SkipReason(Environment.GetEnvironmentVariable("THIDE_UI_TESTS"), Environment.GetEnvironmentVariable("DISPLAY"),
                OperatingSystem.IsLinux(), OperatingSystem.IsWindows()) is { } reason)
            Skip = reason;
    }

    /// <summary>Why a UI test cannot run here, or null when it can.</summary>
    internal static string? SkipReason(string? uiTests, string? display, bool isLinux, bool isWindows)
    {
        if (uiTests != "1")
            return "UI test: set THIDE_UI_TESTS=1 or run tests/run-ui-tests.sh.";
        // The throw-away profile is set up through XDG_* (see StartAsync), which Windows ignores: the
        // app would read and write the developer's own %APPDATA%\ThIDE.
        if (isWindows)
            return "UI test: not run on Windows, where the app cannot be given a throw-away profile.";
        if (isLinux && string.IsNullOrEmpty(display))
            return "UI test: no X display (run under tests/run-ui-tests.sh, which starts Xvfb).";
        return null;
    }
}

/// <summary>
/// One running ThIDE process with the UI-automation server on, a throw-away profile, a temp folder
/// of its own, and an MCP client.
/// Selectors and tools are documented on the server (ui_* tools in ThIDE/Services/UiAutomation).
/// </summary>
public sealed class ThideApp : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _home;
    private readonly StringBuilder _output = new();
    private McpClient _client = null!;

    private ThideApp(Process process, string home, string tempDirectory)
    {
        _process = process;
        _home = home;
        TempDirectory = tempDirectory;
    }

    /// <summary>The app's own temp folder: what <c>Path.GetTempPath()</c> is inside the app. Deleted
    /// when the app is disposed.</summary>
    public string TempDirectory { get; }

    public static string FixturesDir => Metadata("UiFixtures");

    /// <summary>Where screenshots worth keeping go (tests/ui-artifacts unless UI_ARTIFACTS says otherwise).</summary>
    public static string ArtifactsDir =>
        Environment.GetEnvironmentVariable("UI_ARTIFACTS") is { Length: > 0 } dir
            ? dir
            : Path.GetFullPath(Path.Combine(FixturesDir, "..", "ui-artifacts"));

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
    public static Task<ThideApp> StartAsync(string? cwd = null, params string[] files)
        => StartAsync(new Dictionary<string, string>(), cwd, files);

    /// <summary>As <see cref="StartAsync(string?, string[])"/>, with extra or changed environment
    /// variables for the app (such as a PATH that finds a stub tool first).</summary>
    public static async Task<ThideApp> StartAsync(
        IReadOnlyDictionary<string, string> environment, string? cwd, params string[] files)
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
        // (.NET maps ApplicationData to XDG_CONFIG_HOME on Linux and macOS; Windows would use the real
        // profile, so UiFact skips there.)
        // The folders must exist: for a missing XDG_CONFIG_HOME .NET returns "" and the app then falls
        // back to ~/.config, i.e. the developer's real profile.
        psi.Environment["XDG_CONFIG_HOME"] = Directory.CreateDirectory(Path.Combine(home, "config")).FullName;
        psi.Environment["XDG_DATA_HOME"] = Directory.CreateDirectory(Path.Combine(home, "data")).FullName;
        psi.Environment["XDG_CACHE_HOME"] = Directory.CreateDirectory(Path.Combine(home, "cache")).FullName;
        // A temp folder of its own as well: the app keeps render jobs, tool probes and viewer models
        // under fixed names in the temp folder (<temp>/ThIDE/...), where a ThIDE the developer has
        // open keeps its own.
        var temp = CreateTempDirectory();
        foreach (var name in TempVariables) psi.Environment[name] = temp;
        foreach (var (name, value) in environment) psi.Environment[name] = value;

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ThIDE.");
        }
        catch
        {
            DeleteQuietly(temp);
            DeleteQuietly(home);
            throw;
        }
        var app = new ThideApp(process, home, temp);
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

    /// <summary>True once the app process has ended.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Waits up to <paramref name="timeout"/> for the app to exit on its own; true when it did.</summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        try { await _process.WaitForExitAsync().WaitAsync(timeout); return true; }
        catch (TimeoutException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { });
        _process.Dispose();
        DeleteQuietly(_home);
        DeleteQuietly(TempDirectory);
    }

    /// <summary>The variables that name the temp folder: TMPDIR on Linux and macOS, TEMP and TMP on Windows.</summary>
    internal static readonly string[] TempVariables = ["TMPDIR", "TEMP", "TMP"];

    /// <summary>
    /// Makes a new, empty temp folder for one app and returns its path, which is short and has no
    /// symbolic link in it.
    /// Short, because .NET puts Unix sockets in the temp folder (the diagnostics socket and every
    /// named pipe) and a socket path is at most about a hundred bytes: with a longer temp path the
    /// app never finishes starting. So on Linux and macOS the folder goes straight under /tmp,
    /// whatever TMPDIR the tests themselves run with.
    /// Without links, because a file under a linked folder has two spellings, the one given and
    /// the resolved one, and the app goes wrong where it meets both (with a project in such a
    /// folder, Link Survey File writes a wrong relative path).
    /// </summary>
    internal static string CreateTempDirectory()
    {
        var root = RealPath(OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp");
        var dir = Path.Combine(root, "thui-" + Guid.NewGuid().ToString("N")[..8]);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }

    /// <summary><paramref name="path"/>, which must exist, with every symbolic link in it followed.</summary>
    internal static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var real = Path.GetPathRoot(full)!;
        foreach (var part in full[real.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            real = Path.Combine(real, part);
            if (Directory.ResolveLinkTarget(real, returnFinalTarget: true) is { } target)
                real = RealPath(target.FullName); // the target's own path may go through links too
        }
        return real;
    }

    private static void DeleteQuietly(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private static string Metadata(string key) =>
        Path.GetFullPath(typeof(ThideApp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == key).Value!);
}

public sealed class UiToolException(string message) : Exception(message);
