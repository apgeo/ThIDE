// A receiver's check of a render node message against render-node.schema.json: the keywords the
// schema uses, with additionalProperties not enforced (the protocol says receivers ignore unknown
// properties; the schema's closed objects describe what a sender writes). A keyword it does not
// know is a problem, so nothing in the schema goes unchecked without notice.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Therion.Blender;

internal static class RenderNodeValidator
{
    private static readonly HashSet<string> Annotations = ["$schema", "$id", "$defs", "title", "description", "additionalProperties"];

    public static IReadOnlyList<string> Validate(string schema, string def, string json)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return [$"$: not JSON ({ex.Message})"]; }
        using (doc)
        using (var schemaDoc = JsonDocument.Parse(schema))
        {
            var root = schemaDoc.RootElement;
            Check(root, root.GetProperty("$defs").GetProperty(def), doc.RootElement, "$", errors);
        }
        return errors;
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        foreach (var keyword in schema.EnumerateObject())
        {
            var k = keyword.Value;
            switch (keyword.Name)
            {
                case "$ref":
                    string target = k.GetString()!;
                    const string defs = "#/$defs/";
                    if (!target.StartsWith(defs, StringComparison.Ordinal)
                        || !root.GetProperty("$defs").TryGetProperty(target[defs.Length..], out var referenced))
                        errors.Add($"{path}: the schema refers to '{target}', which is not there");
                    else Check(root, referenced, value, path, errors);
                    break;
                case "type":
                    if (!IsType(value, k.GetString()!)) errors.Add($"{path}: expected {k.GetString()}, found {value.ValueKind}");
                    break;
                case "const":
                    if (!Same(value, k)) errors.Add($"{path}: must be {k.GetRawText()}");
                    break;
                case "enum":
                    if (!k.EnumerateArray().Any(o => Same(value, o))) errors.Add($"{path}: {value.GetRawText()} is not one of {k.GetRawText()}");
                    break;
                case "required":
                    if (value.ValueKind == JsonValueKind.Object)
                        foreach (var name in k.EnumerateArray())
                            if (!value.TryGetProperty(name.GetString()!, out _)) errors.Add($"{path}: '{name.GetString()}' is missing");
                    break;
                case "properties":
                    if (value.ValueKind == JsonValueKind.Object)
                        foreach (var property in k.EnumerateObject())
                            if (value.TryGetProperty(property.Name, out var child))
                                Check(root, property.Value, child, $"{path}.{property.Name}", errors);
                    break;
                case "items":
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        int index = 0;
                        foreach (var item in value.EnumerateArray()) Check(root, k, item, $"{path}[{index++}]", errors);
                    }
                    break;
                case "minItems":
                    if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() < k.GetInt32()) errors.Add($"{path}: fewer than {k.GetInt32()} items");
                    break;
                case "minimum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() < k.GetDouble()) errors.Add($"{path}: below {k.GetRawText()}");
                    break;
                case "exclusiveMinimum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() <= k.GetDouble()) errors.Add($"{path}: not above {k.GetRawText()}");
                    break;
                case "minLength":
                    if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length < k.GetInt32()) errors.Add($"{path}: shorter than {k.GetInt32()} characters");
                    break;
                case "pattern":
                    if (value.ValueKind == JsonValueKind.String && !Regex.IsMatch(value.GetString()!, AsEcma(k.GetString()!), RegexOptions.ECMAScript))
                        errors.Add($"{path}: '{value.GetString()}' does not match {k.GetString()}");
                    break;
                default:
                    if (!Annotations.Contains(keyword.Name)) errors.Add($"{path}: the validator does not know the schema keyword '{keyword.Name}'");
                    break;
            }
        }
    }

    /// <summary>
    /// A schema pattern as .NET reads it with ECMA-262's end of input. Without the multiline
    /// flag, ECMA-262's <c>$</c> matches at the end only; .NET's matches before a final line
    /// break as well, so that <c>"job-1\n"</c> would pass <c>^…$</c>. Every <c>$</c> outside a
    /// character class is held to the end.
    /// </summary>
    internal static string AsEcma(string pattern)
    {
        var text = new System.Text.StringBuilder(pattern.Length + 8);
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                text.Append(c).Append(pattern[++i]);
                continue;
            }
            if (inClass) inClass = c != ']';
            else if (c == '[') inClass = true;
            else if (c == '$')
            {
                text.Append("$(?!\n)");
                continue;
            }
            text.Append(c);
        }
        return text.ToString();
    }

    private static bool IsType(JsonElement v, string type) => type switch
    {
        "object" => v.ValueKind == JsonValueKind.Object,
        "array" => v.ValueKind == JsonValueKind.Array,
        "string" => v.ValueKind == JsonValueKind.String,
        "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => v.ValueKind == JsonValueKind.Number,
        "integer" => v.ValueKind == JsonValueKind.Number && v.GetDouble() % 1 == 0,
        _ => false,
    };

    private static bool Same(JsonElement a, JsonElement b) => a.ValueKind switch
    {
        JsonValueKind.Number => b.ValueKind == JsonValueKind.Number && a.GetDouble() == b.GetDouble(),
        JsonValueKind.String => b.ValueKind == JsonValueKind.String && a.GetString() == b.GetString(),
        _ => a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText(),
    };
}
