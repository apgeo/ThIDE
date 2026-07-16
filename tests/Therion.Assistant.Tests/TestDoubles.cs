using System.Text.Json.Nodes;

namespace Therion.Assistant.Tests;

/// <summary>
/// A scripted OpenAI-compatible endpoint: answers from a queue (then <see cref="Fallback"/>,
/// for infinite-loop cases) and captures every request body for shape assertions.
/// </summary>
public sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public ScriptedHandler(params object[] responses)
    {
        foreach (var r in responses)
            _responses.Enqueue(r as HttpResponseMessage ?? Json((string)r));
    }

    /// <summary>Produces the response once the queue is empty; null means fail the test loudly.</summary>
    public Func<string>? Fallback { get; init; }

    public List<JsonNode> Requests { get; } = [];
    public List<string> Urls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Urls.Add(request.RequestUri!.ToString());
        Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!);

        if (_responses.Count > 0) return _responses.Dequeue();
        if (Fallback is { } fallback) return Json(fallback());
        throw new InvalidOperationException("The scripted endpoint ran out of responses.");
    }

    private static HttpResponseMessage Json(string body) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };
}

public sealed record FakeTool(
    ToolDescriptor Descriptor,
    Func<IReadOnlyDictionary<string, object?>, ToolOutcome> Run);

/// <summary>Records every executed call's arguments; screening misses never land here.</summary>
public sealed class FakeCatalog(params FakeTool[] tools) : IToolCatalog
{
    private readonly Dictionary<string, FakeTool> _byName =
        tools.ToDictionary(t => t.Descriptor.Name, StringComparer.Ordinal);

    public IReadOnlyList<ToolDescriptor> Tools { get; } = tools.Select(t => t.Descriptor).ToList();

    public List<IReadOnlyDictionary<string, object?>> Received { get; } = [];

    public Task<ToolOutcome> CallAsync(
        string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        Received.Add(arguments);
        return Task.FromResult(_byName[name].Run(arguments));
    }
}
