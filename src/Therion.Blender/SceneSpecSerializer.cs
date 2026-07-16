// SceneSpec JSON (de)serialization + versioning (BA-B5). camelCase keys, enums as
// their C# names (case-insensitive on read). Presets (BA-B9) and the CLI (BA-B14)
// speak this format. Reading gates on the version field: newer-than-us fails with a
// clear message; older versions run through the migration hook (none exist yet — v1
// is the first schema). The canonical (non-indented) bytes feed the SHA-256 spec
// hash the generated script prints (`THIDE:spec-hash=`).

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Therion.Blender;

/// <summary>Thrown when spec JSON is malformed or its version is unusable.</summary>
public sealed class SceneSpecFormatException(string message) : Exception(message);

/// <summary>Serializes, deserializes, migrates and hashes <see cref="SceneSpec"/>s.</summary>
public static class SceneSpecSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        // Diacritics in names/paths stay readable — the file is UTF-8.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes the spec (indented, camelCase, string enums).</summary>
    public static string Write(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return JsonSerializer.Serialize(spec, Options);
    }

    public static void WriteFile(SceneSpec spec, string path)
        => File.WriteAllText(path, Write(spec), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Parses spec JSON, applying version gating/migration (see file header).</summary>
    public static SceneSpec Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        int version = ProbeVersion(json);
        if (version > BlenderModule.SceneSpecSchemaVersion)
            throw new SceneSpecFormatException(
                $"This render spec is version {version}, newer than this build understands " +
                $"({BlenderModule.SceneSpecSchemaVersion}). Update ThIDE to open it.");
        if (version < 1)
            throw new SceneSpecFormatException($"Invalid render-spec version {version}.");

        // Migration hook: when v2 lands, upgrade the JSON here before binding.
        try
        {
            return JsonSerializer.Deserialize<SceneSpec>(json, Options)
                   ?? throw new SceneSpecFormatException("Render spec deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new SceneSpecFormatException($"Malformed render spec JSON: {ex.Message}");
        }
    }

    public static SceneSpec ReadFile(string path) => Read(File.ReadAllText(path));

    /// <summary>Lowercase-hex SHA-256 of the canonical spec bytes — stable across
    /// runs/platforms, printed by the generated script (<c>THIDE:spec-hash=</c>).</summary>
    public static string ComputeHash(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(spec, CanonicalOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static int ProbeVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new SceneSpecFormatException("Render spec JSON must be an object.");
            if (!doc.RootElement.TryGetProperty("version", out var versionElement))
                throw new SceneSpecFormatException("Render spec JSON has no \"version\" field.");
            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
                throw new SceneSpecFormatException("Render spec \"version\" must be an integer.");
            return version;
        }
        catch (JsonException ex)
        {
            throw new SceneSpecFormatException($"Malformed render spec JSON: {ex.Message}");
        }
    }
}
