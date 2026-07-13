using ModelContextProtocol.Client;
using Therion.Assistant;

namespace Therion.Mcp.Evals;

/// <param name="FinalText">The model's last assistant message (no tool calls).</param>
/// <param name="Calls">Every tool call attempted, in order.</param>
/// <param name="Turns">Model↔tool round-trips.</param>
/// <param name="Tokens">Total tokens the endpoint reported (0 if it didn't).</param>
public sealed record Conversation(string FinalText, IReadOnlyList<ToolCallRecord> Calls, int Turns, int Tokens);

/// <summary>
/// The harness's face of the chat loop. The loop itself lives in <c>Therion.Assistant</c>
/// (<see cref="ChatEngine"/> — extracted at T-07.1 so the Assistant pane and this harness share
/// one implementation, D-043); this adapter keeps the harness's fresh-conversation-per-case shape
/// and its <see cref="ToolCallRecord"/> vocabulary for the grader. No approval hook: the eval runs
/// unattended, which is the pre-extraction behaviour.
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
        var engine = new ChatEngine(http, new ChatEngineOptions(endpoint, model) { MaxTurns = maxTurns });
        var result = await engine.RunAsync(
            new ChatSession(System), prompt, new McpToolCatalog(mcp, tools), callbacks: null, ct);

        return new Conversation(
            result.FinalText,
            result.Calls.Select(c => new ToolCallRecord(c.Tool, c.SchemaValid, c.Ok)).ToList(),
            result.Turns,
            result.Tokens);
    }
}
