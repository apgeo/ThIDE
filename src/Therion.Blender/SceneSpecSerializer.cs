// SceneSpec JSON (de)serialization + versioning (BA-B5). camelCase keys, enums as
// their C# names (case-insensitive on read). Presets (BA-B9) and the CLI (BA-B14)
// speak this format. Reading gates on the version field: newer-than-us fails with a
// clear message; older versions run through the migration hook (none exist yet — v1
// is the first schema). The canonical (non-indented) bytes feed the SHA-256 spec
// hash the generated script prints (`THIDE:spec-hash=`).

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteLaterTextsOnlyWhenSet } },
    };

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteLaterTextsOnlyWhenSet } },
    };

    /// <summary>The credits and corner lines, the end card, the captions, the title's subtitle
    /// and time, and the fades were added to the overlays after the format's first release. They
    /// are written only when a spec has them, so a spec without them is the same JSON, and has
    /// the same hash, as before they existed.</summary>
    private static void WriteLaterTextsOnlyWhenSet(JsonTypeInfo type)
    {
        // The annotations came later still, and are written under the same rule.
        if (type.Type == typeof(SceneSpec))
        {
            foreach (var property in type.Properties)
                if (property.Name == "annotations")
                    property.ShouldSerialize = (_, value) => value is IReadOnlyList<AnnotationSpec> { Count: > 0 };
        }
        if (type.Type == typeof(AnnotationSpec))
        {
            // An annotation says what it has; what it leaves to the defaults is not written.
            foreach (var property in type.Properties)
                property.ShouldSerialize = property.Name switch
                {
                    "kind" => null,
                    "facing" => (_, value) => value is PhotoFacing.Fixed,
                    "framed" => (_, value) => value is true,
                    "fade" => (_, value) => value is not 0.5,
                    _ => (_, value) => value is not null,
                };
        }
        if (type.Type == typeof(EndCardSpec))
        {
            foreach (var property in type.Properties)
            {
                if (property.Name == "subtitle") property.ShouldSerialize = (_, value) => value is not null;
                else if (property.Name == "backdrop") property.ShouldSerialize = (_, value) => value is not 1.0;
                else if (property.Name == "overScene") property.ShouldSerialize = (_, value) => value is true;
            }
        }
        if (type.Type != typeof(OverlaySpec)) return;
        foreach (var property in type.Properties)
        {
            if (property.Name is "credits" or "endCard" or "subtitle" or "titleSeconds" or "corner")
                property.ShouldSerialize = (_, value) => value is not null;
            else if (property.Name is "captions" or "cornerCaptions")
                property.ShouldSerialize = (_, value) => value is IReadOnlyList<CaptionSpec> { Count: > 0 };
            else if (property.Name is "fadeFromBlack" or "fadeToBlack")
                property.ShouldSerialize = (_, value) => value is not 0.0;
        }
    }

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
            // The text is JSON (ProbeVersion read it): what is wrong is its shape, which the
            // reader names by a key or a kind of value, in a line.
            throw new SceneSpecFormatException($"Malformed render spec JSON: {JsonFault.OfShape(ex)}");
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
            // Where, and not the parser's own words: it quotes what it could not read, whatever
            // file it was that somebody took for a spec.
            throw new SceneSpecFormatException($"Malformed render spec JSON: {JsonFault.NotJson(ex)}");
        }
    }
}
