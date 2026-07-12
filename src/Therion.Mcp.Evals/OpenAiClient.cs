using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Evals;

/// <param name="FinalText">The model's last assistant message (no tool calls).</param>
/// <param name="Calls">Every tool call attempted, in order.</param>
/// <param name="Turns">Model↔tool round-trips.</param>
/// <param name="Tokens">Total tokens the endpoint reported (0 if it didn't).</param>
public sealed record Conversation(string FinalText, IReadOnlyList<ToolCallRecord> Calls, int Turns, int Tokens);

/// <summary>
/// A minimal OpenAI-compatible chat client + tool loop, deliberately dependency-free: LM Studio and Ollama
/// both expose <c>POST {endpoint}/chat/completions</c> with the OpenAI <c>tools</c>/<c>tool_calls</c> shape,
/// so we hand-roll the request rather than pull the OpenAI SDK into the tree. The MCP tools are advertised
/// verbatim (their JSON schema is already JSON Schema); each tool_call the model makes is executed against
/// the real MCP server and its result fed back. A call that names an unknown tool or sends unparseable
/// arguments is recorded as <em>not schema-valid</em> — that is the call_validity signal, and it is how a
/// hallucinated tool is caught.
/// </summary>
public sealed class OpenAiClient(HttpClient http, string endpoint, string model)
{
    private const string System =
        "You are an assistant for Therion cave-survey projects. Use the provided tools to inspect and modify "
        + "the project; do not guess. When a task asks for a change, apply it. When you have the answer, reply "
        + "directly and concisely.";

    public async Task<Conversation> RunAsync(
        McpClient mcp, IReadOnlyList<McpClientTool> tools, string prompt, int maxTurns, CancellationToken ct)
    {
        var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var toolSpec = new JsonArray(tools.Select(ToOpenAiTool).ToArray());

        var messages = new JsonArray
        {
            Message("system", System),
            Message("user", prompt),
        };

        var calls = new List<ToolCallRecord>();
        int turns = 0, tokens = 0;

        for (int i = 0; i <= maxTurns; i++)
        {
            var response = await CompleteAsync(messages, toolSpec, ct);
            tokens += (int?)response?["usage"]?["total_tokens"] ?? 0;

            var message = response?["choices"]?[0]?["message"];
            if (message is null) return new Conversation("(no response from the endpoint)", calls, turns, tokens);

            var toolCalls = message["tool_calls"] as JsonArray;
            if (toolCalls is null || toolCalls.Count == 0)
                return new Conversation((string?)message["content"] ?? "", calls, turns, tokens);

            // Echo the assistant turn (with its tool_calls) before the tool results — the protocol requires it.
            messages.Add(message.DeepClone());
            turns++;

            foreach (var call in toolCalls.OfType<JsonObject>())
            {
                var id = (string?)call["id"] ?? "";
                var fn = call["function"];
                var name = (string?)fn?["name"] ?? "";
                var argsText = (string?)fn?["arguments"] ?? "{}";

                var (content, record) = await ExecuteAsync(mcp, toolNames, name, argsText, ct);
                calls.Add(record);
                messages.Add(ToolResultMessage(id, content));
            }
        }

        return new Conversation($"(gave up after {maxTurns} tool turns)", calls, turns, tokens);
    }

    private async Task<(string Content, ToolCallRecord Record)> ExecuteAsync(
        McpClient mcp, HashSet<string> known, string name, string argsText, CancellationToken ct)
    {
        if (!known.Contains(name))
            return ($"Error: no such tool '{name}'.", new ToolCallRecord(name, SchemaValid: false, Ok: false));

        Dictionary<string, object> args;
        try
        {
            args = JsonSerializer.Deserialize<Dictionary<string, object>>(
                string.IsNullOrWhiteSpace(argsText) ? "{}" : argsText) ?? new();
        }
        catch (JsonException)
        {
            return ("Error: arguments were not valid JSON.", new ToolCallRecord(name, SchemaValid: false, Ok: false));
        }

        try
        {
            var result = await mcp.CallToolAsync(name, args, cancellationToken: ct);
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
            return (text.Length == 0 ? "(no content)" : text, new ToolCallRecord(name, SchemaValid: true, Ok: result.IsError != true));
        }
        catch (Exception ex)
        {
            // The call was well-formed (schema-valid) but the server rejected it — still counts as a valid call.
            return ($"Error: {ex.Message}", new ToolCallRecord(name, SchemaValid: true, Ok: false));
        }
    }

    private async Task<JsonNode?> CompleteAsync(JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone(),
            ["tool_choice"] = "auto",
            ["temperature"] = 0,
            ["stream"] = false,
        };

        using var response = await http.PostAsJsonAsync($"{endpoint.TrimEnd('/')}/chat/completions", body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    private static JsonObject ToOpenAiTool(McpClientTool tool) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText()),
        },
    };

    private static JsonObject Message(string role, string content) => new() { ["role"] = role, ["content"] = content };

    private static JsonObject ToolResultMessage(string id, string content) =>
        new() { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = content };
}
