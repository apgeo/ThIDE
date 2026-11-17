// The JSON schema of scene-plan.json, for programs that write a plan without this library.

using System.Reflection;

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
}
