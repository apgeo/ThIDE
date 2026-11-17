// scene-plan.json reading and writing. camelCase keys, enums as camelCase words, vectors and
// colours as [x, y, z] and [r, g, b]. A camera pose and a colour stop take one line each, so a
// track of a thousand poses is a thousand lines a person can still read and compare. Lines end
// in LF on every platform, and numbers are written the same under every culture, so the same plan
// is the same bytes everywhere.
//
// Reading checks the version first: a plan newer than this build is refused with a message that
// says so. Unknown keys inside a known section are ignored; unknown sections are kept
// (ScenePlan.Sections).

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new VectorConverter(),
            new ColorConverter(),
            new CameraKeyConverter(),
            new ColorStopConverter(),
        },
    };

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
            return version;
        }
        catch (JsonException ex)
        {
            throw new ScenePlanFormatException($"Malformed scene plan JSON: {ex.Message}");
        }
    }

    // ---- compact values ----

    // The writer puts a value written as raw text straight after the one before it. A pose and a
    // colour stop bring their own line break, and the indentation of the place they have in the
    // document: camera.track[n] and look.materials[n].stops[n].
    private const string TrackIndent = "\n      ";
    private const string StopsIndent = "\n          ";

    private static string Num(double value)
    {
        if (!double.IsFinite(value))
            throw new JsonException("A scene plan cannot hold a number that is not finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Triple(double a, double b, double c) => $"[{Num(a)}, {Num(b)}, {Num(c)}]";

    private static string Text(CaveVector3 v) => Triple(v.X, v.Y, v.Z);

    private static string Text(ColorRgb c) => Triple(c.R, c.G, c.B);

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
}
