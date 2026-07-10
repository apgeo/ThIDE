using System.Reflection;
using ModelContextProtocol.Client;

namespace Therion.Mcp.Tests;

/// <summary>
/// Spawns the real <c>therion-mcp</c> executable over stdio and connects the SDK client to it.
/// This is the protocol-level regression guard: it exercises transport, handshake, schema
/// generation and serialization exactly as a host would.
/// </summary>
internal static class ServerHost
{
    /// <summary>Path to the built server assembly, injected by the csproj (TherionMcpServerDll).</summary>
    public static string ServerDll { get; } =
        typeof(ServerHost).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "TherionMcpServerDll").Value
        ?? throw new InvalidOperationException("TherionMcpServerDll assembly metadata is missing.");

    /// <param name="serverArgs">Arguments after the assembly path, e.g. <c>--workspace</c> and a path.</param>
    public static async Task<McpClient> ConnectAsync(CancellationToken ct = default, params string[] serverArgs)
    {
        Assert.True(File.Exists(ServerDll), $"Server not built at {ServerDll}. Build the solution first.");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "therion-mcp (test)",
            Command = "dotnet",
            Arguments = [ServerDll, .. serverArgs],
            // Surface server-side crashes in the test output instead of a silent handshake timeout.
            StandardErrorLines = line => Console.Error.WriteLine($"[therion-mcp] {line}"),
        });

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}
