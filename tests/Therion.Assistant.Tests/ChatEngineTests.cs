using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Therion.Assistant;

namespace Therion.Assistant.Tests;

/// <summary>
/// The engine against a scripted endpoint + fake catalog — no model, no network, no MCP server.
/// Covers the loop's contract: screening (schema validity), approval gating, result feedback,
/// turn budget, cancellation backfill, and session continuity.
/// </summary>
public sealed class ChatEngineTests
{
    // ---- plain answers ---------------------------------------------------------------------

    [Fact]
    public async Task PlainAnswer_ReturnsText_CountsTokens_AndExtendsSession()
    {
        var handler = new ScriptedHandler(FinalAnswer("It has 4 stations.", tokens: 123));
        var engine = Engine(handler);
        var session = new ChatSession("system prompt");

        var result = await engine.RunAsync(session, "How many stations?", Catalog());

        Assert.Equal("It has 4 stations.", result.FinalText);
        Assert.Empty(result.Calls);
        Assert.Equal(0, result.Turns);
        Assert.Equal(123, result.Tokens);
        // system + user + assistant — the answer must persist for the next turn.
        Assert.Equal(3, session.Length);
        Assert.Equal("assistant", (string?)session.Messages[2]!["role"]);
    }

    [Fact]
    public async Task SecondRun_ContinuesTheSameConversation()
    {
        var handler = new ScriptedHandler(FinalAnswer("First."), FinalAnswer("Second."));
        var engine = Engine(handler);
        var session = new ChatSession("system prompt");

        await engine.RunAsync(session, "one", Catalog());
        await engine.RunAsync(session, "two", Catalog());

        // The second request must carry the whole history: system, user, assistant, user.
        var second = handler.Requests[1];
        var roles = second["messages"]!.AsArray().Select(m => (string?)m!["role"]).ToArray();
        Assert.Equal(["system", "user", "assistant", "user"], roles);
    }

    [Fact]
    public async Task NoChoices_ReturnsTheNoResponseNotice()
    {
        var handler = new ScriptedHandler("{}");
        var result = await Engine(handler).RunAsync(new ChatSession("s"), "hi", Catalog());

        Assert.Equal("(no response from the endpoint)", result.FinalText);
    }

    [Fact]
    public async Task EndpointFailure_ThrowsWithStatusAndDetail()
    {
        var handler = new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("model not loaded"),
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(handler).RunAsync(new ChatSession("s"), "hi", Catalog()));

        Assert.Contains("500", ex.Message);
        Assert.Contains("model not loaded", ex.Message);
    }

    // ---- the tool loop ---------------------------------------------------------------------

    [Fact]
    public async Task ToolCall_Executes_FeedsTheResultBack_AndRecords()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "survey_stats", """{"limit":5}""")),
            FinalAnswer("Done."));
        var catalog = Catalog(readOnlyTool("survey_stats", _ => new ToolOutcome("""{"ok":true}""", true)));

        var result = await Engine(handler).RunAsync(new ChatSession("s"), "stats?", catalog);

        Assert.Equal("Done.", result.FinalText);
        Assert.Equal(1, result.Turns);
        var call = Assert.Single(result.Calls);
        Assert.Equal(new ChatToolCall("survey_stats", SchemaValid: true, Ok: true), call);
        var received = Assert.Single(catalog.Received);
        Assert.Equal(5, ((System.Text.Json.JsonElement)received["limit"]!).GetInt32());

        // The follow-up request must echo the assistant tool_calls turn, then the tool result.
        var second = handler.Requests[1]["messages"]!.AsArray();
        Assert.Equal("assistant", (string?)second[^2]!["role"]);
        Assert.NotNull(second[^2]!["tool_calls"]);
        Assert.Equal("tool", (string?)second[^1]!["role"]);
        Assert.Equal("c1", (string?)second[^1]!["tool_call_id"]);
        Assert.Equal("""{"ok":true}""", (string?)second[^1]!["content"]);
    }

    [Fact]
    public async Task UnknownTool_IsSchemaInvalid_AndNeverReachesTheCatalog()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "made_up_tool", "{}")),
            FinalAnswer("ok"));
        var catalog = Catalog(readOnlyTool("real_tool", _ => new ToolOutcome("x", true)));

        var result = await Engine(handler).RunAsync(new ChatSession("s"), "go", catalog);

        Assert.Equal(new ChatToolCall("made_up_tool", SchemaValid: false, Ok: false), Assert.Single(result.Calls));
        Assert.Empty(catalog.Received);
        var toolMsg = handler.Requests[1]["messages"]!.AsArray()[^1]!;
        Assert.Equal("Error: no such tool 'made_up_tool'.", (string?)toolMsg["content"]);
    }

    [Fact]
    public async Task MalformedArguments_AreSchemaInvalid_AndNeverReachTheCatalog()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "real_tool", "{not json")),
            FinalAnswer("ok"));
        var catalog = Catalog(readOnlyTool("real_tool", _ => new ToolOutcome("x", true)));

        var result = await Engine(handler).RunAsync(new ChatSession("s"), "go", catalog);

        Assert.Equal(new ChatToolCall("real_tool", SchemaValid: false, Ok: false), Assert.Single(result.Calls));
        Assert.Empty(catalog.Received);
    }

    [Fact]
    public async Task CatalogException_IsSchemaValid_ButNotOk()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "boom", "{}")),
            FinalAnswer("ok"));
        var catalog = Catalog(readOnlyTool("boom", _ => throw new InvalidOperationException("kaput")));

        var result = await Engine(handler).RunAsync(new ChatSession("s"), "go", catalog);

        Assert.Equal(new ChatToolCall("boom", SchemaValid: true, Ok: false), Assert.Single(result.Calls));
        var toolMsg = handler.Requests[1]["messages"]!.AsArray()[^1]!;
        Assert.Equal("Error: kaput", (string?)toolMsg["content"]);
    }

    [Fact]
    public async Task EmptyToolContent_BecomesTheNoContentMarker()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "quiet", "{}")),
            FinalAnswer("ok"));
        var catalog = Catalog(readOnlyTool("quiet", _ => new ToolOutcome("", true)));

        await Engine(handler).RunAsync(new ChatSession("s"), "go", catalog);

        var toolMsg = handler.Requests[1]["messages"]!.AsArray()[^1]!;
        Assert.Equal("(no content)", (string?)toolMsg["content"]);
    }

    [Fact]
    public async Task TurnBudgetExhausted_GivesUp()
    {
        // The model asks for a tool on every turn, forever.
        var handler = new ScriptedHandler { Fallback = () => ToolCallTurn(("c", "loop_tool", "{}")) };
        var catalog = Catalog(readOnlyTool("loop_tool", _ => new ToolOutcome("again", true)));
        var engine = Engine(handler, maxTurns: 2);

        var result = await engine.RunAsync(new ChatSession("s"), "go", catalog);

        Assert.Equal("(gave up after 2 tool turns)", result.FinalText);
        Assert.Equal(3, result.Turns); // i = 0..maxTurns inclusive, matching the eval client
    }

    // ---- forced synthesis turn (AI-08.1) ----------------------------------------------------

    [Fact]
    public async Task EmptyFinalContent_WithSynthesis_ForcesAToolFreeAnswerTurn()
    {
        // Tool call, then the model stops calling tools but returns empty content — synthesis must
        // kick in and its text becomes the answer.
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "list_stations", "{}")),
            FinalAnswer(""),
            FinalAnswer("Found 3 stations near the entrance."));
        var catalog = Catalog(readOnlyTool("list_stations", _ => new ToolOutcome("""{"ok":true}""", true)));
        var engine = Engine(handler, synthesize: true);
        var session = new ChatSession("s");

        var result = await engine.RunAsync(session, "which stations?", catalog);

        Assert.Equal("Found 3 stations near the entrance.", result.FinalText);
        // The synthesis request must forbid further tool calls.
        Assert.Equal("none", (string?)handler.Requests[^1]["tool_choice"]);
        // The ephemeral nudge must not be persisted: the session ends with the *answer*, and no
        // user turn after the tool result carries the synthesis instruction.
        Assert.Equal("assistant", (string?)session.Messages[^1]!["role"]);
        Assert.Equal("Found 3 stations near the entrance.", (string?)session.Messages[^1]!["content"]);
        Assert.DoesNotContain(session.Messages,
            m => ((string?)m!["content"])?.Contains("Do not call any more tools") == true);
    }

    [Fact]
    public async Task GiveUp_WithSynthesis_AnswersFromContextInstead()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "loop_tool", "{}")),
            ToolCallTurn(("c2", "loop_tool", "{}")),
            FinalAnswer("Here is what I found so far."));
        var catalog = Catalog(readOnlyTool("loop_tool", _ => new ToolOutcome("again", true)));
        var engine = Engine(handler, maxTurns: 1, synthesize: true);

        var result = await engine.RunAsync(new ChatSession("s"), "go", catalog);

        Assert.Equal("Here is what I found so far.", result.FinalText);
        Assert.Equal("none", (string?)handler.Requests[^1]["tool_choice"]);
    }

    [Fact]
    public async Task EmptyFinalContent_WithoutSynthesis_StaysBlank_AndMakesNoExtraCall()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "list_stations", "{}")),
            FinalAnswer(""));
        var catalog = Catalog(readOnlyTool("list_stations", _ => new ToolOutcome("""{"ok":true}""", true)));
        var engine = Engine(handler); // synthesize defaults off

        var result = await engine.RunAsync(new ChatSession("s"), "which?", catalog);

        Assert.Equal("", result.FinalText);
        Assert.Equal(2, handler.Requests.Count); // no third (synthesis) request
    }

    // ---- streaming (AI-08.2) ----------------------------------------------------------------

    [Fact]
    public async Task Streaming_EmitsDeltas_AssemblesText_AndCountsUsage()
    {
        var handler = new ScriptedHandler(Sse(
            """{"choices":[{"delta":{"role":"assistant","content":"Hel"}}]}""",
            """{"choices":[{"delta":{"content":"lo."}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"total_tokens":9}}"""));
        var updates = new List<ChatUpdate>();

        var result = await Engine(handler, stream: true).RunAsync(
            new ChatSession("s"), "hi", Catalog(), new ChatCallbacks { OnUpdate = updates.Add });

        Assert.Equal("Hello.", result.FinalText);
        Assert.Equal(9, result.Tokens);
        Assert.True((bool?)handler.Requests[0]["stream"]);
        var deltas = updates.OfType<AssistantDelta>().ToList();
        Assert.Equal(2, deltas.Count);
        Assert.Equal("Hel", deltas[0].Delta);
        Assert.Equal("Hello.", deltas[^1].Text);
        Assert.Equal("Hello.", Assert.IsType<AssistantAnswered>(updates[^1]).Text);
    }

    [Fact]
    public async Task Streaming_AssemblesToolCalls_AcrossChunks_ThenExecutes()
    {
        var handler = new ScriptedHandler(
            Sse(
                """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1","type":"function","function":{"name":"survey_stats","arguments":""}}]}}]}""",
                """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"limit\":"}}]}}]}""",
                """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"5}"}}]}}]}"""),
            Sse("""{"choices":[{"delta":{"content":"Done."}}]}"""));
        var catalog = Catalog(readOnlyTool("survey_stats", _ => new ToolOutcome("""{"ok":true}""", true)));

        var result = await Engine(handler, stream: true).RunAsync(new ChatSession("s"), "stats", catalog);

        Assert.Equal("Done.", result.FinalText);
        var call = Assert.Single(result.Calls);
        Assert.Equal(new ChatToolCall("survey_stats", SchemaValid: true, Ok: true), call);
        var received = Assert.Single(catalog.Received);
        Assert.Equal(5, ((System.Text.Json.JsonElement)received["limit"]!).GetInt32());
    }

    // ---- approval gating -------------------------------------------------------------------

    [Fact]
    public async Task NonReadOnlyTool_DeniedByTheHook_NeverExecutes()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "edit_file", """{"path":"a.th"}""")),
            FinalAnswer("understood"));
        var catalog = Catalog(writingTool("edit_file", _ => new ToolOutcome("wrote", true)));
        var asked = new List<ToolCallInfo>();
        var callbacks = new ChatCallbacks
        {
            ApproveAsync = (info, _) => { asked.Add(info); return Task.FromResult(false); },
        };

        var result = await Engine(handler).RunAsync(new ChatSession("s"), "edit", catalog, callbacks);

        Assert.Empty(catalog.Received);
        Assert.Equal(new ChatToolCall("edit_file", SchemaValid: true, Ok: false, Declined: true),
            Assert.Single(result.Calls));
        var info = Assert.Single(asked);
        Assert.False(info.ReadOnly);
        var toolMsg = handler.Requests[1]["messages"]!.AsArray()[^1]!;
        Assert.Equal("The user declined this action.", (string?)toolMsg["content"]);
    }

    [Fact]
    public async Task ReadOnlyTool_SkipsTheApprovalHook()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "read_file", "{}")),
            FinalAnswer("ok"));
        var catalog = Catalog(readOnlyTool("read_file", _ => new ToolOutcome("text", true)));
        var callbacks = new ChatCallbacks
        {
            ApproveAsync = (_, _) => throw new InvalidOperationException("must not be consulted"),
        };

        var result = await Engine(handler).RunAsync(new ChatSession("s"), "read", catalog, callbacks);

        Assert.Single(catalog.Received);
        Assert.True(Assert.Single(result.Calls).Ok);
    }

    [Fact]
    public async Task Updates_FireInOrder()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "read_file", "{}")),
            FinalAnswer("done"));
        var catalog = Catalog(readOnlyTool("read_file", _ => new ToolOutcome("text", true)));
        var updates = new List<ChatUpdate>();

        await Engine(handler).RunAsync(new ChatSession("s"), "read", catalog,
            new ChatCallbacks { OnUpdate = updates.Add });

        Assert.Collection(updates,
            u => Assert.Equal("read_file", Assert.IsType<ToolCallStarted>(u).Call.Tool),
            u => Assert.True(Assert.IsType<ToolCallFinished>(u).Ok),
            u => Assert.Equal("done", Assert.IsType<AssistantAnswered>(u).Text));
    }

    // ---- cancellation ----------------------------------------------------------------------

    [Fact]
    public async Task Cancellation_MidBatch_BackfillsEveryPendingToolResult()
    {
        var handler = new ScriptedHandler(
            ToolCallTurn(("c1", "slow", "{}"), ("c2", "slow", "{}")));
        using var cts = new CancellationTokenSource();
        var catalog = Catalog(readOnlyTool("slow", _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));
        var session = new ChatSession("s");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Engine(handler).RunAsync(session, "go", catalog, ct: cts.Token));

        // system, user, assistant(tool_calls), then one backfilled result per advertised call —
        // the session must stay well-formed so the *next* turn is accepted.
        Assert.Equal(5, session.Length);
        foreach (var id in new[] { "c1", "c2" })
        {
            var msg = session.Messages.Single(m => (string?)m!["tool_call_id"] == id)!;
            Assert.Equal("tool", (string?)msg["role"]);
            Assert.Equal("(cancelled by the user)", (string?)msg["content"]);
        }
    }

    // ---- request shape ---------------------------------------------------------------------

    [Fact]
    public async Task Request_AdvertisesTools_AndTargetsChatCompletions()
    {
        var handler = new ScriptedHandler(FinalAnswer("hi"));
        var catalog = Catalog(readOnlyTool("read_file", _ => new ToolOutcome("x", true)));

        await Engine(handler).RunAsync(new ChatSession("s"), "hi", catalog);

        Assert.EndsWith("/v1/chat/completions", handler.Urls.Single());
        var request = handler.Requests[0];
        Assert.Equal("test-model", (string?)request["model"]);
        Assert.False((bool?)request["stream"]);
        var tool = request["tools"]!.AsArray().Single()!;
        Assert.Equal("function", (string?)tool["type"]);
        Assert.Equal("read_file", (string?)tool["function"]!["name"]);
        Assert.NotNull(tool["function"]!["parameters"]);
    }

    [Fact]
    public async Task Request_OmitsTools_WhenTheCatalogIsEmpty()
    {
        var handler = new ScriptedHandler(FinalAnswer("hi"));

        await Engine(handler).RunAsync(new ChatSession("s"), "hi", Catalog());

        Assert.Null(handler.Requests[0]["tools"]);
        Assert.Null(handler.Requests[0]["tool_choice"]);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static ChatEngine Engine(
        ScriptedHandler handler, int maxTurns = 8, bool synthesize = false, bool stream = false) =>
        new(new HttpClient(handler), new ChatEngineOptions("http://127.0.0.1:9/v1", "test-model")
        {
            MaxTurns = maxTurns,
            SynthesizeFinalAnswer = synthesize,
            Stream = stream,
        });

    /// <summary>A text/event-stream response body: one <c>data:</c> frame per chunk, then [DONE].</summary>
    private static HttpResponseMessage Sse(params string[] chunks)
    {
        var sb = new StringBuilder();
        foreach (var c in chunks) sb.Append("data: ").Append(c).Append("\n\n");
        sb.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

    private static FakeCatalog Catalog(params FakeTool[] tools) => new(tools);

    private static FakeTool readOnlyTool(string name, Func<IReadOnlyDictionary<string, object?>, ToolOutcome> run) =>
        new(new ToolDescriptor(name, "a test tool", """{"type":"object"}""", ReadOnly: true), run);

    private static FakeTool writingTool(string name, Func<IReadOnlyDictionary<string, object?>, ToolOutcome> run) =>
        new(new ToolDescriptor(name, "a writing test tool", """{"type":"object"}""", ReadOnly: false), run);

    private static string FinalAnswer(string text, int tokens = 0) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text },
        }),
        ["usage"] = new JsonObject { ["total_tokens"] = tokens },
    }.ToJsonString();

    private static string ToolCallTurn(params (string Id, string Name, string Args)[] calls) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Args },
                }).ToArray()),
            },
        }),
    }.ToJsonString();
}
