// Annotations: what a person adds to a scene to point things out. A label, a callout with a line
// to what it is about, a marker, or a photo, each at a station of the model or at a point, each
// there for the whole clip, for a time, or while the camera is near.
//
// This is the authored form, plain data on SceneSpec: it names a station and leaves sizes to be
// worked out from the cave. ScenePlanBuilder lowers it to the plan's annotations, where every
// place and size is a number and every time a key of a change (Plan/AnnotationPlanner). Only the
// player draws annotations; a generated script leaves them out, and says so.
//
// A list of annotations is also a file of its own (AnnotationsFile): a JSON array, with the
// photos named relative to the file, which the command line and the agent tools take.

using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.Cave3D;

namespace Therion.Blender;

/// <summary>What an annotation is.</summary>
public enum AnnotationKind
{
    /// <summary>A text on its anchor that turns to face the camera.</summary>
    Label,
    /// <summary>A text that stands away from its anchor, with a line from the anchor to it.</summary>
    Callout,
    /// <summary>A glowing ball on its anchor.</summary>
    Marker,
    /// <summary>A picture from an image file.</summary>
    Photo,
    /// <summary>A line of a thickness along <see cref="AnnotationSpec.Points"/>, lying on a surface.</summary>
    Outline,
}

/// <summary>Which way a photo faces.</summary>
public enum PhotoFacing
{
    /// <summary>It faces the camera: parallel to the picture and upright in it, wherever the
    /// camera is.</summary>
    Camera,
    /// <summary>It faces along <see cref="AnnotationSpec.Normal"/> and stays so.</summary>
    Fixed,
}

/// <summary>
/// One annotation. Its anchor is a station of the model (<see cref="Station"/>) or a point
/// (<see cref="At"/>): one of the two. Without <see cref="From"/>, <see cref="To"/> and
/// <see cref="Within"/> it is there for the whole clip.
/// </summary>
public sealed record AnnotationSpec
{
    /// <summary>The annotation's name in the scene; null is <c>annotation-&lt;n&gt;</c>, by its
    /// place in the list from 1.</summary>
    public string? Id { get; init; }

    public AnnotationKind Kind { get; init; } = AnnotationKind.Label;

    /// <summary>The name of the station the annotation is about, as the model has it.</summary>
    public string? Station { get; init; }

    /// <summary>The point the annotation is about, in the model's own coordinates: those its
    /// stations have in the model file.</summary>
    [JsonConverter(typeof(TripleConverter))]
    public CaveVector3? At { get; init; }

    /// <summary>What a label or a callout says.</summary>
    public string? Text { get; init; }

    /// <summary>How large it is, in metres: the height of a text's letters, the radius of a
    /// marker, the width of a photo. Null is a size that suits the cave: a text as large as a
    /// station label, a marker of one hundredth of the cave's diagonal, a photo of a tenth.</summary>
    public double? Size { get; init; }

    /// <summary>Where a callout's text, or the middle of a photo, stands from the anchor, in
    /// metres east, north and up. Null puts a callout's text four letter heights above its
    /// anchor, and a photo on its anchor. A photo that stands away from its anchor has a line
    /// to it.</summary>
    [JsonConverter(typeof(TripleConverter))]
    public CaveVector3? Offset { get; init; }

    /// <summary>The colour a text or a marker glows in; null is the plan's own (cream for a
    /// text, orange-red for a marker).</summary>
    [JsonConverter(typeof(ColorConverter))]
    public ColorRgb? Color { get; init; }

    /// <summary>The image file of a photo: a PNG or a JPEG. In an annotations file, relative to
    /// that file.</summary>
    public string? Image { get; init; }

    public PhotoFacing Facing { get; init; } = PhotoFacing.Camera;

    /// <summary>The way a photo with <see cref="PhotoFacing.Fixed"/> faces: east, north, up.</summary>
    [JsonConverter(typeof(TripleConverter))]
    public CaveVector3? Normal { get; init; }

    /// <summary>A border about a photo.</summary>
    public bool Framed { get; init; }

    /// <summary>What a marker looks like: a ball (the default), a pin, a cone, a cube, a ring, or a
    /// small picture (<see cref="Image"/>, <c>2 × Size</c> wide).</summary>
    public Plan.PlanMarkerSymbol? Symbol { get; init; }

    /// <summary>What sets how large a marker is seen to be (<see cref="Plan.PlanMarkerSizing"/>);
    /// null is its size as it is. Needs <see cref="SizeFactor"/>.</summary>
    public Plan.PlanMarkerSizing? SizeBy { get; init; }

    /// <summary>With <see cref="SizeBy"/>: metres of radius per metre of distance, or the share of
    /// the picture's width the marker is seen across.</summary>
    public double? SizeFactor { get; init; }

    /// <summary>With <see cref="SizeBy"/>: the smallest radius, in metres; null is a quarter of the
    /// size.</summary>
    public double? MinSize { get; init; }

    /// <summary>With <see cref="SizeBy"/>: the largest radius, in metres; null is four times the
    /// size.</summary>
    public double? MaxSize { get; init; }

    /// <summary>The points of an outline, in the model's coordinates like <see cref="At"/>; its
    /// thickness is <see cref="Size"/>. Without <see cref="At"/> and <see cref="Station"/> the
    /// outline is at its first point.</summary>
    [JsonConverter(typeof(TriplesConverter))]
    public IReadOnlyList<CaveVector3>? Points { get; init; }

    /// <summary>What the annotation is, for a change of the plan that names a group: lower-case
    /// words joined by '-'. Null is its kind.</summary>
    public string? Role { get; init; }

    /// <summary>When it appears, in seconds from the start of the clip; null is from the
    /// start.</summary>
    public double? From { get; init; }

    /// <summary>When it is gone; null is never.</summary>
    public double? To { get; init; }

    /// <summary>It is there only while the camera is within this many metres of its anchor.</summary>
    public double? Within { get; init; }

    /// <summary>How long it takes to appear and to go, in seconds. What is there at the start
    /// of the clip does not fade in, and what is there at its end does not fade out.</summary>
    public double Fade { get; init; } = 0.5;

    /// <summary>The id an annotation has at <paramref name="index"/> of its list: its own, or
    /// <c>annotation-&lt;n&gt;</c>.</summary>
    public string IdAt(int index)
        => string.IsNullOrWhiteSpace(Id) ? "annotation-" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : Id;

    /// <summary>Three numbers, written as <c>[x, y, z]</c>; read from that or from
    /// <c>{ "x": …, "y": …, "z": … }</c>, the way the rest of a spec writes a point.</summary>
    private sealed class TripleConverter : JsonConverter<CaveVector3>
    {
        public override CaveVector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var (x, y, z) = ReadTriple(ref reader, "x", "y", "z");
            return new CaveVector3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, CaveVector3 value, JsonSerializerOptions options)
            => WriteTriple(writer, value.X, value.Y, value.Z);
    }

    /// <summary>A colour, written as <c>[r, g, b]</c>; read from that or from
    /// <c>{ "r": …, "g": …, "b": … }</c>.</summary>
    private sealed class TriplesConverter : JsonConverter<IReadOnlyList<CaveVector3>>
    {
        public override IReadOnlyList<CaveVector3> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a list of points: [[x, y, z], ...].");
            var points = new List<CaveVector3>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var (x, y, z) = ReadTriple(ref reader, "x", "y", "z");
                points.Add(new CaveVector3(x, y, z));
            }
            return points;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<CaveVector3> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var p in value) WriteTriple(writer, p.X, p.Y, p.Z);
            writer.WriteEndArray();
        }
    }

    private sealed class ColorConverter : JsonConverter<ColorRgb>
    {
        public override ColorRgb Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var (r, g, b) = ReadTriple(ref reader, "r", "g", "b");
            return new ColorRgb(r, g, b);
        }

        public override void Write(Utf8JsonWriter writer, ColorRgb value, JsonSerializerOptions options)
            => WriteTriple(writer, value.R, value.G, value.B);
    }

    private static void WriteTriple(Utf8JsonWriter writer, double a, double b, double c)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(a);
        writer.WriteNumberValue(b);
        writer.WriteNumberValue(c);
        writer.WriteEndArray();
    }

    private static (double, double, double) ReadTriple(ref Utf8JsonReader reader, string first, string second, string third)
    {
        string expected = $"Expected three numbers: [{first}, {second}, {third}].";
        var values = new double[3];
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException(expected);
                values[i] = reader.GetDouble();
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException(expected);
            return (values[0], values[1], values[2]);
        }
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException(expected);
        int found = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString() ?? "";
            int at = name.Equals(first, StringComparison.OrdinalIgnoreCase) ? 0
                : name.Equals(second, StringComparison.OrdinalIgnoreCase) ? 1
                : name.Equals(third, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
            if (at < 0 || !reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException(expected);
            values[at] = reader.GetDouble();
            found |= 1 << at;
        }
        if (found != 7) throw new JsonException(expected);
        return (values[0], values[1], values[2]);
    }
}

/// <summary>
/// A list of annotations as a file of its own: a JSON array of annotations, or an object with
/// the array under <c>annotations</c>. Keys are camelCase, kinds and facings are words, points
/// and colours are three numbers. A key that is not known is refused: it may be a misspelt one.
/// </summary>
public static class AnnotationsFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Reads annotations from JSON text.</summary>
    /// <param name="baseDirectory">The folder the photos' paths are relative to; with it every
    /// image path is made a full one. Null leaves the paths as they are.</param>
    /// <exception cref="SceneSpecFormatException">The text is not a list of annotations.</exception>
    public static IReadOnlyList<AnnotationSpec> Read(string json, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        List<AnnotationSpec?>? read;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            // Where, and not the parser's own words: it quotes what it could not read, however
            // long that is and whatever file it was that somebody took for a list of annotations.
            throw new SceneSpecFormatException($"Malformed annotations JSON: {JsonFault.NotJson(ex)}");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("annotations", out var inner) || root.EnumerateObject().Count() != 1)
                    throw new SceneSpecFormatException("An annotations file is a list of annotations, or an object with the list under \"annotations\" and nothing else.");
                root = inner;
            }
            if (root.ValueKind != JsonValueKind.Array)
                throw new SceneSpecFormatException("An annotations file is a list of annotations.");
            try
            {
                read = root.Deserialize<List<AnnotationSpec?>>(Options);
            }
            catch (JsonException ex)
            {
                // What is wrong with the shape names a key or a kind of value, in a line.
                throw new SceneSpecFormatException($"Malformed annotations JSON: {JsonFault.OfShape(ex)}");
            }
        }
        var annotations = new List<AnnotationSpec>(read?.Count ?? 0);
        foreach (var annotation in read ?? [])
        {
            if (annotation is null) throw new SceneSpecFormatException("An annotation is missing from the list.");
            annotations.Add(baseDirectory is not null && !string.IsNullOrWhiteSpace(annotation.Image)
                ? annotation with { Image = Path.GetFullPath(annotation.Image, Path.GetFullPath(baseDirectory)) }
                : annotation);
        }
        return annotations;
    }

    /// <summary>Reads an annotations file; its photos are named relative to it.</summary>
    /// <exception cref="SceneSpecFormatException">The file is not a list of annotations.</exception>
    public static IReadOnlyList<AnnotationSpec> ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllText(path), Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    /// <summary>Writes annotations as the JSON array <see cref="Read"/> reads.</summary>
    public static string Write(IReadOnlyList<AnnotationSpec> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        var options = new JsonSerializerOptions(Options)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        return JsonSerializer.Serialize(annotations, options).ReplaceLineEndings("\n") + "\n";
    }
}
