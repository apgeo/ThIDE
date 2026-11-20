// The JSON schema of scene-plan.json, for programs that write a plan without this library.

using System.Text.Json;

namespace Therion.Blender.Plan;

/// <summary>The JSON schema (draft 2020-12) of the scene plan, as shipped in this assembly.</summary>
public static class ScenePlanSchema
{
    /// <summary>The name to give the schema when it is written to a file.</summary>
    public const string FileName = "scene-plan.schema.json";

    private static readonly Lazy<string> Loaded = new(() =>
    {
        using var stream = typeof(ScenePlanSchema).Assembly.GetManifestResourceStream("plan/" + FileName)
                           ?? throw new InvalidOperationException("The scene-plan schema is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    });

    /// <summary>The schema text.</summary>
    public static string Text => Loaded.Value;

    private static readonly Lazy<PlanKeys> LoadedKeys = new(() =>
    {
        using var schema = JsonDocument.Parse(Text);
        return KeysOf(schema.RootElement)!;
    });

    /// <summary>The keys the schema knows, from the plan's sections down: what a reader refuses
    /// anything else of.</summary>
    internal static PlanKeys Keys => LoadedKeys.Value;

    /// <summary>The keys of an object the schema describes, or of the objects in an array it
    /// describes; null for any other value.</summary>
    private static PlanKeys? KeysOf(JsonElement schema)
    {
        if (schema.TryGetProperty("properties", out var properties))
            return new PlanKeys(properties.EnumerateObject().ToDictionary(p => p.Name, p => KeysOf(p.Value), StringComparer.Ordinal));
        return schema.TryGetProperty("items", out var items) ? KeysOf(items) : null;
    }
}

/// <summary>The keys an object of a plan may have, each with the keys its own value may have
/// (null when it holds none). An array of objects has the keys of its objects.</summary>
internal sealed record PlanKeys(IReadOnlyDictionary<string, PlanKeys?> Keys);
