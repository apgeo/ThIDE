// The meshes of a bundle that came from elsewhere, read as Blender will read them before Blender
// does: how many triangles they make (the limit of a node holds on what is imported, not on what a
// header claims), and, for a GLB, that it holds everything it shows inside itself.
//
// PLY: the header is read to its end_header whatever it holds (comments, any number of lines, up to
// a cap), and the faces are counted from the data, each face of n corners as n - 2 triangles,
// because one line of a header could say a face count that the data does not keep to.
//
// GLB: a glTF file may name other files by URI (images and buffers), and Blender's importer opens
// them wherever they are: outside the bundle, the frames that go back to whoever sent it would show
// them. A GLB of a bundle names none: every buffer is its own binary chunk, every image is a PNG or a
// JPEG inside it, and it uses no extension but those that only describe materials (none that
// decompresses meshes or decodes other image formats). Its triangles are counted from its
// accessors, for every node that shows a mesh, and every accessor must fit the data it says it reads.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Therion.Blender.Plan;

namespace Therion.Blender.Player;

/// <summary>A mesh file that cannot be read as the reader of bundles must read it.</summary>
internal sealed class MeshFileException(string message) : Exception(message);

internal static class BundleMeshes
{
    /// <summary>The longest PLY header read: comments and all.</summary>
    internal const int MaxPlyHeaderBytes = 1024 * 1024;

    /// <summary>The longest JSON chunk of a GLB.</summary>
    internal const int MaxGlbJsonBytes = 64 * 1024 * 1024;

    /// <summary>The triangles the mesh file at <paramref name="path"/> makes when Blender imports it.</summary>
    /// <exception cref="MeshFileException">They cannot be counted, or the file is not one a bundle may hold.</exception>
    public static long Triangles(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".ply" => PlyTriangles(path),
                ".glb" => GlbTriangles(path),
                _ => throw new MeshFileException("it is neither a PLY nor a GLB file"),
            };
        }
        catch (EndOfStreamException) { throw new MeshFileException("the file ends before its data does"); }
        catch (OverflowException) { throw new MeshFileException("a count of the file is larger than any mesh"); }
    }

    // ---- PLY ----

    private enum PlyFormat { Ascii, LittleEndian, BigEndian }

    private sealed record PlyProperty(string Name, int Size, int CountSize, bool IsList);

    private sealed record PlyElement(string Name, long Count, List<PlyProperty> Properties);

    private sealed record PlyHeader(PlyFormat Format, List<PlyElement> Elements, long Length);

    private static readonly Dictionary<string, int> PlyTypes = new(StringComparer.Ordinal)
    {
        ["char"] = 1, ["int8"] = 1, ["uchar"] = 1, ["uint8"] = 1,
        ["short"] = 2, ["int16"] = 2, ["ushort"] = 2, ["uint16"] = 2,
        ["int"] = 4, ["int32"] = 4, ["uint"] = 4, ["uint32"] = 4,
        ["float"] = 4, ["float32"] = 4, ["double"] = 8, ["float64"] = 8,
    };

    /// <summary>The faces a PLY file's header declares; null when the header cannot be read.
    /// Reads the header alone.</summary>
    public static long? PlyDeclaredFaces(Stream stream)
    {
        try { return ReadPlyHeader(stream).Elements.FirstOrDefault(e => e.Name == "face")?.Count ?? 0; }
        catch (MeshFileException) { return null; }
    }

    private static PlyHeader ReadPlyHeader(Stream stream)
    {
        var line = new StringBuilder();
        var lines = new List<string>();
        long read = 0;
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0) throw new MeshFileException("the PLY header has no end_header");
            if (++read > MaxPlyHeaderBytes) throw new MeshFileException($"the PLY header is longer than {MaxPlyHeaderBytes} bytes");
            if (b != '\n')
            {
                line.Append((char)b);
                continue;
            }
            string text = line.ToString().TrimEnd('\r');
            line.Clear();
            if (lines.Count == 0 && text != "ply") throw new MeshFileException("the file is not a PLY file");
            lines.Add(text);
            if (text.Trim() == "end_header") break;
        }

        PlyFormat? format = null;
        var elements = new List<PlyElement>();
        foreach (string text in lines.Skip(1))
        {
            var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "comment" or "obj_info" or "end_header":
                    continue;
                case "format":
                    if (format is not null || parts.Length != 3 || parts[2] != "1.0") throw new MeshFileException("the PLY header's format line is not one of PLY 1.0");
                    format = parts[1] switch
                    {
                        "ascii" => PlyFormat.Ascii,
                        "binary_little_endian" => PlyFormat.LittleEndian,
                        "binary_big_endian" => PlyFormat.BigEndian,
                        _ => throw new MeshFileException($"the PLY format '{PortableNames.Printable(parts[1], 40)}' is not known"),
                    };
                    break;
                case "element":
                    if (parts.Length != 3 || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long count))
                        throw new MeshFileException("an element of the PLY header has no count");
                    elements.Add(new PlyElement(parts[1], count, []));
                    break;
                case "property":
                    if (elements.Count == 0) throw new MeshFileException("the PLY header has a property before any element");
                    elements[^1].Properties.Add(parts switch
                    {
                        [_, "list", var countType, var itemType, var name] when PlyTypes.TryGetValue(countType, out int c) && PlyTypes.TryGetValue(itemType, out int s)
                            && countType is not ("float" or "float32" or "double" or "float64")
                            => new PlyProperty(name, s, c, IsList: true),
                        [_, var type, var name] when PlyTypes.TryGetValue(type, out int s) => new PlyProperty(name, s, 0, IsList: false),
                        _ => throw new MeshFileException("a property of the PLY header is not one PLY knows"),
                    });
                    break;
                default:
                    throw new MeshFileException($"the PLY header has a line PLY does not know ('{PortableNames.Printable(parts[0], 40)}')");
            }
        }
        if (format is null) throw new MeshFileException("the PLY header has no format line");
        return new PlyHeader(format.Value, elements, read);
    }

    private static long PlyTriangles(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var header = ReadPlyHeader(stream);
        int face = header.Elements.FindIndex(e => e.Name == "face");
        if (face < 0) return 0;  // points alone: no triangle
        var corners = header.Elements[face].Properties.FirstOrDefault(p => p.IsList)
                      ?? throw new MeshFileException("the faces of the PLY file have no list of corners");
        var reader = new PlyReader(stream, header.Format);
        long triangles = 0;
        for (int e = 0; e <= face; e++)
        {
            var element = header.Elements[e];
            if (e < face && header.Format != PlyFormat.Ascii && element.Properties.All(p => !p.IsList))
            {
                reader.Skip(checked(element.Count * element.Properties.Sum(p => (long)p.Size)));
                continue;
            }
            for (long i = 0; i < element.Count; i++)
                foreach (var property in element.Properties)
                {
                    if (!property.IsList)
                    {
                        reader.Scalar(property.Size);
                        continue;
                    }
                    long n = reader.Count(property.CountSize);
                    if (n < 0) throw new MeshFileException("a list of the PLY file has a negative length");
                    reader.Items(n, property.Size);
                    if (e == face && ReferenceEquals(property, corners) && n > 2) triangles = checked(triangles + n - 2);
                }
        }
        return triangles;
    }

    /// <summary>Reads the data of a PLY file as far as counting needs: binary values by their
    /// sizes, ASCII values as words.</summary>
    private sealed class PlyReader(Stream stream, PlyFormat format)
    {
        private readonly byte[] _buffer = new byte[1 << 16];
        private int _at, _end;

        private int Next()
        {
            if (_at == _end)
            {
                _end = stream.Read(_buffer, 0, _buffer.Length);
                _at = 0;
                if (_end <= 0) throw new EndOfStreamException();
            }
            return _buffer[_at++];
        }

        public void Skip(long bytes)
        {
            long buffered = Math.Min(bytes, _end - _at);
            _at += (int)buffered;
            bytes -= buffered;
            if (bytes == 0) return;
            if (stream.Position + bytes > stream.Length) throw new EndOfStreamException();
            stream.Seek(bytes, SeekOrigin.Current);
        }

        private string Word()
        {
            var word = new StringBuilder();
            int c;
            do c = Next(); while (c is ' ' or '\t' or '\r' or '\n');
            while (c is not (' ' or '\t' or '\r' or '\n'))
            {
                if (word.Length > 64) throw new MeshFileException("a value of the PLY file is not a number");
                word.Append((char)c);
                try { c = Next(); }
                catch (EndOfStreamException) { break; }
            }
            return word.ToString();
        }

        public void Scalar(int size)
        {
            if (format == PlyFormat.Ascii) Word();
            else Skip(size);
        }

        public void Items(long count, int size)
        {
            if (format != PlyFormat.Ascii)
            {
                Skip(checked(count * size));
                return;
            }
            for (long i = 0; i < count; i++) Word();
        }

        public long Count(int size)
        {
            if (format == PlyFormat.Ascii)
            {
                string word = Word();
                return long.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)
                    ? n : throw new MeshFileException("a list of the PLY file has no length");
            }
            Span<byte> bytes = stackalloc byte[size];
            for (int i = 0; i < size; i++) bytes[i] = (byte)Next();
            bool little = format == PlyFormat.LittleEndian;
            return size switch
            {
                1 => bytes[0],
                2 => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
                _ => little ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes),
            };
        }
    }

    // ---- GLB ----

    private const uint GlbMagic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

    /// <summary>The extensions a GLB of a bundle may use: those that only describe materials,
    /// lights and texture placement. Any other (meshes compressed, images in other formats,
    /// instancing) is refused.</summary>
    private static bool IsAllowedExtension(string name)
        => name.StartsWith("KHR_materials_", StringComparison.Ordinal)
           || name is "KHR_texture_transform" or "KHR_lights_punctual" or "KHR_mesh_quantization";

    private static long GlbTriangles(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        Span<byte> head = stackalloc byte[20];
        if (stream.ReadAtLeast(head, 20, throwOnEndOfStream: false) < 20) throw new MeshFileException("the file is not a GLB file");
        if (BinaryPrimitives.ReadUInt32LittleEndian(head) != GlbMagic || BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) != 2)
            throw new MeshFileException("the file is not a GLB file of glTF 2.0");
        uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(head[12..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(head[16..]) != JsonChunk || jsonLength > MaxGlbJsonBytes || 20 + (long)jsonLength > length)
            throw new MeshFileException("the GLB file's JSON chunk is not there or is too large");
        var json = new byte[jsonLength];
        stream.ReadExactly(json);

        // The binary chunk, when there is one, follows the JSON padded to four bytes.
        long binAt = 20 + (((long)jsonLength + 3) & ~3L), binLength = 0;
        if (binAt + 8 <= length)
        {
            Span<byte> chunk = stackalloc byte[8];
            stream.Seek(binAt, SeekOrigin.Begin);
            stream.ReadExactly(chunk);
            if (BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]) == BinChunk)
            {
                binLength = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
                binAt += 8;
                if (binAt + binLength > length) throw new MeshFileException("the GLB file's binary chunk is cut short");
            }
        }

        JsonDocument document;
        try { document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { throw new MeshFileException("the GLB file's JSON cannot be read"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new MeshFileException("the GLB file's JSON is not an object");
            CheckNoUriAndKnownExtensions(root);
            foreach (string key in new[] { "extensionsUsed", "extensionsRequired" })
                foreach (var name in Items(root, key))
                    if (name.ValueKind != JsonValueKind.String || !IsAllowedExtension(name.GetString()!))
                        throw new MeshFileException($"the GLB file uses the extension '{PortableNames.Printable(name.ToString(), 80)}', which a bundle may not");

            var buffers = Items(root, "buffers").ToList();
            if (buffers.Count > 1) throw new MeshFileException("the GLB file has more than one buffer; a bundle's GLB holds its data in its own binary chunk");
            long bufferLength = buffers.Count == 1 ? Number(buffers[0], "byteLength") : 0;
            if (bufferLength > binLength) throw new MeshFileException("the GLB file's buffer is larger than its binary chunk");

            var views = Items(root, "bufferViews").Select(v =>
            {
                if (Integer(v, "buffer") != 0) throw new MeshFileException("a buffer view of the GLB file names no buffer it has");
                long offset = Integer(v, "byteOffset", 0), size = Number(v, "byteLength");
                if (offset < 0 || size < 0 || offset + size > bufferLength) throw new MeshFileException("a buffer view of the GLB file lies outside its buffer");
                return (Offset: offset, Length: size, Stride: Integer(v, "byteStride", 0));
            }).ToList();

            foreach (var image in Items(root, "images"))
            {
                string? type = image.TryGetProperty("mimeType", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                if (type is not ("image/png" or "image/jpeg") || !image.TryGetProperty("bufferView", out _))
                    throw new MeshFileException("an image of the GLB file is not a PNG or a JPEG inside it");
                var view = At(views, Integer(image, "bufferView"), "an image");
                stream.Seek(binAt + view.Offset, SeekOrigin.Begin);
                try
                {
                    var (format, _, _) = PlanImage.Identify(new BoundedStream(stream, view.Length));
                    if ((format == PlanImageFormat.Png) != (type == "image/png")) throw new PlanImageException("it is not what its type says");
                }
                catch (PlanImageException ex) { throw new MeshFileException($"an image of the GLB file cannot be shown: {ex.Message}"); }
            }

            var accessors = Items(root, "accessors").Select(a =>
            {
                if (a.TryGetProperty("sparse", out _) || !a.TryGetProperty("bufferView", out _))
                    return (Count: -1L, Fits: false);
                long count = Number(a, "count");
                int component = Integer(a, "componentType") switch
                {
                    5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
                    _ => throw new MeshFileException("an accessor of the GLB file has a type glTF does not know"),
                };
                int items = (a.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null) switch
                {
                    "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16,
                    _ => throw new MeshFileException("an accessor of the GLB file has a type glTF does not know"),
                };
                var view = At(views, Integer(a, "bufferView"), "an accessor");
                long element = (long)component * items, offset = Integer(a, "byteOffset", 0);
                long stride = view.Stride > 0 ? view.Stride : element;
                bool fits = count >= 0 && offset >= 0 && (count == 0 || offset + (count - 1) * stride + element <= view.Length);
                return (Count: count, Fits: fits);
            }).ToList();

            var meshes = Items(root, "meshes").Select(mesh =>
            {
                long triangles = 0;
                foreach (var primitive in Items(mesh, "primitives"))
                {
                    if (!primitive.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
                        throw new MeshFileException("a primitive of the GLB file has no attributes");
                    foreach (var attribute in attributes.EnumerateObject())
                        Read(accessors, attribute.Value, "an attribute");
                    long corners = primitive.TryGetProperty("indices", out var indices)
                        ? Read(accessors, indices, "the indices")
                        : attributes.TryGetProperty("POSITION", out var position) ? Read(accessors, position, "the positions") : 0;
                    triangles = checked(triangles + Integer(primitive, "mode", 4) switch
                    {
                        4 => corners / 3,
                        5 or 6 => Math.Max(0, corners - 2),
                        >= 0 and <= 3 => 0,
                        _ => throw new MeshFileException("a primitive of the GLB file has a mode glTF does not know"),
                    });
                }
                return triangles;
            }).ToList();

            // Every node that shows a mesh adds its triangles: the player joins them into one mesh.
            var nodes = Items(root, "nodes").ToList();
            long total = 0;
            foreach (var node in nodes)
                if (node.TryGetProperty("mesh", out _))
                    total = checked(total + At(meshes, Integer(node, "mesh"), "a node"));
            // A node that is the child of two (or of itself) would be imported again and again.
            var parents = new HashSet<int>();
            foreach (var node in nodes)
                foreach (var child in Items(node, "children"))
                {
                    int index = child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out int i) ? i : -1;
                    if (index < 0 || index >= nodes.Count || !parents.Add(index))
                        throw new MeshFileException("the nodes of the GLB file are not a tree");
                }
            return total;
        }
    }

    /// <summary>No <c>uri</c> anywhere (images and buffers, or one an extension names), and an
    /// <c>extensions</c> object anywhere names only extensions a bundle's GLB may use.</summary>
    private static void CheckNoUriAndKnownExtensions(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "uri")
                        throw new MeshFileException("the GLB file names a file by its URI; a bundle's GLB holds everything it shows inside itself");
                    if (property.Name == "extensions" && property.Value.ValueKind == JsonValueKind.Object)
                        foreach (var extension in property.Value.EnumerateObject())
                            if (!IsAllowedExtension(extension.Name))
                                throw new MeshFileException($"the GLB file uses the extension '{PortableNames.Printable(extension.Name, 80)}', which a bundle may not");
                    CheckNoUriAndKnownExtensions(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CheckNoUriAndKnownExtensions(item);
                break;
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement owner, string name)
        => owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : throw new MeshFileException($"the GLB file's '{name}' is not a list")
            : [];

    private static long Number(JsonElement owner, string name)
        => owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out long n) && n >= 0
            ? n : throw new MeshFileException($"a '{name}' of the GLB file is not a count");

    private static int Integer(JsonElement owner, string name, int? absent = null)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var value))
            return absent ?? throw new MeshFileException($"a '{name}' of the GLB file is missing");
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) && n >= 0
            ? n : throw new MeshFileException($"a '{name}' of the GLB file is not a count");
    }

    private static T At<T>(List<T> list, int index, string what)
        => index >= 0 && index < list.Count ? list[index] : throw new MeshFileException($"{what} of the GLB file names something it does not have");

    /// <summary>The element count of the accessor <paramref name="reference"/> names, which must fit its data.</summary>
    private static long Read(List<(long Count, bool Fits)> accessors, JsonElement reference, string what)
    {
        int index = reference.ValueKind == JsonValueKind.Number && reference.TryGetInt32(out int i) ? i : -1;
        var accessor = At(accessors, index, what);
        if (!accessor.Fits) throw new MeshFileException($"{what} of the GLB file read more than the data it names, or no data");
        return accessor.Count;
    }

    /// <summary>A stream that reads no more than a given number of bytes of another.</summary>
    private sealed class BoundedStream(Stream inner, long length) : Stream
    {
        private long _left = length;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0) return 0;
            int read = inner.Read(buffer, offset, (int)Math.Min(count, _left));
            _left -= read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
