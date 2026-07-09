using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Therion.Core;

namespace Therion.Mcp.Tools;

/// <summary>What <c>server_info</c> reports. Workspace state is added in T-01.2 with WorkspaceHost.</summary>
public sealed record ServerInfo(
    string Name,
    string Version,
    string SyntaxVersion,
    bool UiBridge);

/// <summary>
/// Ring R1. The handshake probe: proves transport, DI, schema generation and serialization all work,
/// and tells a host which flavour of the server it just connected to (headless vs in-app).
/// </summary>
[McpServerToolType]
public static class ServerInfoTool
{
    [McpServerTool(Name = "server_info", Title = "Server info", ReadOnly = true, Idempotent = true)]
    [Description("Identity of this Therion MCP server: its version, the Therion syntax version it "
               + "validates against, and whether it is attached to a running ThIDE window (uiBridge).")]
    public static ToolResult<ServerInfo> GetServerInfo(IUiBridge uiBridge)
    {
        var syntax = TherionSyntaxVersion.Default;
        return ToolResult<ServerInfo>.Success(new ServerInfo(
            Name: "therion-mcp",
            Version: AssemblyVersion,
            SyntaxVersion: $"{syntax.Major}.{syntax.Minor}.{syntax.Patch}",
            UiBridge: uiBridge.IsAvailable));
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
