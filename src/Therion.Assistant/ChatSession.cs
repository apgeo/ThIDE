using System.Text.Json.Nodes;

namespace Therion.Assistant;

/// <summary>
/// One conversation's history, in the OpenAI chat shape (system + user/assistant/tool turns).
/// <see cref="ChatEngine.RunAsync"/> appends to it as it works, so calling it again with the same
/// session continues the conversation. Not thread-safe: one run at a time per session.
/// </summary>
public sealed class ChatSession(string systemPrompt)
{
    internal JsonArray Messages { get; } = [ChatEngine.Message("system", systemPrompt)];

    /// <summary>Messages so far, system prompt included — a length signal for "new chat" nudges.</summary>
    public int Length => Messages.Count;
}
