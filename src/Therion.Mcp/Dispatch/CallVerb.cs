// `therion-mcp call` — one tool call from a shell or a CI step: the same tools a macro or the
// Assistant uses, in-process, with JSON arguments and the tool's JSON result on stdout. Tools keep
// their own safety: those that change files preview unless the arguments say dryRun:false (or write:true).

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace Therion.Mcp.Dispatch;

public static class CallVerb
{
    /// <summary>
    /// <c>call --list [--json]</c>, or <c>call &lt;tool&gt; --workspace p [--args '{json}' | --args-file f]
    /// [--arg k=v]…</c>. Returns 0 when the tool succeeded, 1 when it reported a failure, 2 usage error.
    /// </summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr,
        Func<IServiceCollection, IServiceProvider> buildProvider, CancellationToken ct = default)
    {
        string? workspace = null, tool = null, json = null;
        bool list = false, asJson = false;
        var pairs = new List<(string Key, string Value)>();
        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--workspace" when i + 1 < args.Count: workspace = args[++i]; break;
                case "--list": list = true; break;
                case "--json": asJson = true; break;
                case "--args" when i + 1 < args.Count: json = args[++i]; break;
                case "--args-file" when i + 1 < args.Count:
                    var file = args[++i];
                    try { json = await File.ReadAllTextAsync(file, ct); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { stderr.WriteLine($"error: {file}: {ex.Message}"); return 2; }
                    break;
                case "--arg" when i + 1 < args.Count:
                    var kv = args[++i];
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) { stderr.WriteLine($"error: --arg expects key=value, got '{kv}'."); return 2; }
                    pairs.Add((kv[..eq], kv[(eq + 1)..]));
                    break;
                default:
                    if (tool is null && !args[i].StartsWith("--", StringComparison.Ordinal)) tool = args[i];
                    else { stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
                    break;
            }
        }

        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(workspace));
        services.AddToolServices();
        var dispatcher = new ToolDispatcher(buildProvider(services));

        if (list)
        {
            if (asJson)
                stdout.WriteLine(JsonSerializer.Serialize(dispatcher.Tools.Select(t => new { name = t.Name, readOnly = t.ReadOnly, preview = t.CanPreview, description = t.Description, inputSchema = t.Schema }),
                    new JsonSerializerOptions { WriteIndented = true }));
            else
                foreach (var t in dispatcher.Tools)
                    stdout.WriteLine($"{t.Name,-28} {(t.ReadOnly ? "read" : t.CanPreview ? "preview" : "acts")}  {FirstSentence(t.Description)}");
            return 0;
        }
        if (tool is null) { stderr.WriteLine("error: use 'call --list' or 'call <tool> --workspace <path> [--args '{…}'] [--arg key=value]…'."); return 2; }
        if (dispatcher.Find(tool) is not { } descriptor) { stderr.WriteLine($"error: no tool '{tool}' (see 'call --list')."); return 2; }

        JsonElement arguments;
        try { arguments = Arguments(json, pairs, descriptor.Schema); }
        catch (JsonException ex) { stderr.WriteLine($"error: --args is not a JSON object: {ex.Message}"); return 2; }

        var outcome = await dispatcher.CallAsync(tool, arguments, ct);
        if (outcome.Result.ValueKind != JsonValueKind.Undefined)
            stdout.WriteLine(JsonSerializer.Serialize(outcome.Result, new JsonSerializerOptions(McpJsonUtilities.DefaultOptions) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        if (!outcome.Ok) stderr.WriteLine("error: " + (outcome.Error ?? "the tool failed"));
        return outcome.Ok ? 0 : 1;
    }

    /// <summary>
    /// The arguments object: <paramref name="json"/> (an object), then each key=value on top. A value
    /// is typed by the tool's schema (number, boolean, array as JSON) and is a string otherwise.
    /// </summary>
    public static JsonElement Arguments(string? json, IReadOnlyList<(string Key, string Value)> pairs, JsonElement schema)
    {
        var obj = string.IsNullOrWhiteSpace(json) ? new System.Text.Json.Nodes.JsonObject()
            : System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? throw new JsonException("expected an object");
        foreach (var (key, value) in pairs)
        {
            string? type = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props)
                && props.TryGetProperty(key, out var prop) && prop.TryGetProperty("type", out var t)
                ? (t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => x != "null") : t.GetString())
                : null;
            obj[key] = type is "integer" or "number" or "boolean" or "array" or "object"
                ? System.Text.Json.Nodes.JsonNode.Parse(value)
                : System.Text.Json.Nodes.JsonValue.Create(value);
        }
        return JsonSerializer.SerializeToElement(obj);
    }

    private static string FirstSentence(string text)
    {
        int dot = text.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? text[..(dot + 1)] : text;
    }
}
