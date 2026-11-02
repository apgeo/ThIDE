using System.Text.Json.Nodes;
using Therion.Assistant;

namespace Therion.Assistant.Tests;

/// <summary>
/// Whatever ends a run after the assistant's tool_calls turn has been echoed into the session, the
/// session must stay well-formed: every advertised tool_call id needs a tool result, or the next
/// turn is rejected by any endpoint that validates tool pairing. Cancellation already honours this;
/// these cases cover the other ways the batch can be abandoned.
/// </summary>
public sealed class SessionIntegrityOnFailureTests
{
    [Fact]
    public async Task ToolCallWithNumericId_LeavesEveryToolCallAnswered()
    {
        var handler = new ScriptedHandler(
            Reply("""[{"id":7,"type":"function","function":{"name":"t","arguments":"{}"}}]"""),
            FinalAnswer("ok"));
        var catalog = new FakeCatalog(Tool("t"));
        var session = new ChatSession("s");

        await Record.ExceptionAsync(() => Engine(handler).RunAsync(session, "go", catalog));

        AssertEveryToolCallAnswered(session);
    }

    [Fact]
    public async Task ToolCallWithObjectArguments_LeavesEveryToolCallAnswered()
    {
        var handler = new ScriptedHandler(
            Reply("""[{"id":"c1","type":"function","function":{"name":"t","arguments":{"limit":5}}}]"""),
            FinalAnswer("ok"));
        var catalog = new FakeCatalog(Tool("t"));
        var session = new ChatSession("s");

        await Record.ExceptionAsync(() => Engine(handler).RunAsync(session, "go", catalog));

        AssertEveryToolCallAnswered(session);
    }

    [Fact]
    public async Task ThrowingApprovalHook_LeavesEveryToolCallAnswered()
    {
        var handler = new ScriptedHandler(
            Reply("""[{"id":"c1","type":"function","function":{"name":"w","arguments":"{}"}},"""
                + """{"id":"c2","type":"function","function":{"name":"w","arguments":"{}"}}]"""),
            FinalAnswer("ok"));
        var catalog = new FakeCatalog(new FakeTool(
            new ToolDescriptor("w", "a writing tool", """{"type":"object"}""", ReadOnly: false),
            _ => new ToolOutcome("wrote", true)));
        var callbacks = new ChatCallbacks
        {
            ApproveAsync = (_, _) => throw new InvalidOperationException("the approval card could not be shown"),
        };
        var session = new ChatSession("s");

        await Record.ExceptionAsync(() => Engine(handler).RunAsync(session, "go", catalog, callbacks));

        AssertEveryToolCallAnswered(session);
    }

    private static void AssertEveryToolCallAnswered(ChatSession session)
    {
        var answered = session.Messages
            .Where(m => (string?)m!["role"] == "tool")
            .Select(m => m!["tool_call_id"]?.ToString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in session.Messages)
        {
            if ((string?)message!["role"] != "assistant" || message["tool_calls"] is not JsonArray calls) continue;
            foreach (var call in calls)
            {
                var id = call!["id"]?.ToString() ?? "";
                Assert.True(answered.Contains(id),
                    $"assistant tool_call id '{id}' has no tool result; the session would poison the next turn. "
                    + $"Session: {session.Serialize()}");
            }
        }
    }

    private static ChatEngine Engine(ScriptedHandler handler) =>
        new(new HttpClient(handler), new ChatEngineOptions("http://127.0.0.1:9/v1", "test-model"));

    private static FakeTool Tool(string name) =>
        new(new ToolDescriptor(name, "a test tool", """{"type":"object"}""", ReadOnly: true),
            _ => new ToolOutcome("""{"ok":true}""", true));

    /// <summary>A one-shot reply whose assistant message carries the given raw <c>tool_calls</c> JSON.</summary>
    private static string Reply(string toolCallsJson) =>
        """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":""" + toolCallsJson + "}}]}";

    private static string FinalAnswer(string text) =>
        """{"choices":[{"message":{"role":"assistant","content":""" + "\"" + text + "\"}}]}";
}
