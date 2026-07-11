// Preset JSON (de)serialization + versioning (BA-B9). Mirrors SceneSpecSerializer:
// camelCase keys, string enums, an envelope `version` gate (newer-than-us fails clearly,
// older runs the migration hook — none yet) plus the nested SceneSpec's own version gate.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Therion.Blender.Presets;

/// <summary>Thrown when preset JSON is malformed or its version is unusable.</summary>
public sealed class PresetFormatException(string message) : Exception(message);

/// <summary>Serializes, deserializes and version-gates <see cref="RenderPreset"/>s.</summary>
public static class PresetSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(RenderPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return JsonSerializer.Serialize(preset, Options);
    }

    public static void WriteFile(RenderPreset preset, string path)
        => File.WriteAllText(path, Write(preset), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Parses preset JSON, gating on the envelope version (see file header).</summary>
    public static RenderPreset Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        int version = ProbeVersion(json);
        if (version > BlenderModule.PresetSchemaVersion)
            throw new PresetFormatException(
                $"This preset is version {version}, newer than this build understands " +
                $"({BlenderModule.PresetSchemaVersion}). Update ThIDE to open it.");
        if (version < 1)
            throw new PresetFormatException($"Invalid preset version {version}.");

        // Migration hook: upgrade older envelopes here before binding when v2 lands.
        try
        {
            var preset = JsonSerializer.Deserialize<RenderPreset>(json, Options)
                         ?? throw new PresetFormatException("Preset deserialized to null.");
            if (preset.Spec is null)
                throw new PresetFormatException("Preset has no spec.");
            if (string.IsNullOrWhiteSpace(preset.Name))
                throw new PresetFormatException("Preset has no name.");
            return preset;
        }
        catch (JsonException ex)
        {
            throw new PresetFormatException($"Malformed preset JSON: {ex.Message}");
        }
    }

    public static RenderPreset ReadFile(string path) => Read(File.ReadAllText(path));

    private static int ProbeVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new PresetFormatException("Preset JSON must be an object.");
            if (!doc.RootElement.TryGetProperty("version", out var versionElement))
                throw new PresetFormatException("Preset JSON has no \"version\" field.");
            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
                throw new PresetFormatException("Preset \"version\" must be an integer.");
            return version;
        }
        catch (JsonException ex)
        {
            throw new PresetFormatException($"Malformed preset JSON: {ex.Message}");
        }
    }
}
