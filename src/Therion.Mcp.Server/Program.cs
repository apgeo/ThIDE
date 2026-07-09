// therion-mcp: a headless Model Context Protocol server exposing ThIDE's parser/semantics/workspace
// engines to any MCP host (Claude Code, LM Studio, an Ollama bridge). Speaks stdio; the host spawns
// it as a child process. Rings R1+R2 only — reaching the running IDE is the in-app host's job.
// Design: .claude/mcp-integration/02-server-architecture.md.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Therion.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the JSON-RPC frames: anything else written there corrupts the session.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "therion-mcp", Version = ServerVersion() })
    .WithStdioServerTransport()
    .AddTherionMcpTools();

await builder.Build().RunAsync();
return 0;

// Informational version minus the "+commitsha" suffix MSBuild appends.
static string ServerVersion()
{
    var raw = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    int plus = raw.IndexOf('+');
    return plus < 0 ? raw : raw[..plus];
}
