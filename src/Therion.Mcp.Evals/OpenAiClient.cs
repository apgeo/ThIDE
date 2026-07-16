using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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
        McpClient mcp, IReadOnlyList<McpClientTool> tools, string prompt, int maxTurns, string contextMode, CancellationToken ct)
    {
        var engine = new ChatEngine(http, new ChatEngineOptions(endpoint, model) { MaxTurns = maxTurns });
        var session = new ChatSession(System);
        await InjectContextAsync(session, mcp, contextMode, ct);

        var result = await engine.RunAsync(session, prompt, new McpToolCatalog(mcp, tools), callbacks: null, ct);

        return new Conversation(
            result.FinalText,
            result.Calls.Select(c => new ToolCallRecord(c.Tool, c.SchemaValid, c.Ok)).ToList(),
            result.Turns,
            result.Tokens);
    }

    /// <summary>
    /// Mirrors the Assistant pane (CD-02): for card/pack, read the matching <c>therion://context</c>
    /// resource from the same server and add it as a second system message, so the A/B/C runs measure the
    /// exact context the pane would inject. A failed read or the no-workspace error envelope is ignored.
    /// </summary>
    private static async Task InjectContextAsync(ChatSession session, McpClient mcp, string contextMode, CancellationToken ct)
    {
        var uri = contextMode switch
        {
            "card" => "therion://context/card",
            "pack" => "therion://context/pack",
            _ => null,
        };
        if (uri is null) return;

        try
        {
            var result = await mcp.ReadResourceAsync(uri, cancellationToken: ct);
            var text = string.Concat(result.Contents.OfType<TextResourceContents>().Select(c => c.Text));
            if (!string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith('{'))
                session.AppendSystem(text);
        }
        catch
        {
            // Context is a nice-to-have; a case never fails because the digest couldn't be read.
        }
    }
}
