// scene-plan.json reading and writing. camelCase keys, enums as camelCase words, vectors and
// colours as [x, y, z] and [r, g, b]. A camera pose, a colour stop, a text and a marker take one
// line each, so a track of a thousand poses is a thousand lines a person can still read and
// compare. Lines end
// in LF on every platform, and numbers are written the same under every culture, so the same plan
// is the same bytes everywhere.
//
// Reading checks the version first: a plan newer than this build is refused with a message that
// says so. An unknown key inside a known section is refused too, as the player refuses it: it may
// be what a newer version added. Unknown sections are kept (ScenePlan.Sections): they belong to
// modules outside this library.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Therion.Cave3D;

namespace Therion.Blender.Plan;

/// <summary>Thrown when plan JSON is malformed or its version is unusable.</summary>
public sealed class ScenePlanFormatException(string message) : Exception(message);

/// <summary>Reads and writes <see cref="ScenePlan"/>s as JSON.</summary>
public static class ScenePlanSerializer
{
    /// <summary>The name a plan has beside its player.</summary>
    public const string FileName = "scene-plan.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Names with diacritics stay readable; the file is UTF-8.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteRenderOnlyWhenOff } },
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new VectorConverter(),
            new ColorConverter(),
            new CameraKeyConverter(),
            new ColorStopConverter(),
            new TextConverter(),
            new MarkerConverter(),
        },
    };

    /// <summary>An output renders unless it says otherwise, so "render" is written only when
    /// it is false.</summary>
    private static void WriteRenderOnlyWhenOff(JsonTypeInfo type)
    {
        if (type.Type != typeof(PlanOutput)) return;
        foreach (var property in type.Properties)
            if (property.Name == "render") property.ShouldSerialize = (_, value) => value is false;
    }

    public static string Write(ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, Options).ReplaceLineEndings("\n") + "\n";
    }

    public static void WriteFile(ScenePlan plan, string path)
        => File.WriteAllText(path, Write(plan), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static ScenePlan Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        int version = ProbeVersion(json);
        if (version > BlenderModule.ScenePlanSchemaVersion)
            throw new ScenePlanFormatException(
                $"This scene plan is version {version}, newer than this build understands " +
                $"({BlenderModule.ScenePlanSchemaVersion}).");
        if (version < 1)
            throw new ScenePlanFormatException($"Invalid scene-plan version {version}.");

        try
        {
            return JsonSerializer.Deserialize<ScenePlan>(json, Options)
                   ?? throw new ScenePlanFormatException("The scene plan deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ScenePlanFormatException($"Malformed scene plan JSON: {ex.Message}");
        }
    }

    public static ScenePlan ReadFile(string path) => Read(File.ReadAllText(path));

    private static int ProbeVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ScenePlanFormatException("Scene plan JSON must be an object.");
            if (!doc.RootElement.TryGetProperty("version", out var versionElement))
                throw new ScenePlanFormatException("Scene plan JSON has no \"version\" field.");
            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
                throw new ScenePlanFormatException("Scene plan \"version\" must be an integer.");
            if (version == BlenderModule.ScenePlanSchemaVersion && UnknownKey(doc.RootElement) is { } unknown)
                throw new ScenePlanFormatException(
                    $"{unknown} is not known to this build (scene plan version {version}); a newer build may be needed.");
            return version;
        }
        catch (JsonException ex)
        {
            throw new ScenePlanFormatException($"Malformed scene plan JSON: {ex.Message}");
        }
    }

    /// <summary>The path of the first key inside a known section that the schema does not have,
    /// or null. The sections themselves may be any: those of other modules are kept.</summary>
    private static string? UnknownKey(JsonElement root)
    {
        foreach (var section in root.EnumerateObject())
            if (ScenePlanSchema.Keys.Keys.TryGetValue(section.Name, out var keys) && keys is not null
                && UnknownKey(section.Value, keys, section.Name) is { } path)
                return path;
        return null;
    }

    private static string? UnknownKey(JsonElement value, PlanKeys keys, string path)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in value.EnumerateArray())
                if (UnknownKey(item, keys, $"{path}[{index++}]") is { } found)
                    return found;
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Keys.TryGetValue(property.Name, out var inner))
                    return path + "." + property.Name;
                if (inner is not null && UnknownKey(property.Value, inner, path + "." + property.Name) is { } found)
                    return found;
            }
        }
        return null;
    }

    // ---- compact values ----

    // The writer puts a value written as raw text straight after the one before it. A pose, a
    // colour stop and a label bring their own line break, and the indentation of the place they
    // have in the document: camera.track[n], look.materials[n].stops[n] and labels.*.items[n].
    private const string TrackIndent = "\n      ";
    private const string StopsIndent = "\n          ";
    private const string LabelIndent = "\n        ";

    private static string Num(double value)
    {
        if (!double.IsFinite(value))
            throw new JsonException("A scene plan cannot hold a number that is not finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Triple(double a, double b, double c) => $"[{Num(a)}, {Num(b)}, {Num(c)}]";

    private static string Text(CaveVector3 v) => Triple(v.X, v.Y, v.Z);

    private static string Text(ColorRgb c) => Triple(c.R, c.G, c.B);

    /// <summary>A text as a JSON string, escaped as the rest of the document is.</summary>
    private static string Quoted(string text, JsonSerializerOptions options) => JsonSerializer.Serialize(text, options);

    private static (double, double, double) ReadTriple(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of three numbers.");
        Span<double> values = stackalloc double[3];
        for (int i = 0; i < 3; i++)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("Expected an array of three numbers.");
            values[i] = reader.GetDouble();
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Expected an array of three numbers.");
        return (values[0], values[1], values[2]);
    }

    private sealed class VectorConverter : JsonConverter<CaveVector3>
    {
        public override CaveVector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var (x, y, z) = ReadTriple(ref reader);
            return new CaveVector3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, CaveVector3 value, JsonSerializerOptions options)
            => writer.WriteRawValue(Text(value));
    }

    private sealed class ColorConverter : JsonConverter<ColorRgb>
    {
        public override ColorRgb Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var (r, g, b) = ReadTriple(ref reader);
            return new ColorRgb(r, g, b);
        }

        public override void Write(Utf8JsonWriter writer, ColorRgb value, JsonSerializerOptions options)
            => writer.WriteRawValue(Text(value));
    }

    private sealed record KeyJson
    {
        public required double T { get; init; }
        public required CaveVector3 Eye { get; init; }
        public required CaveVector3 Target { get; init; }
        public double? Lens { get; init; }
    }

    private sealed class CameraKeyConverter : JsonConverter<PlanCameraKey>
    {
        public override PlanCameraKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<KeyJson>(ref reader, options) ?? throw new JsonException("Expected a camera pose.");
            return new PlanCameraKey(key.T, key.Eye, key.Target, key.Lens);
        }

        public override void Write(Utf8JsonWriter writer, PlanCameraKey value, JsonSerializerOptions options)
        {
            string lens = value.Lens is { } l ? $", \"lens\": {Num(l)}" : "";
            writer.WriteRawValue(
                $"{TrackIndent}{{ \"t\": {Num(value.T)}, \"eye\": {Text(value.Eye)}, \"target\": {Text(value.Target)}{lens} }}");
        }
    }

    private sealed record StopJson
    {
        public required double At { get; init; }
        public required ColorRgb Color { get; init; }
    }

    private sealed class ColorStopConverter : JsonConverter<PlanColorStop>
    {
        public override PlanColorStop Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var stop = JsonSerializer.Deserialize<StopJson>(ref reader, options) ?? throw new JsonException("Expected a colour stop.");
            return new PlanColorStop(stop.At, stop.Color);
        }

        public override void Write(Utf8JsonWriter writer, PlanColorStop value, JsonSerializerOptions options)
            => writer.WriteRawValue($"{StopsIndent}{{ \"at\": {Num(value.At)}, \"color\": {Text(value.Color)} }}");
    }

    private sealed record TextJson
    {
        public required string Id { get; init; }
        public string? Role { get; init; }
        public required string Text { get; init; }
        public required CaveVector3 At { get; init; }
        public required double Size { get; init; }
    }

    private sealed class TextConverter : JsonConverter<PlanText>
    {
        public override PlanText Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var text = JsonSerializer.Deserialize<TextJson>(ref reader, options) ?? throw new JsonException("Expected a text.");
            return new PlanText(text.Id, text.Text, text.At, text.Size, text.Role);
        }

        public override void Write(Utf8JsonWriter writer, PlanText value, JsonSerializerOptions options)
        {
            string role = value.Role is { } r ? $", \"role\": {Quoted(r, options)}" : "";
            writer.WriteRawValue(
                $"{LabelIndent}{{ \"id\": {Quoted(value.Id, options)}{role}, \"text\": {Quoted(value.Text, options)}, " +
                $"\"at\": {Text(value.At)}, \"size\": {Num(value.Size)} }}");
        }
    }

    private sealed record MarkerJson
    {
        public required string Id { get; init; }
        public string? Role { get; init; }
        public required CaveVector3 At { get; init; }
        public required double Radius { get; init; }
    }

    private sealed class MarkerConverter : JsonConverter<PlanMarker>
    {
        public override PlanMarker Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var marker = JsonSerializer.Deserialize<MarkerJson>(ref reader, options) ?? throw new JsonException("Expected a marker.");
            return new PlanMarker(marker.Id, marker.At, marker.Radius, marker.Role);
        }

        public override void Write(Utf8JsonWriter writer, PlanMarker value, JsonSerializerOptions options)
        {
            string role = value.Role is { } r ? $", \"role\": {Quoted(r, options)}" : "";
            writer.WriteRawValue(
                $"{LabelIndent}{{ \"id\": {Quoted(value.Id, options)}{role}, \"at\": {Text(value.At)}, \"radius\": {Num(value.Radius)} }}");
        }
    }
}
