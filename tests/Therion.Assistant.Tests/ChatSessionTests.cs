using System.Text.Json.Nodes;
using Therion.Assistant;

namespace Therion.Assistant.Tests;

/// <summary>Serialize/Restore round-trip and the visible-dialogue extraction used to redraw the
/// pane after a restart (AI-08.3b).</summary>
public sealed class ChatSessionTests
{
    [Fact]
    public async Task Serialize_ThenRestore_ContinuesTheSameConversation()
    {
        var handler = new ScriptedHandler(FinalAnswer("First."), FinalAnswer("Second."));
        var engine = Engine(handler);
        var session = new ChatSession("system prompt");
        await engine.RunAsync(session, "one", Catalog());

        var restored = ChatSession.Restore(session.Serialize());
        Assert.NotNull(restored);
        await engine.RunAsync(restored!, "two", Catalog());

        // The second request carried the whole restored history: system, user, assistant, user.
        var roles = handler.Requests[1]["messages"]!.AsArray().Select(m => (string?)m!["role"]).ToArray();
        Assert.Equal(["system", "user", "assistant", "user"], roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Restore_ReturnsNull_ForUnusableInput(string? json)
    {
        Assert.Null(ChatSession.Restore(json));
    }

    [Fact]
    public async Task AppendSystem_AddsASecondSystemMessageCarriedIntoTheRequest()
    {
        var handler = new ScriptedHandler(FinalAnswer("Answer."));
        var engine = Engine(handler);
        var session = new ChatSession("persona");
        session.AppendSystem("# workspace context card");

        await engine.RunAsync(session, "q", Catalog());

        var messages = handler.Requests[0]["messages"]!.AsArray();
        Assert.Equal("system", (string?)messages[0]!["role"]);
        Assert.Equal("persona", (string?)messages[0]!["content"]);
        Assert.Equal("system", (string?)messages[1]!["role"]);
        Assert.Equal("# workspace context card", (string?)messages[1]!["content"]);
        Assert.Equal("user", (string?)messages[2]!["role"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AppendSystem_IgnoresBlankText(string text)
    {
        var handler = new ScriptedHandler(FinalAnswer("Answer."));
        var engine = Engine(handler);
        var session = new ChatSession("persona");
        session.AppendSystem(text);

        await engine.RunAsync(session, "q", Catalog());

        var roles = handler.Requests[0]["messages"]!.AsArray().Select(m => (string?)m!["role"]).ToArray();
        Assert.Equal(["system", "user"], roles);
    }

    [Fact]
    public void Dialogue_KeepsUserAndAssistantText_DropsSystemAndToolPlumbing()
    {
        // A hand-built session: system, user, an assistant tool-call turn, its tool result, then the
        // assistant's written answer. Only the user line and the final answer are "visible".
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = "you are a bot" },
            new JsonObject { ["role"] = "user", ["content"] = "how many stations?" },
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = "c1" }),
            },
            new JsonObject { ["role"] = "tool", ["tool_call_id"] = "c1", ["content"] = "{\"ok\":true}" },
            new JsonObject { ["role"] = "assistant", ["content"] = "There are 4 stations." },
        };
        var session = ChatSession.Restore(messages.ToJsonString())!;

        var dialogue = session.Dialogue();

        Assert.Collection(dialogue,
            t => { Assert.True(t.IsUser); Assert.Equal("how many stations?", t.Text); },
            t => { Assert.False(t.IsUser); Assert.Equal("There are 4 stations.", t.Text); });
    }

    // ---- helpers (mirror ChatEngineTests) ---------------------------------------------------

    private static ChatEngine Engine(ScriptedHandler handler) =>
        new(new HttpClient(handler), new ChatEngineOptions("http://127.0.0.1:9/v1", "test-model"));

    private static FakeCatalog Catalog(params FakeTool[] tools) => new(tools);

    private static string FinalAnswer(string text) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text },
        }),
    }.ToJsonString();
}
