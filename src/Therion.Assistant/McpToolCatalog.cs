using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Assistant;

/// <summary>
/// The real catalog: adapts a connected MCP client (the in-app loopback server for the Assistant
/// pane, a spawned stdio server for the eval harness) to the engine's seam. The one MCP-aware
/// type in this library (D-003: SDK types stay at the host edge).
/// </summary>
public sealed class McpToolCatalog : IToolCatalog
{
    private readonly McpClient _client;

    /// <param name="tools">The client's tool list (<c>ListToolsAsync</c>), advertised verbatim —
    /// the schema is already JSON Schema. A tool with no <c>readOnlyHint</c> counts as writing,
    /// which is the safe direction for the approval gate.</param>
    public McpToolCatalog(McpClient client, IReadOnlyList<McpClientTool> tools)
    {
        _client = client;
        Tools = tools
            .Select(t => new ToolDescriptor(
                t.Name,
                t.Description,
                t.JsonSchema.GetRawText(),
                ReadOnly: t.ProtocolTool.Annotations?.ReadOnlyHint == true))
            .ToList();
    }

    public IReadOnlyList<ToolDescriptor> Tools { get; }

    public async Task<ToolOutcome> CallAsync(
        string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var result = await _client.CallToolAsync(name, arguments, cancellationToken: ct);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        return new ToolOutcome(text, Ok: result.IsError != true);
    }
}
