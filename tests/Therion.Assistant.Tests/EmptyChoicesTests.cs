using System.Net;
using System.Text;
using Therion.Assistant;

namespace Therion.Assistant.Tests;

/// <summary>
/// Replies whose <c>choices</c> array is present but empty. Streaming endpoints that honour
/// <c>stream_options.include_usage</c> send the usage total in a final chunk with
/// <c>"choices": []</c>; a one-shot endpoint may answer <c>{"choices":[]}</c> when it has nothing to say.
/// Neither shape may end the turn with an exception.
/// </summary>
public sealed class EmptyChoicesTests
{
    [Fact]
    public async Task Streaming_UsageOnlyChunkWithEmptyChoices_IsCountedNotFatal()
    {
        var handler = new ScriptedHandler(Sse(
            """{"choices":[{"delta":{"role":"assistant","content":"Hi"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            """{"choices":[],"usage":{"prompt_tokens":4,"completion_tokens":5,"total_tokens":9}}"""));
        var engine = Engine(handler, stream: true);
        var session = new ChatSession("s");

        ChatResult? result = null;
        var error = await Record.ExceptionAsync(async () => result = await engine.RunAsync(session, "hi", new FakeCatalog()));

        Assert.True(error is null, $"the streamed turn failed on the usage chunk: {error?.GetType().Name}: {error?.Message}");
        Assert.Equal("Hi", result!.FinalText);
        Assert.Equal(9, result.Tokens);
        Assert.Equal("assistant", (string?)session.Messages[^1]!["role"]);
    }

    [Fact]
    public async Task OneShot_EmptyChoicesArray_ReturnsTheNoResponseNotice()
    {
        var handler = new ScriptedHandler("""{"choices":[],"usage":{"total_tokens":3}}""");
        var engine = Engine(handler, stream: false);

        ChatResult? result = null;
        var error = await Record.ExceptionAsync(async () => result = await engine.RunAsync(new ChatSession("s"), "hi", new FakeCatalog()));

        Assert.True(error is null, $"the turn failed on an empty choices array: {error?.GetType().Name}: {error?.Message}");
        Assert.Equal("(no response from the endpoint)", result!.FinalText);
    }

    private static ChatEngine Engine(ScriptedHandler handler, bool stream) =>
        new(new HttpClient(handler), new ChatEngineOptions("http://127.0.0.1:9/v1", "test-model") { Stream = stream });

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
}
