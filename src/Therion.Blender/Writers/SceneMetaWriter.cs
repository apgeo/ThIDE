// scene-meta.json serializer (BA-B3). System.Text.Json always writes numbers with the
// invariant format regardless of thread culture (R-08), so no manual float formatting
// is needed. camelCase keys; indented for human inspection and stable golden tests.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Therion.Blender.Writers;

/// <summary>Serializes a <see cref="SceneMeta"/> to <c>scene-meta.json</c>.</summary>
public static class SceneMetaWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Emit the literal characters (diacritics in station/survey names) rather than
        // \uXXXX escapes — the file is UTF-8 and this keeps it readable and compact.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(SceneMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        return JsonSerializer.Serialize(meta, Options);
    }

    public static void WriteFile(SceneMeta meta, string path)
        => File.WriteAllText(path, Write(meta), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Parses a document back (used by tests and the future self-contained mode).</summary>
    public static SceneMeta Read(string json)
        => JsonSerializer.Deserialize<SceneMeta>(json, Options)
           ?? throw new InvalidOperationException("scene-meta.json deserialized to null.");
}
