// What the assistant may send to the model. The default sends everything — the model runs on this
// computer (LM Studio). The other two exist for when it does not: hide every tool that returns survey
// data, or keep the tools but blank out the values in what they return.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Therion.Assistant;

public enum AssistantDataPolicy
{
    /// <summary>Everything the tools return goes to the model.</summary>
    SendAll,
    /// <summary>Only tools that reveal no survey data are offered; no workspace context; no saved conversation.</summary>
    HideContent,
    /// <summary>All tools, but numbers, names and free text in their results are replaced by placeholders.</summary>
    Redact,
}

/// <summary>Offers only the tools that reveal no survey data (the HideContent policy).</summary>
public sealed class FilteredToolCatalog(IToolCatalog inner) : IToolCatalog
{
    /// <summary>Tools that answer about Therion, ThIDE or the UI — never about the survey's contents.</summary>
    public static IReadOnlySet<string> DataFree { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "server_info", "describe_command", "explain_diagnostic", "search_thbook",
        "convert_coordinates", "convert_units", "get_declination",
        "list_commands", "run_command", "list_settings", "get_setting", "set_setting",
        "focus_tool", "show_toast", "set_layout", "reset_layout", "save_all",
    };

    public IReadOnlyList<ToolDescriptor> Tools => inner.Tools.Where(t => DataFree.Contains(t.Name)).ToList();

    public Task<ToolOutcome> CallAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct) =>
        DataFree.Contains(name)
            ? inner.CallAsync(name, arguments, ct)
            : Task.FromResult(new ToolOutcome($"Error: '{name}' is not available: the user keeps project data from the model.", false));
}

/// <summary>Passes every result through <see cref="ToolResultRedactor"/> (the Redact policy).</summary>
public sealed class RedactingToolCatalog(IToolCatalog inner) : IToolCatalog
{
    public IReadOnlyList<ToolDescriptor> Tools => inner.Tools;

    public async Task<ToolOutcome> CallAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var outcome = await inner.CallAsync(name, arguments, ct).ConfigureAwait(false);
        return outcome with { Content = ToolResultRedactor.Redact(outcome.Content) };
    }
}

/// <summary>
/// Best-effort removal of survey data from a tool result: the JSON keeps its shape, its keys, its
/// booleans, diagnostic codes and severities, and counts; other numbers become <c>#</c> and other text
/// <c>…</c>. A result that is not JSON is replaced by its length.
/// </summary>
public static class ToolResultRedactor
{
    private static readonly HashSet<string> KeptText = new(StringComparer.OrdinalIgnoreCase)
        { "code", "severity", "kind", "type", "status", "action", "tool", "dimension", "source", "unit", "units" };

    private static readonly string[] CountSuffixes = ["count", "total", "offset", "limit", "errors", "warnings", "line", "column", "edits", "newerrors", "newwarnings", "comparablepieces"];

    private static readonly JsonSerializerOptions Output = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Redact(string content)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(content); }
        catch (JsonException) { return $"(redacted: {content.Length} characters of text)"; }
        return node is null ? content : Walk(node, null)!.ToJsonString(Output);
    }

    private static JsonNode? Walk(JsonNode? node, string? key)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var k in o.Select(p => p.Key).ToList()) o[k] = Walk(o[k]?.DeepClone(), k);
                return o;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++) a[i] = Walk(a[i]?.DeepClone(), key);
                return a;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var s = v.GetValue<string>();
                return key is not null && KeptText.Contains(key) || s.StartsWith("TH", StringComparison.Ordinal) && s.Length <= 12
                    ? v : JsonValue.Create("…");
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                return key is not null && CountSuffixes.Any(c => key.EndsWith(c, StringComparison.OrdinalIgnoreCase)) ? v : JsonValue.Create("#");
            default:
                return node;
        }
    }
}
