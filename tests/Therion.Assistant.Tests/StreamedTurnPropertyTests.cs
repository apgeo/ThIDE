// The chat engine against generated server-sent-event streams. A well-formed stream, however the text
// and the tool-call arguments are cut into chunks, gives the right text, the right tool calls with the
// right arguments, and the token total. A damaged stream (cut short, a chunk that is not JSON, a chunk
// of the wrong shape, no [DONE]) may end the turn with an error but never hangs, and the history always
// stays usable: every tool call the assistant made has its result. Seeds are fixed
// (Therion.TestSupport.GeneratedRuns).

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Therion.TestSupport;

namespace Therion.Assistant.Tests;

public sealed class StreamedTurnPropertyTests
{
    private const int SeedBase = 27_500;
    private static readonly TimeSpan TurnBudget = TimeSpan.FromSeconds(10);

    private sealed record Call(string Id, string Name, string ArgumentsJson);

    // ---- generated content ---------------------------------------------------------------------

    private static readonly string[] Words =
    [
        "survey", "station", "e\u0301", "Peștera", "日本語", "\U0001F600", "\U0001D11E", "a\"b", "back\\slash", "tab\there", "line\nbreak", "{", "}", "[", "]", ",", ":", "null", "\u2028",
    ];

    private static string Text(Random random, int words) =>
        string.Join(random.Next(2) == 0 ? " " : "", Enumerable.Range(0, words).Select(_ => Words[random.Next(Words.Length)]));

    private static JsonNode? Value(Random random, int depth) => random.Next(depth > 2 ? 5 : 8) switch
    {
        0 => JsonValue.Create(random.Next(-1000, 1000)),
        1 => JsonValue.Create(Math.Round(random.NextDouble() * 1e6, 3)),
        2 => JsonValue.Create(Text(random, random.Next(0, 4))),
        3 => JsonValue.Create(random.Next(2) == 0),
        4 => null,
        5 => new JsonArray(Enumerable.Range(0, random.Next(0, 4)).Select(_ => Value(random, depth + 1)).ToArray()),
        _ => Object(random, depth + 1),
    };

    private static JsonObject Object(Random random, int depth)
    {
        var o = new JsonObject();
        for (int i = 0, n = random.Next(0, 4); i < n; i++) o["k" + i + Text(random, random.Next(0, 2))] = Value(random, depth);
        return o;
    }

    // Cuts a string into pieces at random places, never between the two halves of a surrogate pair
    // (an endpoint sends whole characters).
    private static List<string> Split(Random random, string s, int maxPieces)
    {
        var cuts = new SortedSet<int>();
        for (int i = 0, n = random.Next(0, maxPieces); i < n && s.Length > 1; i++)
        {
            int at = random.Next(1, s.Length);
            if (!char.IsLowSurrogate(s[at])) cuts.Add(at);
        }
        var pieces = new List<string>();
        int start = 0;
        foreach (var c in cuts) { pieces.Add(s[start..c]); start = c; }
        pieces.Add(s[start..]);
        return pieces;
    }

    // ---- the stream ----------------------------------------------------------------------------

    private static string Chunk(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static JsonObject DeltaChunk(JsonObject delta, string? finish = null) =>
        new() { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }) };

    /// <summary>The data lines of one streamed completion: text and/or tool calls cut up at random, then a usage chunk.</summary>
    private static List<string> StreamLines(Random random, string? text, IReadOnlyList<Call> calls, int totalTokens)
    {
        var lines = new List<string>();
        if (random.Next(2) == 0) lines.Add(Chunk(DeltaChunk(new JsonObject { ["role"] = "assistant", ["content"] = "" })));

        foreach (var piece in text is null ? [] : Split(random, text, 6))
            lines.Add(Chunk(DeltaChunk(new JsonObject { ["content"] = piece })));

        // Each call's deltas in order; the calls' deltas interleaved at random.
        var perCall = new List<Queue<JsonObject>>();
        for (int i = 0; i < calls.Count; i++)
        {
            var queue = new Queue<JsonObject>();
            var parts = Split(random, calls[i].ArgumentsJson, 8);
            for (int p = 0; p < parts.Count; p++)
            {
                var function = new JsonObject { ["arguments"] = parts[p] };
                var tc = new JsonObject { ["index"] = i, ["function"] = function };
                if (p == 0)
                {
                    tc["id"] = calls[i].Id;
                    tc["type"] = "function";
                    function["name"] = calls[i].Name;
                }
                queue.Enqueue(tc);
            }
            perCall.Add(queue);
        }
        while (perCall.Any(q => q.Count > 0))
        {
            var open = perCall.Where(q => q.Count > 0).ToList();
            // Sometimes two calls' pieces share one chunk.
            var deltas = new JsonArray();
            for (int i = 0, n = random.Next(1, 3); i < n && open.Any(q => q.Count > 0); i++)
                deltas.Add(open.Where(q => q.Count > 0).ElementAt(random.Next(open.Count(q => q.Count > 0))).Dequeue());
            lines.Add(Chunk(DeltaChunk(new JsonObject { ["tool_calls"] = deltas })));
        }

        lines.Add(Chunk(DeltaChunk([], calls.Count > 0 ? "tool_calls" : "stop")));
        // The usage total comes in a last chunk with no choices.
        lines.Add(Chunk(new JsonObject { ["choices"] = new JsonArray(), ["usage"] = new JsonObject { ["total_tokens"] = totalTokens } }));
        return lines;
    }

    private static string Sse(Random random, IEnumerable<string> dataLines, bool done = true)
    {
        string eol = random.Next(3) == 0 ? "\r\n" : "\n";
        var sb = new StringBuilder();
        foreach (var line in dataLines)
        {
            if (random.Next(6) == 0) sb.Append(": keep-alive").Append(eol).Append(eol);
            if (random.Next(10) == 0) sb.Append("event: message").Append(eol);
            sb.Append(random.Next(5) == 0 ? "data:" : "data: ").Append(line).Append(eol).Append(eol);
        }
        if (done) sb.Append("data: [DONE]").Append(eol).Append(eol);
        return sb.ToString();
    }

    private static HttpResponseMessage Response(string sse) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
    };

    // ---- the engine under test -----------------------------------------------------------------

    private static readonly string[] ToolNames = ["list_files", "read_file", "get_diagnostics", "survey_stats"];

    private static FakeCatalog Catalog(List<string> seen) => new(ToolNames.Select(n => new FakeTool(
        new ToolDescriptor(n, "a test tool", """{"type":"object"}""", ReadOnly: true),
        args =>
        {
            lock (seen) seen.Add(n + " " + JsonSerializer.Serialize(args));
            return new ToolOutcome("""{"ok":true}""", true);
        })).ToArray());

    private static ChatEngine Engine(ScriptedHandler handler) =>
        new(new HttpClient(handler), new ChatEngineOptions("http://127.0.0.1:9/v1", "test-model") { Stream = true });

    /// <summary>Every tool call the assistant made has a result right after it, in order, and nothing else sits between.</summary>
    private static void AssertSessionUsable(ChatSession session)
    {
        var messages = session.Messages.Select(m => m!.AsObject()).ToList();
        for (int i = 0; i < messages.Count; i++)
        {
            if ((string?)messages[i]["role"] != "assistant" || messages[i]["tool_calls"] is not JsonArray calls) continue;
            for (int c = 0; c < calls.Count; c++)
            {
                var id = calls[c]?["id"]?.ToString() ?? "";
                Assert.True(i + 1 + c < messages.Count
                            && (string?)messages[i + 1 + c]["role"] == "tool"
                            && (messages[i + 1 + c]["tool_call_id"]?.ToString() ?? "") == id,
                    $"tool call {c} ('{id}') of message {i} has no result next in line. Session: {session.Serialize()}");
            }
        }
        // The history has no message that is not an object with a role.
        Assert.All(messages, m => Assert.False(string.IsNullOrEmpty((string?)m["role"])));
    }

    private static string FinalStream(string text) => Sse(new Random(1), StreamLines(new Random(1), text, [], 5));

    // ---- well-formed streams -------------------------------------------------------------------

    [Fact]
    public async Task A_well_formed_stream_gives_the_text_the_calls_and_the_arguments()
    {
        for (int seed = SeedBase; seed < SeedBase + GeneratedRuns.Count(250); seed++)
        {
            var random = new Random(seed);
            var calls = Enumerable.Range(0, random.Next(0, 4))
                .Select(i => new Call($"call_{random.Next(1000)}_{i}", ToolNames[random.Next(ToolNames.Length)], Object(random, 0).ToJsonString()))
                .ToList();
            string finalText = Text(random, random.Next(1, 8));
            var seen = new List<string>();
            var handler = calls.Count == 0
                ? new ScriptedHandler(Response(Sse(random, StreamLines(random, finalText, [], 11))))
                : new ScriptedHandler(
                    Response(Sse(random, StreamLines(random, random.Next(2) == 0 ? null : "let me look", calls, 7))),
                    Response(Sse(random, StreamLines(random, finalText, [], 13))));
            var session = new ChatSession("system");
            using var cts = new CancellationTokenSource(TurnBudget);

            ChatResult result;
            try { result = await Engine(handler).RunAsync(session, "go", Catalog(seen), ct: cts.Token); }
            catch (Exception ex) { throw new Xunit.Sdk.XunitException($"seed {seed}: the turn failed: {ex.GetType().Name}: {ex.Message}"); }

            string at = $"seed {seed} (replay: THIDE_PROPERTY_SEED={seed} THIDE_PROPERTY_RUNS=1)";
            Assert.True(finalText == result.FinalText, $"{at}: text '{result.FinalText}' instead of '{finalText}'");
            Assert.True(calls.Count == result.Calls.Count, $"{at}: {result.Calls.Count} calls instead of {calls.Count}");
            Assert.True(result.Calls.All(c => c is { SchemaValid: true, Ok: true }), $"{at}: a call was not schema-valid or not ok");
            // The totals of the two completions add up (a turn with no tool calls is one completion).
            Assert.Equal(calls.Count == 0 ? 11 : 7 + 13, result.Tokens);
            var expected = calls.Select(c => c.Name + " " + JsonSerializer.Serialize(JsonSerializer.Deserialize<Dictionary<string, object?>>(c.ArgumentsJson))).ToList();
            Assert.True(expected.SequenceEqual(seen), $"{at}: the tools got\n  {string.Join("\n  ", seen)}\ninstead of\n  {string.Join("\n  ", expected)}");
            AssertSessionUsable(session);
            var last = session.Messages[^1]!.AsObject();
            Assert.Equal("assistant", (string?)last["role"]);
            Assert.Equal(finalText, (string?)last["content"]);
        }
    }

    // ---- damaged streams -----------------------------------------------------------------------

    private static readonly string[] OddChunks =
    [
        "{\"choices\":[{\"delta\":",                                          // cut off
        "not json at all",
        "[1,2,3]", "null", "\"text\"", "42", "{}",
        "{\"choices\":\"nope\"}", "{\"choices\":[null]}", "{\"choices\":[{\"delta\":\"x\"}]}", "{\"choices\":[{\"delta\":null}]}",
        "{\"choices\":[{\"delta\":{\"content\":5}}]}",                         // text that is a number
        "{\"choices\":[{\"delta\":{\"content\":[\"a\"]}}]}",                   // text as parts
        "{\"choices\":[{\"delta\":{\"content\":{\"a\":1}}}]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":\"x\"}}]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":[null,1,\"a\"]}}]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":\"x\"}]}}]}",    // index that is not a number
        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":-1,\"id\":3,\"function\":{\"name\":4,\"arguments\":7}}]}}]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":99999999999}]}}]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1.5,\"function\":\"f\"}]}}]}",
        "{\"choices\":[],\"usage\":{\"total_tokens\":\"many\"}}",
        "{\"usage\":{\"total_tokens\":1e99}}",
        "{\"usage\":[]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c9\",\"function\":{\"name\":\"nope\",\"arguments\":\"{\"}}]}}]}",
        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"dup\",\"function\":{\"name\":\"list_files\",\"arguments\":\"[]\"}},{\"index\":0,\"id\":\"dup\",\"function\":{\"name\":\"list_files\",\"arguments\":\"[]\"}}]}}]}",
    ];

    private static string DamagedStream(Random random)
    {
        var calls = Enumerable.Range(0, random.Next(0, 3))
            .Select(i => new Call($"call_{i}", ToolNames[random.Next(ToolNames.Length)], random.Next(5) == 0 ? "{\"a\":" : Object(random, 0).ToJsonString()))
            .ToList();
        var lines = StreamLines(random, random.Next(3) == 0 ? null : Text(random, random.Next(1, 5)), calls, 9);
        for (int i = 0, n = random.Next(0, 4); i < n; i++)
            lines.Insert(random.Next(lines.Count + 1), OddChunks[random.Next(OddChunks.Length)]);
        if (random.Next(4) == 0) lines.RemoveAt(random.Next(lines.Count));
        if (random.Next(5) == 0 && lines.Count > 1) { var i = random.Next(lines.Count); (lines[i], lines[^1]) = (lines[^1], lines[i]); }
        var sse = Sse(random, lines, done: random.Next(3) != 0);
        // The connection may drop anywhere, in the middle of a line.
        return random.Next(3) == 0 ? sse[..random.Next(sse.Length + 1)] : sse;
    }

    [Fact]
    public async Task A_damaged_stream_never_hangs_and_never_leaves_a_tool_call_without_its_result()
    {
        var failures = new List<string>();
        for (int seed = SeedBase + 10_000; seed < SeedBase + 10_000 + GeneratedRuns.Count(400); seed++)
        {
            var random = new Random(seed);
            // The first completion is damaged; whatever follows it is a plain text answer.
            var handler = new ScriptedHandler(Response(DamagedStream(random)))
            {
                Fallback = () => Sse(new Random(2), StreamLines(new Random(2), "done", [], 3)),
            };
            var session = new ChatSession("system");
            using var cts = new CancellationTokenSource(TurnBudget);

            Exception? error = null;
            try { await Engine(handler).RunAsync(session, "go", Catalog([]), ct: cts.Token); }
            catch (Exception ex) { error = ex; }

            string at = $"seed {seed} (replay: THIDE_PROPERTY_SEED={seed} THIDE_PROPERTY_RUNS=1)";
            if (cts.IsCancellationRequested) { failures.Add($"{at}: the turn did not end within {TurnBudget.TotalSeconds:0} s"); continue; }
            try { AssertSessionUsable(session); }
            catch (Xunit.Sdk.XunitException ex) { failures.Add($"{at}: {ex.Message} (turn {(error is null ? "ended" : "threw " + error.GetType().Name + ": " + error.Message)})"); }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(8)) + (failures.Count > 8 ? $"{Environment.NewLine}… and {failures.Count - 8} more" : ""));
    }

    [Fact]
    public async Task A_chunk_of_the_wrong_shape_is_skipped_not_thrown()
    {
        // A chunk of the wrong shape is skipped like a chunk that is not JSON, so the turn goes on and
        // ends with a result: no exception from reading a field as a type it is not.
        var failures = new List<string>();
        for (int seed = SeedBase + 20_000; seed < SeedBase + 20_000 + GeneratedRuns.Count(400); seed++)
        {
            var random = new Random(seed);
            var handler = new ScriptedHandler(Response(DamagedStream(random)))
            {
                Fallback = () => Sse(new Random(2), StreamLines(new Random(2), "done", [], 3)),
            };
            using var cts = new CancellationTokenSource(TurnBudget);

            try { await Engine(handler).RunAsync(new ChatSession("system"), "go", Catalog([]), ct: cts.Token); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { failures.Add($"seed {seed}: hung"); }
            catch (Exception ex)
            {
                failures.Add($"seed {seed} (replay: THIDE_PROPERTY_SEED={seed} THIDE_PROPERTY_RUNS=1): {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(8)) + (failures.Count > 8 ? $"{Environment.NewLine}… and {failures.Count - 8} more" : ""));
    }

    [Fact]
    public async Task Each_odd_chunk_between_two_good_ones_changes_nothing()
    {
        foreach (var odd in OddChunks)
        {
            var lines = new List<string>
            {
                Chunk(DeltaChunk(new JsonObject { ["content"] = "hel" })),
                odd,
                Chunk(DeltaChunk(new JsonObject { ["content"] = "lo" })),
            };
            // An odd chunk that happens to carry a whole tool call makes the engine ask again; the answer to that is "done".
            var handler = new ScriptedHandler(Response(Sse(new Random(3), lines)))
            {
                Fallback = () => Sse(new Random(2), StreamLines(new Random(2), "done", [], 3)),
            };
            var session = new ChatSession("system");

            var result = await Engine(handler).RunAsync(session, "go", Catalog([]));

            Assert.True(result.FinalText is "hello" or "done", $"after '{odd}': '{result.FinalText}'");
            AssertSessionUsable(session);
        }
    }
}
