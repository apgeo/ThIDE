using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Therion.Core;

namespace Therion.Mcp.Tools;

/// <param name="UiBridge">True when this server is attached to a running ThIDE window (ring R3 available).</param>
/// <param name="WorkspaceLoaded">
/// Whether a workspace is open <em>right now</em>. A server started with <c>--workspace</c> reports
/// false until the first tool call opens it; <c>workspace_info</c> is what opens it.
/// </param>
public sealed record ServerInfo(
    string Name,
    string Version,
    string SyntaxVersion,
    bool UiBridge,
    bool WorkspaceLoaded,
    string? WorkspaceRoot);

/// <summary>
/// Ring R1. The handshake probe: proves transport, DI, schema generation and serialization all work,
/// and tells a host which flavour of the server it just connected to (headless vs in-app).
/// </summary>
[McpServerToolType]
public sealed class ServerInfoTool(IWorkspaceHost host, IUiBridge uiBridge)
{
    [McpServerTool(Name = "server_info", Title = "Server info", ReadOnly = true, Idempotent = true)]
    [Description("Identity of this Therion MCP server: its version, the Therion syntax version it "
               + "validates against, whether it is attached to a running ThIDE window (uiBridge), "
               + "and whether a workspace is open yet. Call workspace_info for the project itself — "
               + "that is also what opens a workspace given with --workspace.")]
    public ToolResult<ServerInfo> GetServerInfo()
    {
        var syntax = TherionSyntaxVersion.Default;
        return ToolResult<ServerInfo>.Success(new ServerInfo(
            Name: "therion-mcp",
            Version: AssemblyVersion,
            SyntaxVersion: $"{syntax.Major}.{syntax.Minor}.{syntax.Patch}",
            UiBridge: uiBridge.IsAvailable,
            WorkspaceLoaded: host.IsLoaded,
            WorkspaceRoot: host.Root));
    }

    /// <summary>Informational version without the <c>+commitsha</c> suffix MSBuild appends.</summary>
    private static string AssemblyVersion
    {
        get
        {
            var raw = typeof(ServerInfoTool).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(ServerInfoTool).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            int plus = raw.IndexOf('+');
            return plus < 0 ? raw : raw[..plus];
        }
    }
}
