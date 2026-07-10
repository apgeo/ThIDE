// therion-mcp: a headless Model Context Protocol server exposing ThIDE's parser/semantics/workspace
// engines to any MCP host (Claude Code, LM Studio, an Ollama bridge). Speaks stdio; the host spawns
// it as a child process. Rings R1+R2 only — reaching the running IDE is the in-app host's job.
// Design: .claude/mcp-integration/02-server-architecture.md. Setup: docs/mcp-host-setup.md.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

static void PrintHelp()
{
    Console.WriteLine("therion-mcp — Therion project tools over the Model Context Protocol");
    Console.WriteLine();
    Console.WriteLine("An MCP host (Claude Code, LM Studio, an Ollama bridge) spawns this and talks");
    Console.WriteLine("to it over stdio. Running it by hand does nothing useful.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  therion-mcp [--workspace <path>] [--profile data|full]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --workspace <path>   Open this .thconfig, .th, or project folder up front.");
    Console.WriteLine("                       Without it, the model must call load_workspace first.");
    Console.WriteLine("  --profile <name>     data = read-only tools only; full (default) = everything,");
    Console.WriteLine("                       including the ones that write files and run Therion.");
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
