// Storyboard JSON: camelCase keys and enums by name (any case on read), as a render spec's are,
// since a shot is made of a render spec's records. A key that is not known is refused, at every
// depth: it may be what a newer version added. A shot's reveal is the object a render spec has,
// or one of the words "hold" and "none".

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Therion.Blender;

/// <summary>Thrown when storyboard JSON is malformed or its version is unusable.</summary>
public sealed class StoryboardFormatException(string message) : Exception(message);

/// <summary>Reads and writes <see cref="Storyboard"/>s as JSON.</summary>
public static class StoryboardSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(), new ShotRevealConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SceneSpecSerializer.WriteLaterTextsOnlyWhenSet, WriteOnlyWhatIsSaid } },
    };

    /// <summary>A transition says its time and its colour only when it has them: a cut has
    /// neither, and a dissolve no colour.</summary>
    private static void WriteOnlyWhatIsSaid(JsonTypeInfo type)
    {
        if (type.Type == typeof(TransitionSpec))
        {
            foreach (var property in type.Properties)
                if (property.Name == "seconds")
                    property.ShouldSerialize = (owner, _) => ((TransitionSpec)owner).Kind != TransitionKind.Cut;
                else if (property.Name == "color")
                    property.ShouldSerialize = (owner, _) => ((TransitionSpec)owner).Kind == TransitionKind.Dip;
        }
        else if (type.Type == typeof(StoryboardDefaults))
        {
            foreach (var property in type.Properties)
                if (property.Name == "seed") property.ShouldSerialize = (_, value) => value is not 1;
        }
    }

    public static string Write(Storyboard storyboard)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        return JsonSerializer.Serialize(storyboard, Options).ReplaceLineEndings("\n") + "\n";
    }

    public static void WriteFile(Storyboard storyboard, string path)
        => File.WriteAllText(path, Write(storyboard), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Parses storyboard JSON. The version is checked first: one newer than this build
    /// is refused with a message that says so.</summary>
    /// <exception cref="StoryboardFormatException">The text is not a storyboard this build reads.</exception>
    public static Storyboard Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        int version = ProbeVersion(json);
        if (version > BlenderModule.StoryboardSchemaVersion)
            throw new StoryboardFormatException(
                $"This storyboard is version {version}, newer than this build understands " +
                $"({BlenderModule.StoryboardSchemaVersion}). Update ThIDE to open it.");
        if (version < 1)
            throw new StoryboardFormatException($"Invalid storyboard version {version}.");
        try
        {
            return JsonSerializer.Deserialize<Storyboard>(json, Options)
                   ?? throw new StoryboardFormatException("The storyboard deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new StoryboardFormatException($"Malformed storyboard JSON: {JsonFault.OfShape(ex)}");
        }
    }

    public static Storyboard ReadFile(string path) => Read(File.ReadAllText(path));

    private static int ProbeVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new StoryboardFormatException("Storyboard JSON must be an object.");
            if (!doc.RootElement.TryGetProperty("version", out var versionElement))
                throw new StoryboardFormatException("Storyboard JSON has no \"version\" field.");
            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
                throw new StoryboardFormatException("Storyboard \"version\" must be an integer.");
            return version;
        }
        catch (JsonException ex)
        {
            throw new StoryboardFormatException($"Malformed storyboard JSON: {JsonFault.NotJson(ex)}");
        }
    }

    private sealed class ShotRevealConverter : JsonConverter<ShotReveal>
    {
        public override ShotReveal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string word = reader.GetString() ?? "";
                if (word.Equals("hold", StringComparison.OrdinalIgnoreCase)) return ShotReveal.Hold;
                if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) return ShotReveal.None;
                throw new JsonException($"A shot's reveal is \"hold\", \"none\" or a reveal, not \"{word}\".");
            }
            var spec = JsonSerializer.Deserialize<RevealSpec>(ref reader, options) ?? throw new JsonException("Expected a reveal.");
            return ShotReveal.Of(spec);
        }

        public override void Write(Utf8JsonWriter writer, ShotReveal value, JsonSerializerOptions options)
        {
            switch (value.Mode)
            {
                case ShotRevealMode.Hold: writer.WriteStringValue("hold"); break;
                case ShotRevealMode.None: writer.WriteStringValue("none"); break;
                default: JsonSerializer.Serialize(writer, value.Spec ?? new RevealSpec(), options); break;
            }
        }
    }
}
