using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Evals;

/// <param name="Client">The live MCP client — the grader computes ground truth by calling the tools itself.</param>
/// <param name="FinalText">The model's last (non-tool) message.</param>
/// <param name="Calls">Every tool call the model made this run.</param>
/// <param name="WorkspaceDir">The per-run working copy of the fixture (mutations land here).</param>
public sealed record GradeInput(
    McpClient Client, string FinalText, IReadOnlyList<ToolCallRecord> Calls, string WorkspaceDir);

/// <summary>
/// Turns a <see cref="Check"/> + a finished run into pass/fail, deterministically (D-011). The
/// exact-number checks recompute the answer from the server, so a fixture author never has to hard-code a
/// number that could go stale.
/// </summary>
public static class Grader
{
    public static async Task<(bool Passed, string Detail)> GradeAsync(Check check, GradeInput input, CancellationToken ct)
    {
        switch (check)
        {
            case LintClean:
            {
                var data = await CallDataAsync(input.Client, "get_diagnostics",
                    new() { ["minSeverity"] = "error" }, ct);
                int errors = data is { } d && d.TryGetProperty("total", out var t) ? t.GetInt32() : -1;
                return (errors == 0, $"error diagnostics: {errors}");
            }

            case AnswerContains ac:
            {
                var missing = ac.Tokens.Where(tok => !ContainsToken(input.FinalText, tok)).ToList();
                return (missing.Count == 0, missing.Count == 0 ? "all tokens present" : $"missing: {string.Join(", ", missing)}");
            }

            case AnswerMatchesComputed amc:
            {
                var data = await CallDataAsync(input.Client, amc.Tool, ToArgs(amc.Args), ct);
                if (Resolve(data, amc.Pointer) is not { } value)
                    return (false, $"could not compute {amc.Tool}{amc.Pointer}");
                var expected = ValueString(value);
                return (ContainsToken(input.FinalText, expected), $"expected '{expected}' in the answer");
            }

            case FileExists fe:
            {
                var path = Path.Combine(input.WorkspaceDir, fe.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                return (File.Exists(path), File.Exists(path) ? "file present" : $"missing file: {fe.RelativePath}");
            }

            case HandledGracefully:
            {
                bool inventedTool = input.Calls.Any(c => !c.SchemaValid);
                bool answered = !string.IsNullOrWhiteSpace(input.FinalText);
                return (!inventedTool && answered,
                    inventedTool ? "invented/invalid tool call" : answered ? "answered without inventing a tool" : "empty answer");
            }

            default:
                return (false, $"no grader for {check.GetType().Name}");
        }
    }

    private static bool ContainsToken(string haystack, string token) =>
        !string.IsNullOrEmpty(token) && haystack.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object> ToArgs(IReadOnlyDictionary<string, object?>? args) =>
        args is null ? new() : args.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);

    /// <summary>Calls a tool and returns its <c>data</c> element (the envelope is <c>{ok,data}</c>).</summary>
    private static async Task<JsonElement?> CallDataAsync(
        McpClient client, string tool, Dictionary<string, object> args, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: ct);
        var json = EnvelopeText(result);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.TryGetProperty("data", out var data) ? data.Clone() : root.Clone();
        }
        catch (JsonException) { return null; }
    }

    private static string? EnvelopeText(CallToolResult result)
    {
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (!string.IsNullOrWhiteSpace(text)) return text;
        return result.StructuredContent?.GetRawText();
    }

    /// <summary>Minimal JSON Pointer (RFC 6901) resolution: "/a/b" walks object properties.</summary>
    private static JsonElement? Resolve(JsonElement? root, string pointer)
    {
        if (root is not { } element) return null;
        foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var next))
                return null;
            element = next;
        }
        return element;
    }

    private static string ValueString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };
}
