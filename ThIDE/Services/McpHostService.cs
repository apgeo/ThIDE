// The in-app MCP host (02 §Mode 2, T-03.1). With EnableMcpServer on, ThIDE listens on a loopback
// Kestrel endpoint so a local LLM host can reach the *running* IDE's project tools over streamable
// HTTP. The catalog itself lives in the UI-free Therion.Mcp library (AddTherionMcpTools); this class
// is only the transport + lifecycle + safety shell, and is the one place Kestrel/ASP.NET is touched.
//
// Security (02 §B.6): loopback bind only, a random bearer token required on every request, and a
// discovery file under the app-data dir so a shim/host can find the port+token. Off by default; the
// listener never initializes while the flag is off (perf-toggle precedent). Every tool call the SDK
// narrates is mirrored into the in-app LogService (Log pane = audit trail).

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Therion.Mcp;
using Therion.Processing.Abstractions;

namespace ThIDE.Services;

/// <summary>Contents of the MCP discovery file (<c>%AppData%/ThIDE/mcp-endpoint.json</c>).</summary>
/// <remarks>Fields are camelCase on the wire (D-012); a reader must tolerate a stale <c>pid</c>.</remarks>
public sealed record McpEndpointInfo(int Port, string Token, int Pid, string StartedUtc, string Url);

/// <summary>
/// Owns the running IDE's loopback MCP listener. Starts and stops to follow the
/// <c>EnableMcpServer</c> setting, live.
/// </summary>
public interface IMcpHostService : IAsyncDisposable
{
    /// <summary>True while the Kestrel listener is up.</summary>
    bool IsListening { get; }

    /// <summary>The bound loopback port while listening, else null.</summary>
    int? Port { get; }

    /// <summary>Raised (on an arbitrary thread) whenever <see cref="IsListening"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Start or stop the listener to match the current <c>EnableMcpServer</c> setting. Idempotent.</summary>
    Task ApplySettingAsync(CancellationToken ct = default);

    /// <summary>Stop the listener and remove the discovery file (idempotent) — for a clean shutdown.</summary>
    Task StopAsync();
}

public sealed class McpHostService : IMcpHostService
{
    private readonly IAppSettingsService _settings;
    private readonly ILogService _log;
    private readonly IUiBridge _uiBridge;
    private readonly IExternalToolLocator _toolLocator;
    private readonly ITherionCompiler _compiler;
    private readonly IWorkspaceSession? _session;
    private readonly IUnsavedBufferProvider? _buffers;

    // Serializes start/stop transitions so a fast toggle or a settings change mid-start can't race.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _discoveryPath;
    private WebApplication? _app;
    private int? _port;
    private bool _disposed;

    private static readonly string Version =
        (typeof(McpHostService).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString();

    private static readonly JsonSerializerOptions DiscoveryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public McpHostService(
        IAppSettingsService settings,
        ILogService log,
        IUiBridge uiBridge,
        IExternalToolLocator toolLocator,
        ITherionCompiler compiler,
        IWorkspaceSession? session = null,
        IUnsavedBufferProvider? buffers = null)
        : this(settings, log, uiBridge, toolLocator, compiler, session, buffers, DiscoveryFilePath()) { }

    // Test seam (InternalsVisibleTo ThIDE.Tests): a discovery path under a temp dir keeps a test run
    // from writing or deleting the developer's real %AppData%/ThIDE/mcp-endpoint.json. Not seen by DI,
    // which only picks public constructors.
    internal McpHostService(
        IAppSettingsService settings,
        ILogService log,
        IUiBridge uiBridge,
        IExternalToolLocator toolLocator,
        ITherionCompiler compiler,
        IWorkspaceSession? session,
        IUnsavedBufferProvider? buffers,
        string discoveryPath)
    {
        _settings = settings;
        _log = log;
        _uiBridge = uiBridge;
        _toolLocator = toolLocator;
        _compiler = compiler;
        _session = session;
        _buffers = buffers;
        _discoveryPath = discoveryPath;
        _settings.Changed += OnSettingsChanged;
    }

    public bool IsListening => _app is not null;

    public int? Port => _port;

    public event EventHandler? StateChanged;

    /// <summary>Absolute path of the discovery file a shim/host reads to find the endpoint.</summary>
    public static string DiscoveryFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", "mcp-endpoint.json");
    }

    public async Task ApplySettingAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        bool wanted = _settings.Current.EnableMcpServer;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (wanted && _app is null) await StartCoreAsync(ct).ConfigureAwait(false);
            else if (!wanted && _app is not null) await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is not null) await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- lifecycle (caller holds the gate) -------------------------------------------------

    private async Task StartCoreAsync(CancellationToken ct)
    {
        int port = FindFreeLoopbackPort();
        string token = GenerateToken();

        try
        {
            // Empty Args so the Avalonia command line (opened file paths) can't reconfigure the host;
            // an explicit content root avoids surprises from the process working directory.
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppContext.BaseDirectory,
            });

            // Listen wins over UseUrls/ASPNETCORE_URLS, so the bind is authoritatively loopback-only.
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port));

            // Route only our own narration (tool calls at Information) into the Log pane; keep Kestrel
            // and hosting chatter to warnings so the audit trail stays readable.
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddProvider(new LogServiceLoggerProvider(_log));

            // App-owned singletons: registered as *instances*, which the child container never disposes,
            // so stopping the host cannot tear down services the IDE still uses. The tool locator is the
            // user's configured one, so run_build honours the same Therion path the Build pane does (D-029).
            builder.Services.AddSingleton(_uiBridge);   // presence => the R3 catalog is eligible
            builder.Services.AddSingleton(_toolLocator);
            builder.Services.AddSingleton(_compiler);

            // The workspace tools read the running IDE (T-03.2): the live session model with unsaved
            // buffers overlaid, marshalled onto the UI thread. Registered as an instance the child
            // container won't dispose (it wraps app-owned services). Without a session (a test of the
            // host shell itself) fall back to a disk-backed host seeded from the current root.
            if (_session is not null && _buffers is not null)
                builder.Services.AddSingleton<IWorkspaceHost>(
                    new LiveWorkspaceHost(_session, _buffers, _uiBridge));
            else
                builder.Services.AddSingleton<IWorkspaceHost>(_ => new WorkspaceHost(SeedWorkspacePath()));

            builder.Services
                .AddMcpServer(o => o.ServerInfo = new() { Name = "therion-mcp (in-app)", Version = Version })
                .WithHttpTransport()
                .AddTherionMcpTools(McpProfile.Full);

            var app = builder.Build();

            // Loopback is not a trust boundary on a shared machine, so every request must carry the token
            // (02 §B.6.1). This runs before the mapped MCP endpoints.
            app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (!IsAuthorized(context.Request, token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next(context).ConfigureAwait(false);
            });
            app.MapMcp();

            await app.StartAsync(ct).ConfigureAwait(false);

            _app = app;
            _port = port;
            WriteDiscoveryFile(port, token);
            _log.Info($"MCP server listening on http://127.0.0.1:{port}/ (bearer token required). "
                + $"Endpoint written to {_discoveryPath}.");
        }
        catch (Exception ex)
        {
            // A failed start must never crash the IDE; leave the flag on so the user can retry after
            // fixing the cause (e.g. a missing ASP.NET Core runtime, or the port being taken).
            _log.Error($"MCP server failed to start: {ex.Message}");
            await SafeDisposeAppAsync().ConfigureAwait(false);
            _app = null;
            _port = null;
        }
        RaiseStateChanged();
    }

    private async Task StopCoreAsync()
    {
        var app = _app;
        _app = null;
        _port = null;
        TryDeleteDiscoveryFile();
        if (app is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await app.StopAsync(timeout.Token).ConfigureAwait(false); } catch { /* shutting down */ }
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { /* shutting down */ }
        }
        _log.Info("MCP server stopped.");
        RaiseStateChanged();
    }

    // ---- helpers ----------------------------------------------------------------------------

    private string? SeedWorkspacePath()
    {
        try
        {
            var active = _session?.ActiveThconfig?.FullPath;
            if (!string.IsNullOrEmpty(active)) return active;
            var root = _session?.RootPath;
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch
        {
            return null;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // settings.Changed is synchronous; drive the (async) start/stop off the caller's thread.
        _ = ApplyFromEventAsync();
    }

    private async Task ApplyFromEventAsync()
    {
        try { await ApplySettingAsync().ConfigureAwait(false); }
        catch (Exception ex) { _log.Warning($"MCP server: applying the setting failed: {ex.Message}"); }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static bool IsAuthorized(HttpRequest request, string token)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(token);
        // Length is gated first (it isn't secret); the bytes are compared in constant time.
        return presented.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Reserves a free loopback TCP port, then releases it for Kestrel to bind.</summary>
    private static int FindFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private void WriteDiscoveryFile(int port, string token)
    {
        try
        {
            var info = new McpEndpointInfo(
                port, token, Environment.ProcessId,
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                $"http://127.0.0.1:{port}/");
            var dir = Path.GetDirectoryName(_discoveryPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_discoveryPath, JsonSerializer.Serialize(info, DiscoveryJson));
        }
        catch (Exception ex)
        {
            _log.Warning($"MCP server: could not write the discovery file: {ex.Message}");
        }
    }

    private void TryDeleteDiscoveryFile()
    {
        try
        {
            if (File.Exists(_discoveryPath)) File.Delete(_discoveryPath);
        }
        catch { /* best effort */ }
    }

    private async Task SafeDisposeAppAsync()
    {
        if (_app is null) return;
        try { await _app.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    // ---- ILogger → LogService bridge --------------------------------------------------------

    /// <summary>Forwards the MCP server's own log output (tool-call narration) into the in-app Log pane.</summary>
    private sealed class LogServiceLoggerProvider(ILogService log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Forwarder(log);
        public void Dispose() { }

        private sealed class Forwarder(ILogService log) : ILogger
        {
            private static readonly IDisposable Scope = new NullScope();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message)) return;
                var verbosity = logLevel switch
                {
                    LogLevel.Critical or LogLevel.Error => LogVerbosity.Error,
                    LogLevel.Warning => LogVerbosity.Warning,
                    _ => LogVerbosity.Info,
                };
                log.Log(verbosity, $"[MCP] {message}");
            }

            private sealed class NullScope : IDisposable { public void Dispose() { } }
        }
    }
}
