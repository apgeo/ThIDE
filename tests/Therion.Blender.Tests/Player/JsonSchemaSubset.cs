// A validator for the part of JSON Schema (draft 2020-12) that scene-plan.schema.json uses, so
// the tests can hold plans against the shipped schema without a schema library. A keyword it does
// not know fails the validation: nothing in the schema goes unchecked without notice.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Therion.Blender.Tests.Player;

internal static class JsonSchemaSubset
{
    /// <summary>Keywords that say something about the schema and nothing about the document.</summary>
    private static readonly HashSet<string> Annotations = ["$schema", "$id", "$defs", "title", "description"];

    /// <summary>The problems of <paramref name="document"/> under <paramref name="schema"/>, each
    /// with the path of the value; empty when it conforms.</summary>
    public static IReadOnlyList<string> Validate(string schema, string document)
    {
        using var schemaDoc = JsonDocument.Parse(schema);
        using var doc = JsonDocument.Parse(document);
        var errors = new List<string>();
        Check(schemaDoc.RootElement, schemaDoc.RootElement, doc.RootElement, "$", errors);
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
                    else
                        Check(root, referenced, value, path, errors);
                    break;

                case "type":
                    if (!IsType(value, k.GetString()!)) errors.Add($"{path}: expected {k.GetString()}, found {value.ValueKind}");
                    break;

                case "const":
                    if (!JsonEquals(value, k)) errors.Add($"{path}: must be {k.GetRawText()}");
                    break;

                case "enum":
                    if (!k.EnumerateArray().Any(option => JsonEquals(value, option)))
                        errors.Add($"{path}: {value.GetRawText()} is not one of {k.GetRawText()}");
                    break;

                case "required":
                    if (value.ValueKind == JsonValueKind.Object)
                        foreach (var name in k.EnumerateArray())
                            if (!value.TryGetProperty(name.GetString()!, out _))
                                errors.Add($"{path}: '{name.GetString()}' is missing");
                    break;

                case "properties":
                    if (value.ValueKind == JsonValueKind.Object)
                        foreach (var property in k.EnumerateObject())
                            if (value.TryGetProperty(property.Name, out var child))
                                Check(root, property.Value, child, $"{path}.{property.Name}", errors);
                    break;

                case "additionalProperties":
                    if (k.ValueKind != JsonValueKind.False)
                        errors.Add($"{path}: the validator knows additionalProperties only as false");
                    else if (value.ValueKind == JsonValueKind.Object)
                        foreach (var property in value.EnumerateObject())
                            if (!schema.TryGetProperty("properties", out var known) || !known.TryGetProperty(property.Name, out _))
                                errors.Add($"{path}: '{property.Name}' is not a known property");
                    break;

                case "items":
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        int index = 0;
                        foreach (var item in value.EnumerateArray())
                            Check(root, k, item, $"{path}[{index++}]", errors);
                    }
                    break;

                case "minProperties":
                    if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() < k.GetInt32())
                        errors.Add($"{path}: fewer than {k.GetInt32()} properties");
                    break;

                case "maxProperties":
                    if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() > k.GetInt32())
                        errors.Add($"{path}: more than {k.GetInt32()} properties");
                    break;

                case "minItems":
                    if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() < k.GetInt32())
                        errors.Add($"{path}: fewer than {k.GetInt32()} items");
                    break;

                case "maxItems":
                    if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > k.GetInt32())
                        errors.Add($"{path}: more than {k.GetInt32()} items");
                    break;

                case "uniqueItems":
                    if (k.ValueKind == JsonValueKind.True && value.ValueKind == JsonValueKind.Array)
                    {
                        var seen = value.EnumerateArray().Select(item => item.GetRawText()).ToList();
                        if (seen.Distinct(StringComparer.Ordinal).Count() != seen.Count) errors.Add($"{path}: an item is repeated");
                    }
                    break;

                case "minimum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() < k.GetDouble())
                        errors.Add($"{path}: below {k.GetRawText()}");
                    break;

                case "maximum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() > k.GetDouble())
                        errors.Add($"{path}: above {k.GetRawText()}");
                    break;

                case "exclusiveMinimum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() <= k.GetDouble())
                        errors.Add($"{path}: not above {k.GetRawText()}");
                    break;

                case "minLength":
                    if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length < k.GetInt32())
                        errors.Add($"{path}: shorter than {k.GetInt32()} characters");
                    break;

                case "pattern":
                    if (value.ValueKind == JsonValueKind.String && !Regex.IsMatch(value.GetString()!, k.GetString()!, RegexOptions.ECMAScript))
                        errors.Add($"{path}: '{value.GetString()}' does not match {k.GetString()}");
                    break;

                default:
                    if (!Annotations.Contains(keyword.Name))
                        errors.Add($"{path}: the validator does not know the schema keyword '{keyword.Name}'");
                    break;
            }
        }
    }

    private static bool IsType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.GetDouble() % 1 == 0,
        _ => false,
    };

    private static bool JsonEquals(JsonElement a, JsonElement b) => a.ValueKind switch
    {
        JsonValueKind.Number => b.ValueKind == JsonValueKind.Number && a.GetDouble() == b.GetDouble(),
        JsonValueKind.String => b.ValueKind == JsonValueKind.String && a.GetString() == b.GetString(),
        _ => a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText(),
    };
}
