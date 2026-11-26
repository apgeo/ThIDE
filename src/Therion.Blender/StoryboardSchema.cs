// The JSON schema of a storyboard file, as it lies in the assembly.

namespace Therion.Blender;

/// <summary>The JSON schema of <see cref="Storyboard"/> files (<c>*.storyboard.json</c>).</summary>
public static class StoryboardSchema
{
    /// <summary>The name a copy of the schema is given.</summary>
    public const string FileName = "storyboard.schema.json";

    private static readonly Lazy<string> Loaded = new(() =>
    {
        using var stream = typeof(StoryboardSchema).Assembly.GetManifestResourceStream("storyboard/" + FileName)
                           ?? throw new InvalidOperationException("The storyboard schema is not in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    });

    /// <summary>The schema text.</summary>
    public static string Text => Loaded.Value;
}
