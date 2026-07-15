using ModelContextProtocol.Client;

namespace Therion.Mcp.Evals;

/// <summary>
/// Measures the token cost of the tool catalog's schemas — no model, no workspace (tools are advertised
/// regardless of what's loaded). Spawns the server at a profile, lists the tools, and prints per-tool and
/// total approximate tokens (name + description + input schema, the shape that lands in the request's
/// tool list). This turns the TOOL-REGISTRY "cost column" from estimate into measurement, and it's the
/// static half of CAP-02.3 (the dynamic half is the None/Card/Pack A/B/C runs).
/// </summary>
public static class SchemaCost
{
    public static async Task<bool> RunAsync(string serverDll, string profile, CancellationToken ct)
    {
        try
        {
            await using var client = await ConnectAsync(serverDll, profile, ct);
            var tools = (await client.ListToolsAsync(cancellationToken: ct))
                .Select(t => (t.Name, Tokens: TokenEstimator.EstimateTool(t.Name, t.Description, t.JsonSchema.GetRawText())))
                .OrderByDescending(t => t.Tokens)
                .ToList();

            Console.WriteLine($"Tool-schema cost — profile '{profile}', {tools.Count} tools (approx, ~4 chars/token):\n");
            foreach (var (name, tokens) in tools)
                Console.WriteLine($"  {name,-28} {tokens,6}");
            Console.WriteLine($"\n  {"TOTAL",-28} {tools.Sum(t => t.Tokens),6}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"schema-cost failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<McpClient> ConnectAsync(string serverDll, string profile, CancellationToken ct) =>
        await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "therion-mcp (schema-cost)",
            Command = "dotnet",
            Arguments = [serverDll, "--profile", profile],
            StandardErrorLines = _ => { },
        }), cancellationToken: ct);
}
