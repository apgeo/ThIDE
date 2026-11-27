// scene-plan.json reading and writing. camelCase keys, enums as camelCase words, vectors and
// colours as [x, y, z] and [r, g, b]. A camera pose, a colour stop, a strip of a line, a text, a
// marker, a key of a change, a thing written over the picture and a turn of its arrow take one
// line each, so a track
// of a thousand poses is a thousand lines a person can still read and compare. Lines end
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
            new QuaternionConverter(),
            new CameraKeyConverter(),
            new ColorStopConverter(),
            new LineStripConverter(),
            new TextConverter(),
            new MarkerConverter(),
            new PointConverter(),
            new ScreenItemConverter(),
            new ChangeTargetConverter(),
            new VisibleKeyConverter(),
            new NumberKeyConverter(),
            new TintKeyConverter(),
            new VectorKeyConverter(),
            new ColorKeyConverter(),
        },
    };

    /// <summary>An output renders unless it says otherwise, so "render" is written only when
    /// it is false; the times of the texts as a whole are written only when they are not zero.</summary>
    private static void WriteRenderOnlyWhenOff(JsonTypeInfo type)
    {
        if (type.Type == typeof(PlanOutput))
        {
            foreach (var property in type.Properties)
                if (property.Name == "render") property.ShouldSerialize = (_, value) => value is false;
        }
        else if (type.Type == typeof(PlanScreenTexts))
        {
            foreach (var property in type.Properties)
                if (property.Name is "hold" or "fromBlack" or "toBlack") property.ShouldSerialize = (_, value) => value is not 0.0;
        }
        else if (type.Type == typeof(PlanRevealFront))
        {
            // The edge of a reveal is a cut unless it says otherwise.
            foreach (var property in type.Properties)
                if (property.Name == "edge") property.ShouldSerialize = (_, value) => value is not 0.0;
        }
        else if (type.Type == typeof(PlanMaterial))
        {
            // A material is lit unless it says otherwise.
            foreach (var property in type.Properties)
                if (property.Name is "unlit" or "banded") property.ShouldSerialize = (_, value) => value is true;
        }
        else if (type.Type == typeof(PlanTexturedMaterial))
        {
            // A model casts shadows unless its material says otherwise.
            foreach (var property in type.Properties)
                if (property.Name == "shadow") property.ShouldSerialize = (_, value) => value is false;
        }
        else if (type.Type == typeof(PlanCutaway))
        {
            // A line of a cut-away on a line of its own.
            foreach (var property in type.Properties)
                if (property.Name == "lines") property.CustomConverter = new CutawayLinesConverter();
        }
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
            // The text is JSON (ProbeVersion read it): what is wrong is its shape, which the
            // reader names by a key or a kind of value, in a line.
            throw new ScenePlanFormatException($"Malformed scene plan JSON: {JsonFault.OfShape(ex)}");
        }
    }

    public static ScenePlan ReadFile(string path) => Read(File.ReadAllText(path));

    // ---- the plan of a movie of several scenes ----

    /// <summary>The plan of a movie as JSON: a plan the player reads, written as a scene's is.</summary>
    public static string Write(MoviePlan movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return JsonSerializer.Serialize(movie, Options).ReplaceLineEndings("\n") + "\n";
    }

    public static void WriteFile(MoviePlan movie, string path)
        => File.WriteAllText(path, Write(movie), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Whether plan JSON is the plan of a movie of several scenes: its features name
    /// the module <see cref="MoviePlan.SegmentsFeature"/>. False for anything else, a text that
    /// is no plan too.</summary>
    public static bool IsMovie(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array
                   && features.EnumerateArray().Any(f => f.ValueKind == JsonValueKind.String && f.GetString() == MoviePlan.SegmentsFeature);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads the plan of a movie, as <see cref="Read"/> reads a scene's: the version is
    /// checked first, and a key that is not known is refused.</summary>
    public static MoviePlan ReadMovie(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        int version = ProbeVersion(json);
        if (version > BlenderModule.ScenePlanSchemaVersion)
            throw new ScenePlanFormatException(
                $"This plan is version {version}, newer than this build understands ({BlenderModule.ScenePlanSchemaVersion}).");
        if (version < 1)
            throw new ScenePlanFormatException($"Invalid scene-plan version {version}.");
        try
        {
            return JsonSerializer.Deserialize<MoviePlan>(json, Options)
                   ?? throw new ScenePlanFormatException("The plan deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ScenePlanFormatException($"Malformed plan JSON: {JsonFault.OfShape(ex)}");
        }
    }

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
            throw new ScenePlanFormatException($"Malformed scene plan JSON: {JsonFault.NotJson(ex)}");
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
    private const string StripIndent = "\n        ";
    private const string ScreenIndent = "\n      ";
    private const string TurnIndent = "\n        ";
    private const string ChangeKeyIndent = "\n        ";

    private static string Num(double value)
    {
        if (!double.IsFinite(value))
            throw new JsonException("A scene plan cannot hold a number that is not finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Triple(double a, double b, double c) => $"[{Num(a)}, {Num(b)}, {Num(c)}]";

    private static string Text(CaveVector3 v) => Triple(v.X, v.Y, v.Z);

    private static string Text(ColorRgb c) => Triple(c.R, c.G, c.B);

    private static string Text(PlanQuaternion q) => $"[{Num(q.W)}, {Num(q.X)}, {Num(q.Y)}, {Num(q.Z)}]";

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

    private sealed class QuaternionConverter : JsonConverter<PlanQuaternion>
    {
        public override PlanQuaternion Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            const string expected = "Expected an array of four numbers (w, x, y, z).";
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException(expected);
            Span<double> values = stackalloc double[4];
            for (int i = 0; i < 4; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException(expected);
                values[i] = reader.GetDouble();
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException(expected);
            return new PlanQuaternion(values[0], values[1], values[2], values[3]);
        }

        public override void Write(Utf8JsonWriter writer, PlanQuaternion value, JsonSerializerOptions options)
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
        public PlanQuaternion? Orientation { get; init; }
        public double? ClipStart { get; init; }
        public double? ClipEnd { get; init; }
    }

    private sealed class CameraKeyConverter : JsonConverter<PlanCameraKey>
    {
        public override PlanCameraKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<KeyJson>(ref reader, options) ?? throw new JsonException("Expected a camera pose.");
            return new PlanCameraKey(key.T, key.Eye, key.Target, key.Lens)
            {
                Orientation = key.Orientation, ClipStart = key.ClipStart, ClipEnd = key.ClipEnd,
            };
        }

        public override void Write(Utf8JsonWriter writer, PlanCameraKey value, JsonSerializerOptions options)
        {
            string lens = value.Lens is { } l ? $", \"lens\": {Num(l)}" : "";
            string orientation = value.Orientation is { } q ? $", \"orientation\": {Text(q)}" : "";
            string clip = (value.ClipStart is { } near ? $", \"clipStart\": {Num(near)}" : "")
                          + (value.ClipEnd is { } far ? $", \"clipEnd\": {Num(far)}" : "");
            writer.WriteRawValue(
                $"{TrackIndent}{{ \"t\": {Num(value.T)}, \"eye\": {Text(value.Eye)}, \"target\": {Text(value.Target)}{lens}{orientation}{clip} }}");
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

    private sealed record StripJson
    {
        public required List<CaveVector3> Points { get; init; }
        public List<ColorRgb>? Colors { get; init; }
        public List<double>? Reveal { get; init; }
    }

    private sealed class LineStripConverter : JsonConverter<PlanLineStrip>
    {
        public override PlanLineStrip Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var strip = JsonSerializer.Deserialize<StripJson>(ref reader, options) ?? throw new JsonException("Expected a strip of a line.");
            return new PlanLineStrip(strip.Points, strip.Colors) { Reveal = strip.Reveal };
        }

        public override void Write(Utf8JsonWriter writer, PlanLineStrip value, JsonSerializerOptions options)
        {
            var line = new StringBuilder(StripIndent);
            line.Append("{ \"points\": [").AppendJoin(", ", (value.Points ?? []).Select(Text)).Append(']');
            if (value.Colors is { } colors) line.Append(", \"colors\": [").AppendJoin(", ", colors.Select(Text)).Append(']');
            if (value.Reveal is { } reveal) line.Append(", \"reveal\": [").AppendJoin(", ", reveal.Select(Num)).Append(']');
            writer.WriteRawValue(line.Append(" }").ToString());
        }
    }

    private const string CutawayLineIndent = "\n          ";

    private sealed class CutawayLinesConverter : JsonConverter<IReadOnlyList<IReadOnlyList<CaveVector3>>>
    {
        public override IReadOnlyList<IReadOnlyList<CaveVector3>> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            const string expected = "Expected an array of lines, each an array of points.";
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException(expected);
            var lines = new List<IReadOnlyList<CaveVector3>>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException(expected);
                var points = new List<CaveVector3>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var (x, y, z) = ReadTriple(ref reader);
                    points.Add(new CaveVector3(x, y, z));
                }
                lines.Add(points);
            }
            return lines;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<IReadOnlyList<CaveVector3>> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var line in value)
                writer.WriteRawValue(new StringBuilder(CutawayLineIndent).Append('[').AppendJoin(", ", (line ?? []).Select(Text)).Append(']').ToString());
            writer.WriteEndArray();
        }
    }

    private sealed record TextJson
    {
        public required string Id { get; init; }
        public string? Role { get; init; }
        public required string Text { get; init; }
        public required CaveVector3 At { get; init; }
        public required double Size { get; init; }
        public string? Follows { get; init; }
    }

    private sealed class TextConverter : JsonConverter<PlanText>
    {
        public override PlanText Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var text = JsonSerializer.Deserialize<TextJson>(ref reader, options) ?? throw new JsonException("Expected a text.");
            return new PlanText(text.Id, text.Text, text.At, text.Size, text.Role) { Follows = text.Follows };
        }

        public override void Write(Utf8JsonWriter writer, PlanText value, JsonSerializerOptions options)
        {
            string role = value.Role is { } r ? $", \"role\": {Quoted(r, options)}" : "";
            string follows = value.Follows is { } f ? $", \"follows\": {Quoted(f, options)}" : "";
            writer.WriteRawValue(
                $"{LabelIndent}{{ \"id\": {Quoted(value.Id, options)}{role}, \"text\": {Quoted(value.Text, options)}, " +
                $"\"at\": {Text(value.At)}, \"size\": {Num(value.Size)}{follows} }}");
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

    private sealed class PointConverter : JsonConverter<PlanPoint>
    {
        public override PlanPoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            const string expected = "Expected an array of two numbers.";
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException(expected);
            Span<double> values = stackalloc double[2];
            for (int i = 0; i < 2; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException(expected);
                values[i] = reader.GetDouble();
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException(expected);
            return new PlanPoint(values[0], values[1]);
        }

        public override void Write(Utf8JsonWriter writer, PlanPoint value, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{Num(value.X)}, {Num(value.Y)}]");
    }

    private sealed record TurnJson
    {
        public required double T { get; init; }
        public required double Degrees { get; init; }
    }

    private sealed record ScreenItemJson
    {
        public required string Id { get; init; }
        public string? Role { get; init; }
        public required PlanScreenItemKind Kind { get; init; }
        public required PlanPoint At { get; init; }
        public string? Text { get; init; }
        public string? Image { get; init; }
        public double? Size { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }
        public PlanAlignX AlignX { get; init; } = PlanAlignX.Center;
        public PlanAlignY AlignY { get; init; } = PlanAlignY.Center;
        public ColorRgb? Color { get; init; }
        public double? Opacity { get; init; }
        public bool Backing { get; init; }
        public double? Wrap { get; init; }
        public double? Start { get; init; }
        public double? End { get; init; }
        public double? FadeIn { get; init; }
        public double? FadeOut { get; init; }
        public List<TurnJson>? Turn { get; init; }
        public string? Slot { get; init; }
        public CounterJson? Counter { get; init; }
    }

    private sealed record CounterJson
    {
        public PlanChangeTarget? Target { get; init; }
        public List<NumberKeyJson>? Keys { get; init; }
        public int Decimals { get; init; }
        public double? Min { get; init; }
        public double? Max { get; init; }
    }

    private sealed class ScreenItemConverter : JsonConverter<PlanScreenItem>
    {
        public override PlanScreenItem Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var item = JsonSerializer.Deserialize<ScreenItemJson>(ref reader, options)
                       ?? throw new JsonException("Expected a thing written over the picture.");
            return new PlanScreenItem
            {
                Id = item.Id, Role = item.Role, Kind = item.Kind, At = item.At, Text = item.Text, Image = item.Image, Size = item.Size,
                Width = item.Width, Height = item.Height, AlignX = item.AlignX, AlignY = item.AlignY, Color = item.Color,
                Opacity = item.Opacity, Backing = item.Backing, Wrap = item.Wrap, Start = item.Start, End = item.End,
                FadeIn = item.FadeIn, FadeOut = item.FadeOut, Slot = item.Slot,
                Turn = item.Turn?.Select(t => new PlanTurn(t.T, t.Degrees)).ToList(),
                Counter = item.Counter is { } counter
                    ? new PlanCounter
                    {
                        Target = counter.Target ?? new PlanChangeTarget(), Decimals = counter.Decimals, Min = counter.Min, Max = counter.Max,
                        Keys = counter.Keys?.Select(k => new PlanNumberKey(k.T, k.Value, k.Easing)).ToList(),
                    }
                    : null,
            };
        }

        public override void Write(Utf8JsonWriter writer, PlanScreenItem value, JsonSerializerOptions options)
        {
            var line = new StringBuilder(ScreenIndent);
            line.Append("{ \"id\": ").Append(Quoted(value.Id, options));
            if (value.Role is { } role) line.Append(", \"role\": ").Append(Quoted(role, options));
            line.Append(", \"kind\": ").Append(Quoted(Word(value.Kind), options));
            line.Append(", \"at\": [").Append(Num(value.At.X)).Append(", ").Append(Num(value.At.Y)).Append(']');
            if (value.Text is { } text) line.Append(", \"text\": ").Append(Quoted(text, options));
            if (value.Image is { } image) line.Append(", \"image\": ").Append(Quoted(image, options));
            Number("size", value.Size);
            Number("width", value.Width);
            Number("height", value.Height);
            if (value.AlignX != PlanAlignX.Center) line.Append(", \"alignX\": ").Append(Quoted(Word(value.AlignX), options));
            if (value.AlignY != PlanAlignY.Center) line.Append(", \"alignY\": ").Append(Quoted(Word(value.AlignY), options));
            if (value.Color is { } color) line.Append(", \"color\": ").Append(Text(color));
            Number("opacity", value.Opacity);
            if (value.Backing) line.Append(", \"backing\": true");
            Number("wrap", value.Wrap);
            Number("start", value.Start);
            Number("end", value.End);
            Number("fadeIn", value.FadeIn);
            Number("fadeOut", value.FadeOut);
            if (value.Slot is { } slot) line.Append(", \"slot\": ").Append(Quoted(slot, options));
            if (value.Turn is { } turns)
            {
                line.Append(", \"turn\": [");
                for (int i = 0; i < turns.Count; i++)
                    line.Append(i == 0 ? "" : ",").Append(TurnIndent)
                        .Append("{ \"t\": ").Append(Num(turns[i].T)).Append(", \"degrees\": ").Append(Num(turns[i].Degrees)).Append(" }");
                line.Append(turns.Count == 0 ? "]" : ScreenIndent + "]");
            }
            if (value.Counter is { } counter)
            {
                if (counter.Keys is { } keys)
                {
                    line.Append(", \"counter\": { \"keys\": [");
                    for (int i = 0; i < keys.Count; i++)
                        line.Append(i == 0 ? "" : ", ").Append("{ \"t\": ").Append(Num(keys[i].T)).Append(", \"value\": ")
                            .Append(Num(keys[i].Value)).Append(Easing(keys[i].Easing, options)).Append(" }");
                    line.Append(']');
                }
                else
                {
                    line.Append(", \"counter\": { \"target\": ").Append(Text(counter.Target, options));
                }
                if (counter.Decimals != 0) line.Append(", \"decimals\": ").Append(counter.Decimals.ToString(CultureInfo.InvariantCulture));
                Number("min", counter.Min);
                Number("max", counter.Max);
                line.Append(" }");
            }
            line.Append(" }");
            writer.WriteRawValue(line.ToString());

            void Number(string key, double? number)
            {
                if (number is { } n) line.Append(", \"").Append(key).Append("\": ").Append(Num(n));
            }
        }
    }

    // ---- changes ----

    /// <summary>An enum's word as the rest of the document writes it.</summary>
    private static string Word<T>(T value) where T : struct, Enum
        => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>The easing of a key, written only when it is not the even move a key has
    /// without one.</summary>
    private static string Easing(PlanEasing easing, JsonSerializerOptions options)
        => easing == PlanEasing.Linear ? "" : $", \"easing\": {Quoted(Word(easing), options)}";

    /// <summary>The way a colour is mixed on its way to a key, written only when it is not as
    /// light mixes.</summary>
    private static string Mix(PlanColorMix mix, JsonSerializerOptions options)
        => mix == PlanColorMix.Linear ? "" : $", \"mix\": {Quoted(Word(mix), options)}";

    private sealed record TargetJson
    {
        public string? Id { get; init; }
        public string? Role { get; init; }
    }

    private sealed class ChangeTargetConverter : JsonConverter<PlanChangeTarget>
    {
        public override PlanChangeTarget Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var target = JsonSerializer.Deserialize<TargetJson>(ref reader, options) ?? throw new JsonException("Expected the target of a change.");
            return new PlanChangeTarget { Id = target.Id, Role = target.Role };
        }

        public override void Write(Utf8JsonWriter writer, PlanChangeTarget value, JsonSerializerOptions options)
            => writer.WriteRawValue(Text(value, options));
    }

    /// <summary>A target on one line.</summary>
    private static string Text(PlanChangeTarget? target, JsonSerializerOptions options)
    {
        var parts = new List<string>(2);
        if (target?.Id is { } id) parts.Add($"\"id\": {Quoted(id, options)}");
        if (target?.Role is { } role) parts.Add($"\"role\": {Quoted(role, options)}");
        return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
    }

    private sealed record VisibleKeyJson
    {
        public required double T { get; init; }
        public required bool Value { get; init; }
        public double Fade { get; init; }
        public PlanEasing Easing { get; init; } = PlanEasing.Linear;
    }

    private sealed class VisibleKeyConverter : JsonConverter<PlanVisibleKey>
    {
        public override PlanVisibleKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<VisibleKeyJson>(ref reader, options) ?? throw new JsonException("Expected a key of a change.");
            return new PlanVisibleKey(key.T, key.Value, key.Fade, key.Easing);
        }

        public override void Write(Utf8JsonWriter writer, PlanVisibleKey value, JsonSerializerOptions options)
        {
            string fade = value.Fade != 0 ? $", \"fade\": {Num(value.Fade)}" : "";
            writer.WriteRawValue(
                $"{ChangeKeyIndent}{{ \"t\": {Num(value.T)}, \"value\": {(value.Value ? "true" : "false")}{fade}{Easing(value.Easing, options)} }}");
        }
    }

    private sealed record NumberKeyJson
    {
        public required double T { get; init; }
        public required double Value { get; init; }
        public PlanEasing Easing { get; init; } = PlanEasing.Linear;
    }

    private sealed class NumberKeyConverter : JsonConverter<PlanNumberKey>
    {
        public override PlanNumberKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<NumberKeyJson>(ref reader, options) ?? throw new JsonException("Expected a key of a change.");
            return new PlanNumberKey(key.T, key.Value, key.Easing);
        }

        public override void Write(Utf8JsonWriter writer, PlanNumberKey value, JsonSerializerOptions options)
            => writer.WriteRawValue(
                $"{ChangeKeyIndent}{{ \"t\": {Num(value.T)}, \"value\": {Num(value.Value)}{Easing(value.Easing, options)} }}");
    }

    private sealed record TintKeyJson
    {
        public required double T { get; init; }
        public required ColorRgb Color { get; init; }
        public required double Strength { get; init; }
        public PlanEasing Easing { get; init; } = PlanEasing.Linear;
        public PlanColorMix Mix { get; init; } = PlanColorMix.Linear;
    }

    private sealed class TintKeyConverter : JsonConverter<PlanTintKey>
    {
        public override PlanTintKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<TintKeyJson>(ref reader, options) ?? throw new JsonException("Expected a key of a change.");
            return new PlanTintKey(key.T, key.Color, key.Strength, key.Easing) { Mix = key.Mix };
        }

        public override void Write(Utf8JsonWriter writer, PlanTintKey value, JsonSerializerOptions options)
            => writer.WriteRawValue(
                $"{ChangeKeyIndent}{{ \"t\": {Num(value.T)}, \"color\": {Text(value.Color)}, \"strength\": {Num(value.Strength)}{Easing(value.Easing, options)}{Mix(value.Mix, options)} }}");
    }

    private sealed record VectorKeyJson
    {
        public required double T { get; init; }
        public required CaveVector3 Value { get; init; }
        public PlanEasing Easing { get; init; } = PlanEasing.Linear;
    }

    private sealed class VectorKeyConverter : JsonConverter<PlanVectorKey>
    {
        public override PlanVectorKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<VectorKeyJson>(ref reader, options) ?? throw new JsonException("Expected a key of a change.");
            return new PlanVectorKey(key.T, key.Value, key.Easing);
        }

        public override void Write(Utf8JsonWriter writer, PlanVectorKey value, JsonSerializerOptions options)
            => writer.WriteRawValue(
                $"{ChangeKeyIndent}{{ \"t\": {Num(value.T)}, \"value\": {Text(value.Value)}{Easing(value.Easing, options)} }}");
    }

    private sealed record ColorKeyJson
    {
        public required double T { get; init; }
        public required ColorRgb Value { get; init; }
        public PlanEasing Easing { get; init; } = PlanEasing.Linear;
        public PlanColorMix Mix { get; init; } = PlanColorMix.Linear;
    }

    private sealed class ColorKeyConverter : JsonConverter<PlanColorKey>
    {
        public override PlanColorKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var key = JsonSerializer.Deserialize<ColorKeyJson>(ref reader, options) ?? throw new JsonException("Expected a key of a change.");
            return new PlanColorKey(key.T, key.Value, key.Easing) { Mix = key.Mix };
        }

        public override void Write(Utf8JsonWriter writer, PlanColorKey value, JsonSerializerOptions options)
            => writer.WriteRawValue(
                $"{ChangeKeyIndent}{{ \"t\": {Num(value.T)}, \"value\": {Text(value.Value)}{Easing(value.Easing, options)}{Mix(value.Mix, options)} }}");
    }
}
