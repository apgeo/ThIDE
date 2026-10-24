// Opt-in UI-automation server for end-to-end tests and coding agents. Started only when the process
// is launched with THIDE_UI_AUTOMATION=1, so a normal run never opens a listener. Same shape as the
// project MCP host (loopback-only Kestrel, bearer token, owner-only discovery file) but it serves the
// ui_* catalog, which can press any button, so it is never exposed through a user setting.
//
//   THIDE_UI_AUTOMATION=1                 turn it on
//   THIDE_UI_AUTOMATION_PORT=47800        fixed port (default: a free one)
//   THIDE_UI_AUTOMATION_TOKEN=...         fixed bearer token (default: random)
//   THIDE_UI_AUTOMATION_ENDPOINT=path     discovery file (default: <app data>/ThIDE/ui-automation-endpoint.json)

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Therion.Mcp;

namespace ThIDE.Services.UiAutomation;

public static class UiAutomationHost
{
    public const string EnableVariable = "THIDE_UI_AUTOMATION";
    public const string PortVariable = "THIDE_UI_AUTOMATION_PORT";
    public const string TokenVariable = "THIDE_UI_AUTOMATION_TOKEN";
    public const string EndpointVariable = "THIDE_UI_AUTOMATION_ENDPOINT";

    private static WebApplication? s_app;
    private static string? s_discoveryPath;

    public static bool IsRequested => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    public static string DiscoveryFilePath() =>
        Environment.GetEnvironmentVariable(EndpointVariable) is { Length: > 0 } p
            ? Path.GetFullPath(p)
            : Path.Combine(Path.GetDirectoryName(McpEndpoint.DefaultPath())!, "ui-automation-endpoint.json");

    /// <summary>Starts the server if the environment asks for it. Never throws: a failure is logged.</summary>
    public static async Task StartIfRequestedAsync(ILogService log)
    {
        if (!IsRequested || s_app is not null) return;
        WebApplication? app = null;
        try
        {
            int port = int.TryParse(Environment.GetEnvironmentVariable(PortVariable), NumberStyles.None,
                CultureInfo.InvariantCulture, out var fixedPort) ? fixedPort : McpHostService.FindFreeLoopbackPort();
            string token = Environment.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } t
                ? t : McpHostService.GenerateToken();

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port));
            builder.Logging.ClearProviders();
            builder.Services
                .AddMcpServer(o => o.ServerInfo = new() { Name = "thide-ui-automation", Version = "1" })
                .WithHttpTransport()
                .WithTools<UiAutomationTools>();

            app = builder.Build();
            app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (!McpHostService.IsAuthorized(context.Request, token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next(context).ConfigureAwait(false);
            });
            app.MapMcp();
            await app.StartAsync().ConfigureAwait(false);
            s_app = app;

            var endpoint = new McpEndpoint(port, token, Environment.ProcessId,
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture), $"http://127.0.0.1:{port}/");
            s_discoveryPath = DiscoveryFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(s_discoveryPath)!);
            File.WriteAllText(s_discoveryPath, endpoint.ToJson());
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(s_discoveryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            log.Warning($"UI automation server listening on {endpoint.Url} ({EnableVariable}=1); endpoint in {s_discoveryPath}.");
        }
        catch (Exception ex)
        {
            log.Error($"UI automation server failed to start: {ex.Message}");
            if (app is not null)
            {
                try { await app.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>Removes the discovery file and stops the listener without blocking the caller.</summary>
    public static void RequestShutdown()
    {
        try { if (s_discoveryPath is not null && File.Exists(s_discoveryPath)) File.Delete(s_discoveryPath); }
        catch { /* best effort */ }
        var app = s_app;
        s_app = null;
        if (app is not null) _ = Task.Run(async () => { try { await app.StopAsync().ConfigureAwait(false); } catch { } });
    }
}
