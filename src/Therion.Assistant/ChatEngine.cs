using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Therion.Assistant;

/// <summary>
/// A minimal OpenAI-compatible chat client + tool loop, deliberately dependency-free (D-041,
/// D-043): LM Studio and Ollama both expose <c>POST {endpoint}/chat/completions</c> with the
/// OpenAI <c>tools</c>/<c>tool_calls</c> shape, so the request is hand-rolled rather than pulling
/// an SDK into the tree. Tools are advertised verbatim from the catalog (their schema is already
/// JSON Schema); each tool_call the model makes is screened (real name? parseable arguments? —
/// the schema-validity signal that catches a hallucinated tool), optionally gated by the approval
/// hook, executed, and its result fed back — until the model answers in plain text or the turn
/// budget runs out.
/// </summary>
public sealed class ChatEngine(HttpClient http, ChatEngineOptions options)
{
    /// <summary>
    /// Runs one user turn to completion, appending everything to <paramref name="session"/> so the
    /// next turn continues the conversation. Throws <see cref="OperationCanceledException"/> on
    /// cancellation — with the session left well-formed (pending tool calls get a "(cancelled)"
    /// result), so the conversation survives a Stop. Throws <see cref="InvalidOperationException"/>
    /// when the endpoint answers with a non-success status.
    /// </summary>
    public async Task<ChatResult> RunAsync(
        ChatSession session,
        string userMessage,
        IToolCatalog catalog,
        ChatCallbacks? callbacks = null,
        CancellationToken ct = default)
    {
        var byName = catalog.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var toolSpec = new JsonArray(catalog.Tools.Select(ToOpenAiTool).ToArray());

        session.Messages.Add(Message("user", userMessage));

        var calls = new List<ChatToolCall>();
        int turns = 0, tokens = 0;

        for (int i = 0; i <= options.MaxTurns; i++)
        {
            var response = await CompleteAsync(session.Messages, toolSpec, ct);
            tokens += (int?)response?["usage"]?["total_tokens"] ?? 0;

            var message = response?["choices"]?[0]?["message"];
            if (message is null)
                return Finish("(no response from the endpoint)", calls, turns, tokens, callbacks);

            var toolCalls = message["tool_calls"] as JsonArray;
            if (toolCalls is null || toolCalls.Count == 0)
            {
                var text = (string?)message["content"] ?? "";
                session.Messages.Add(Message("assistant", text));
                return Finish(text, calls, turns, tokens, callbacks);
            }

            // Echo the assistant turn (with its tool_calls) before the tool results — the protocol
            // requires it.
            session.Messages.Add(message.DeepClone());
            turns++;

            // Every advertised tool_call must get a result message, even on cancellation — a
            // dangling tool_call would poison the session for the *next* turn.
            var pending = new Queue<(string Id, string Name, string Args)>();
            foreach (var call in toolCalls.OfType<JsonObject>())
                pending.Enqueue((
                    (string?)call["id"] ?? "",
                    (string?)call["function"]?["name"] ?? "",
                    (string?)call["function"]?["arguments"] ?? "{}"));

            try
            {
                while (pending.Count > 0)
                {
                    var (id, name, argsText) = pending.Peek();
                    var (content, record) = await ExecuteAsync(byName, catalog, callbacks, name, argsText, ct);
                    calls.Add(record);
                    session.Messages.Add(ToolResultMessage(id, content));
                    pending.Dequeue();
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var (id, _, _) in pending)
                    session.Messages.Add(ToolResultMessage(id, "(cancelled by the user)"));
                throw;
            }
        }

        return Finish($"(gave up after {options.MaxTurns} tool turns)", calls, turns, tokens, callbacks);
    }

    private static ChatResult Finish(
        string text, List<ChatToolCall> calls, int turns, int tokens, ChatCallbacks? callbacks)
    {
        callbacks?.OnUpdate?.Invoke(new AssistantAnswered(text));
        return new ChatResult(text, calls, turns, tokens);
    }

    private async Task<(string Content, ChatToolCall Record)> ExecuteAsync(
        Dictionary<string, ToolDescriptor> byName,
        IToolCatalog catalog,
        ChatCallbacks? callbacks,
        string name,
        string argsText,
        CancellationToken ct)
    {
        // Screening failures are answers to the model, not exceptions — and they are the
        // call-validity signal: a call that fails screening is recorded as not schema-valid.
        if (!byName.TryGetValue(name, out var descriptor))
            return Screened(callbacks, new ToolCallInfo(name, argsText, ReadOnly: false),
                $"Error: no such tool '{name}'.");

        var info = new ToolCallInfo(name, argsText, descriptor.ReadOnly);

        IReadOnlyDictionary<string, object?> args;
        try
        {
            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                string.IsNullOrWhiteSpace(argsText) ? "{}" : argsText) ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return Screened(callbacks, info, "Error: arguments were not valid JSON.");
        }

        callbacks?.OnUpdate?.Invoke(new ToolCallStarted(info));

        if (!descriptor.ReadOnly
            && callbacks?.ApproveAsync is { } approve
            && !await approve(info, ct))
        {
            const string declined = "The user declined this action.";
            callbacks?.OnUpdate?.Invoke(new ToolCallFinished(info, Ok: false, declined));
            return (declined, new ChatToolCall(name, SchemaValid: true, Ok: false, Declined: true));
        }

        try
        {
            var outcome = await catalog.CallAsync(name, args, ct);
            var content = outcome.Content.Length == 0 ? "(no content)" : outcome.Content;
            callbacks?.OnUpdate?.Invoke(new ToolCallFinished(info, outcome.Ok, content));
            return (content, new ChatToolCall(name, SchemaValid: true, Ok: outcome.Ok));
        }
        catch (OperationCanceledException)
        {
            throw; // RunAsync backfills the pending results and rethrows.
        }
        catch (Exception ex)
        {
            // The call was well-formed but the catalog rejected it — still schema-valid.
            var content = $"Error: {ex.Message}";
            callbacks?.OnUpdate?.Invoke(new ToolCallFinished(info, Ok: false, content));
            return (content, new ChatToolCall(name, SchemaValid: true, Ok: false));
        }
    }

    private static (string, ChatToolCall) Screened(ChatCallbacks? callbacks, ToolCallInfo info, string error)
    {
        callbacks?.OnUpdate?.Invoke(new ToolCallFinished(info, Ok: false, error));
        return (error, new ChatToolCall(info.Tool, SchemaValid: false, Ok: false));
    }

    private async Task<JsonNode?> CompleteAsync(JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages.DeepClone(),
            ["temperature"] = options.Temperature,
            ["stream"] = false,
        };
        if (tools.Count > 0)
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = "auto";
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{options.Endpoint.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        if (!string.IsNullOrEmpty(options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            if (detail.Length > 300) detail = detail[..300] + "…";
            throw new InvalidOperationException($"endpoint returned {(int)response.StatusCode}: {detail}");
        }
        return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    private static JsonObject ToOpenAiTool(ToolDescriptor tool) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = JsonNode.Parse(tool.ParametersJson),
        },
    };

    internal static JsonObject Message(string role, string content) =>
        new() { ["role"] = role, ["content"] = content };

    private static JsonObject ToolResultMessage(string id, string content) =>
        new() { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = content };
}
