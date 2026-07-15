using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Evals;

/// <summary>
/// Spawns the server on each fixture and prints the numbers the graders will compute — no model involved.
/// It is how a fixture author confirms, before an expensive run, that a workspace actually parses and
/// produces the diagnostics/graph the cases assume (e.g. the "broken" fixture really has an error, the
/// "disconnected" one really floats a component). Also exercises the whole MCP client path end to end.
/// </summary>
public static class Prober
{
    public static async Task<bool> RunAsync(string serverDll, string workspacesDir, CancellationToken ct)
    {
        bool ok = true;
        foreach (var name in EvalSuite.Cases.Select(c => c.Workspace).Distinct().Order())
        {
            var dir = Path.Combine(workspacesDir, name);
            try
            {
                await using var client = await ConnectAsync(serverDll, dir, ct);
                var errors = await NumberAsync(client, "get_diagnostics", new() { ["minSeverity"] = "error" }, "total", ct);
                var stations = await NumberAsync(client, "survey_graph", new(), "stations", ct);
                var floating = await NumberAsync(client, "survey_graph", new(), "floatingComponents", ct);
                Console.WriteLine($"  {name,-14} errors={errors}  stations={stations}  floatingComponents={floating}");
            }
            catch (Exception ex)
            {
                ok = false;
                Console.WriteLine($"  {name,-14} PROBE FAILED — {ex.Message}");
            }
        }
        return ok;
    }

    private static async Task<McpClient> ConnectAsync(string serverDll, string workspaceDir, CancellationToken ct) =>
        await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "therion-mcp (probe)",
            Command = "dotnet",
            // Point at cave.thconfig explicitly (as EvalRunner does): a fixture can carry several
            // thconfigs (question 8), which makes opening the bare directory ambiguous.
            Arguments = [serverDll, "--workspace", Path.Combine(workspaceDir, "cave.thconfig"), "--profile", "full"],
            StandardErrorLines = _ => { },
        }), cancellationToken: ct);

    private static async Task<string> NumberAsync(
        McpClient client, string tool, Dictionary<string, object> args, string field, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: ct);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty(field, out var v)
            ? v.GetRawText() : "?";
    }
}
