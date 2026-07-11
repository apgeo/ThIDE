// therion-mcp: a headless Model Context Protocol server exposing ThIDE's parser/semantics/workspace
// engines to any MCP host (Claude Code, LM Studio, an Ollama bridge). Speaks stdio; the host spawns
// it as a child process. Rings R1+R2 only — reaching the running IDE is the in-app host's job.
// Design: .claude/mcp-integration/02-server-architecture.md. Setup: docs/mcp-host-setup.md.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Therion.Mcp;

if (args is ["-h"] or ["--help"])
{
    PrintHelp();
    return 0;
}

if (args is ["--version"])
{
    Console.WriteLine(ServerVersion());
    return 0;
}

// --connect [path]: don't serve our own R1/R2 catalog — bridge this stdio to the *running* IDE's HTTP
// server, so a stdio-only host (LM Studio) reaches the live app (its R3 tools included). Resolves Q-08.
if (Array.IndexOf(args, "--connect") >= 0)
    return await RunConnectAsync(GetOption(args, "--connect"));

var workspacePath = GetOption(args, "--workspace");
if (workspacePath is { Length: 0 })
{
    Console.Error.WriteLine("error: --workspace needs a path.");
    return 2;
}

var profile = McpProfile.Full;
if (GetOption(args, "--profile") is { } requested)
{
    if (!Enum.GetNames<McpProfile>().Any(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"error: unknown profile '{requested}'. Use data or full.");
        return 2;
    }
    profile = Enum.Parse<McpProfile>(requested, ignoreCase: true);
}

// Fail at startup rather than answering `workspace_not_loaded` to every call for the rest of the
// session: a typo in an mcp.json is otherwise near-invisible.
if (workspacePath is not null && !File.Exists(workspacePath) && !Directory.Exists(workspacePath))
{
    Console.Error.WriteLine($"error: no such workspace: {workspacePath}");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the JSON-RPC frames: anything else written there corrupts the session.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// The SDK narrates every tool call at Information, and the host shows our stderr to the user, so
// quiet it down. SetMinimumLevel would win over configuration, hence the check: setting
// Logging__LogLevel__Default=Information still turns the narration back on for a debugging session.
if (builder.Configuration["Logging:LogLevel:Default"] is null)
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Registered before AddTherionMcpTools, whose TryAddSingleton then leaves it alone. The workspace
// opens lazily on the first tool call that needs it, so startup stays fast.
if (workspacePath is not null)
    builder.Services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(workspacePath));

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "therion-mcp", Version = ServerVersion() })
    .WithStdioServerTransport()
    .AddTherionMcpTools(profile);

await builder.Build().RunAsync();
return 0;

// Bridges this process's stdio (facing the spawning host) to the running IDE's loopback HTTP MCP
// server, found via the discovery file. A transparent JSON-RPC pump — no catalog of its own.
static async Task<int> RunConnectAsync(string? discoveryArg)
{
    var path = string.IsNullOrEmpty(discoveryArg) ? McpEndpoint.DefaultPath() : discoveryArg;
    var endpoint = McpEndpoint.TryRead(path);
    if (endpoint is null)
    {
        Console.Error.WriteLine($"error: no running ThIDE MCP server found ({path}).");
        Console.Error.WriteLine(
            "Start ThIDE and turn on Preferences ▸ MCP (Enable the in-app AI tools server), then retry.");
        return 3;
    }

    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };

    // stdout is the JSON-RPC channel to the host — logs go to stderr only, quiet by default.
    using var loggerFactory = LoggerFactory.Create(b => b
        .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
        .SetMinimumLevel(LogLevel.Warning));

    var httpTransport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(endpoint.Url),
        TransportMode = HttpTransportMode.AutoDetect,
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {endpoint.Token}" },
        Name = "therion-mcp --connect",
    }, loggerFactory);

    ITransport upstream;
    try
    {
        upstream = await httpTransport.ConnectAsync(shutdown.Token);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: could not reach the ThIDE MCP server at {endpoint.Url}: {ex.Message}");
        Console.Error.WriteLine("Is ThIDE still running with the MCP server enabled?");
        return 4;
    }

    await using (upstream)
    await using (var downstream = new StdioServerTransport("therion-mcp (connect)", loggerFactory))
        await TransportRelay.RunAsync(downstream, upstream, shutdown.Token);

    return 0;
}

static void PrintHelp()
{
    Console.WriteLine("therion-mcp — Therion project tools over the Model Context Protocol");
    Console.WriteLine();
    Console.WriteLine("An MCP host (Claude Code, LM Studio, an Ollama bridge) spawns this and talks");
    Console.WriteLine("to it over stdio. Running it by hand does nothing useful.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  therion-mcp [--workspace <path>] [--profile data|full]");
    Console.WriteLine("  therion-mcp --connect [<discovery-file>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --workspace <path>   Open this .thconfig, .th, or project folder up front.");
    Console.WriteLine("                       Without it, the model must call load_workspace first.");
    Console.WriteLine("  --profile <name>     data = read-only tools only; full (default) = everything,");
    Console.WriteLine("                       including the ones that write files and run Therion.");
    Console.WriteLine("  --connect [<path>]   Instead of serving, bridge stdio to the RUNNING ThIDE's");
    Console.WriteLine("                       in-app server, so a stdio-only host reaches the live app");
    Console.WriteLine("                       (its UI tools included). Reads the discovery file written");
    Console.WriteLine("                       by Preferences ▸ MCP; pass a path to override its location.");
    Console.WriteLine("  --version            Print the server version.");
    Console.WriteLine("  -h, --help           Show this help.");
    Console.WriteLine();
    Console.WriteLine("See docs/mcp-host-setup.md for host configuration.");
}

/// <summary>The value after <paramref name="name"/>, "" when it is last, or null when absent.</summary>
static string? GetOption(string[] args, string name)
{
    int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.Ordinal));
    if (i < 0) return null;
    return i + 1 < args.Length ? args[i + 1] : "";
}

// Informational version minus the "+commitsha" suffix MSBuild appends.
static string ServerVersion()
{
    var raw = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    int plus = raw.IndexOf('+');
    return plus < 0 ? raw : raw[..plus];
}
