using System.Text.Json;
using System.Text.Json.Nodes;

namespace Therion.Assistant;

/// <summary>
/// One conversation's history, in the OpenAI chat shape (system + user/assistant/tool turns).
/// <see cref="ChatEngine.RunAsync"/> appends to it as it works, so calling it again with the same
/// session continues the conversation. Not thread-safe: one run at a time per session.
/// </summary>
public sealed class ChatSession
{
    public ChatSession(string systemPrompt)
        : this([ChatEngine.Message("system", systemPrompt)]) { }

    private ChatSession(JsonArray messages) => Messages = messages;

    internal JsonArray Messages { get; }

    /// <summary>Messages so far, system prompt included — a length signal for "new chat" nudges.</summary>
    public int Length => Messages.Count;

    /// <summary>
    /// Adds a second system message at the head of a fresh conversation — the workspace context
    /// card (CD-02). Kept separate from the persona prompt so the cached persona stays stable and a
    /// New-Chat refresh replaces only the card. No-op for blank text.
    /// </summary>
    public void AppendSystem(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            Messages.Add(ChatEngine.Message("system", text));
    }

    /// <summary>The full history as JSON, for persisting the conversation across restarts.</summary>
    public string Serialize() => Messages.ToJsonString();

    /// <summary>
    /// Rebuilds a session from <see cref="Serialize"/> output, or null when the text is missing,
    /// unparseable, or not a non-empty message array. Never throws.
    /// </summary>
    public static ChatSession? Restore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) is JsonArray { Count: > 0 } messages
                ? new ChatSession(messages)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The visible dialogue — the user's messages and the assistant's written answers, in order.
    /// Skips the system prompt, tool-call turns, and tool results (all invisible plumbing), so the
    /// pane can redraw the conversation the user actually saw.
    /// </summary>
    public IReadOnlyList<ConversationTurn> Dialogue()
    {
        var turns = new List<ConversationTurn>();
        foreach (var message in Messages)
        {
            if (message is not JsonObject obj) continue;
            var role = (string?)obj["role"];
            // An assistant turn that only carried tool_calls has no visible text.
            var text = (string?)obj["content"];
            switch (role)
            {
                case "user" when !string.IsNullOrWhiteSpace(text):
                    turns.Add(new ConversationTurn(IsUser: true, text!));
                    break;
                case "assistant" when !string.IsNullOrWhiteSpace(text) && obj["tool_calls"] is null:
                    turns.Add(new ConversationTurn(IsUser: false, text!));
                    break;
            }
        }
        return turns;
    }
}
