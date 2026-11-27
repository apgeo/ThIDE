// The parts of invented bundles that break the rules a bundle from elsewhere is held to: GLB files
// that name other files or count their triangles otherwise than they hold them, PLY files whose
// header hides their faces, zips whose entries lie about their sizes. Every file here is made up
// by the test that asks for it; nothing comes from a real cave.

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Therion.Blender.Tests.Player;

internal static class HostileBundles
{
    /// <summary>A GLB of <paramref name="json"/> and the binary chunk <paramref name="bin"/>.</summary>
    public static byte[] Glb(string json, byte[] bin)
    {
        byte[] j = Encoding.UTF8.GetBytes(json);
        int jp = (4 - j.Length % 4) % 4, bp = (4 - bin.Length % 4) % 4;
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        int total = 12 + 8 + j.Length + jp + (bin.Length > 0 ? 8 + bin.Length + bp : 0);
        w.Write(0x46546C67); w.Write(2); w.Write(total);
        w.Write(j.Length + jp); w.Write(0x4E4F534A); w.Write(j); for (int i = 0; i < jp; i++) w.Write((byte)' ');
        if (bin.Length > 0)
        {
            w.Write(bin.Length + bp); w.Write(0x004E4942); w.Write(bin); for (int i = 0; i < bp; i++) w.Write((byte)0);
        }
        return s.ToArray();
    }

    /// <summary>
    /// A GLB of a big emissive quad at Blender x = -100 (behind the invented cave, as the plan's
    /// camera sees it), textured with an image: inside the GLB when <paramref name="imageUri"/>
    /// is null (the PNG <paramref name="embedded"/>), else the file that URI names. With
    /// <paramref name="bufferUri"/>, a second mesh whose three vertices are read from that file.
    /// </summary>
    public static byte[] Backdrop(string? imageUri, string? bufferUri, byte[]? embedded = null, string mimeType = "image/png")
    {
        var bin = new MemoryStream();
        var w = new BinaryWriter(bin);
        // glTF is Y-up: Blender (x, y, z) = glTF (x, z, -y). The plane x = -100, ±300 on the others.
        float[][] p = [[-100, -300, 300], [-100, -300, -300], [-100, 300, -300], [-100, 300, 300]];
        foreach (var v in p) foreach (var c in v) w.Write(c);                       // 0..48 positions
        foreach (var uv in new float[][] { [0, 0], [1, 0], [1, 1], [0, 1] }) foreach (var c in uv) w.Write(c); // 48..80
        foreach (ushort i in new ushort[] { 0, 1, 2, 0, 2, 3 }) w.Write(i);         // 80..92
        string image;
        string extraView = "";
        if (imageUri is null)
        {
            embedded ??= MagentaPng(16);
            w.Write(embedded);                                                         // 92..
            extraView = $",{{\"buffer\":0,\"byteOffset\":92,\"byteLength\":{embedded.Length}}}";
            image = $"{{\"bufferView\":3,\"mimeType\":{JsonValue.Create(mimeType)!.ToJsonString()}}}";
        }
        else image = $"{{\"uri\":{JsonValue.Create(imageUri)!.ToJsonString()}}}";
        w.Flush();
        string buffers = $"{{\"byteLength\":{bin.Length}}}";
        string extraMesh = "", extraNode = "", extraViews = extraView, extraAccessors = "";
        if (bufferUri is not null)
        {
            int view = imageUri is null ? 4 : 3;
            buffers += $",{{\"byteLength\":36,\"uri\":{JsonValue.Create(bufferUri)!.ToJsonString()}}}";
            extraViews += ",{\"buffer\":1,\"byteOffset\":0,\"byteLength\":36}";
            extraAccessors = $",{{\"bufferView\":{view},\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[-1,-1,-1],\"max\":[1,1,1]}}";
            extraMesh = ",{\"primitives\":[{\"attributes\":{\"POSITION\":3}}]}";
            extraNode = ",{\"mesh\":1}";
        }
        string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0" + (bufferUri is null ? "" : ",1") + "]}],"
            + "\"nodes\":[{\"mesh\":0}" + extraNode + "],"
            + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1},\"indices\":2,\"material\":0}]}" + extraMesh + "],"
            + "\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0},\"metallicFactor\":0,\"roughnessFactor\":1},"
            + "\"emissiveTexture\":{\"index\":0},\"emissiveFactor\":[1,1,1]}],"
            + "\"textures\":[{\"source\":0}],"
            + "\"images\":[" + image + "],"
            + "\"buffers\":[" + buffers + "],"
            + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":48,\"byteLength\":32},"
            + "{\"buffer\":0,\"byteOffset\":80,\"byteLength\":12}" + extraViews + "],"
            + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\",\"min\":[-100,-300,-300],\"max\":[-100,300,300]},"
            + "{\"bufferView\":1,\"componentType\":5126,\"count\":4,\"type\":\"VEC2\"},"
            + "{\"bufferView\":2,\"componentType\":5123,\"count\":6,\"type\":\"SCALAR\"}" + extraAccessors + "]}";
        return Glb(json, bin.ToArray());
    }

    /// <summary>A GLB grid of <paramref name="n"/> × <paramref name="n"/> vertices, 2 (n - 1)²
    /// triangles, shown by <paramref name="nodes"/> nodes; <paramref name="edit"/> changes its JSON.</summary>
    public static byte[] Grid(int n, int nodes = 1, Action<JsonObject>? edit = null)
    {
        var bin = new MemoryStream();
        var w = new BinaryWriter(bin);
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { w.Write((float)x); w.Write(0f); w.Write((float)y); }
        int positions = (int)bin.Length;
        int triangles = 0;
        for (int y = 0; y < n - 1; y++)
            for (int x = 0; x < n - 1; x++)
            {
                uint a = (uint)(y * n + x), b = a + 1, c = a + (uint)n, d = c + 1;
                foreach (uint i in new[] { a, c, b, b, c, d }) w.Write(i);
                triangles += 2;
            }
        w.Flush();
        string nodeList = string.Join(",", Enumerable.Repeat("{\"mesh\":0}", nodes));
        string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[" + string.Join(",", Enumerable.Range(0, nodes)) + "]}],\"nodes\":[" + nodeList + "],"
            + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],"
            + $"\"buffers\":[{{\"byteLength\":{bin.Length}}}],"
            + $"\"bufferViews\":[{{\"buffer\":0,\"byteOffset\":0,\"byteLength\":{positions}}},{{\"buffer\":0,\"byteOffset\":{positions},\"byteLength\":{bin.Length - positions}}}],"
            + $"\"accessors\":[{{\"bufferView\":0,\"componentType\":5126,\"count\":{n * n},\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[{n - 1},0,{n - 1}]}},"
            + $"{{\"bufferView\":1,\"componentType\":5125,\"count\":{triangles * 3},\"type\":\"SCALAR\"}}]}}";
        if (edit is not null)
        {
            var node = JsonNode.Parse(json)!.AsObject();
            edit(node);
            json = node.ToJsonString();
        }
        return Glb(json, bin.ToArray());
    }

    /// <summary>A PLY file with <paramref name="padding"/> comment lines put after its first line.</summary>
    public static byte[] Padded(byte[] ply, int padding = 200)
    {
        int eol = Array.IndexOf(ply, (byte)'\n');
        var comments = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("comment invented padding\n", padding)));
        return [.. ply[..(eol + 1)], .. comments, .. ply[(eol + 1)..]];
    }

    /// <summary>A binary PLY of <paramref name="faces"/> faces of <paramref name="corners"/>
    /// corners each, over as many vertices as they need.</summary>
    public static byte[] Polygons(int faces, int corners, bool ascii = false)
    {
        int vertices = corners + faces;
        var header = new StringBuilder("ply\n")
            .Append(ascii ? "format ascii 1.0\n" : "format binary_little_endian 1.0\n")
            .Append("comment invented polygons\n")
            .Append($"element vertex {vertices}\nproperty float x\nproperty float y\nproperty float z\n")
            .Append($"element face {faces}\nproperty list uchar int vertex_indices\nend_header\n");
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(header.ToString()));
        if (ascii)
        {
            var text = new StringBuilder();
            for (int v = 0; v < vertices; v++) text.Append($"{v} {v % 3} {v % 5}\n");
            for (int f = 0; f < faces; f++) text.Append(corners).Append(' ').Append(string.Join(' ', Enumerable.Range(f, corners))).Append('\n');
            s.Write(Encoding.ASCII.GetBytes(text.ToString()));
            return s.ToArray();
        }
        var w = new BinaryWriter(s);
        for (int v = 0; v < vertices; v++) { w.Write((float)v); w.Write((float)(v % 3)); w.Write((float)(v % 5)); }
        for (int f = 0; f < faces; f++)
        {
            w.Write((byte)corners);
            for (int c = 0; c < corners; c++) w.Write(f + c);
        }
        w.Flush();
        return s.ToArray();
    }

    /// <summary>A PNG of one colour, magenta, <paramref name="side"/> pixels square.</summary>
    public static byte[] MagentaPng(int side)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < side; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < side; x++) { raw.WriteByte(255); raw.WriteByte(0); raw.WriteByte(255); }
        }
        byte[] idat;
        using (var z = new MemoryStream())
        {
            using (var deflate = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw.ToArray());
            idat = z.ToArray();
        }
        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string tag, byte[] data)
        {
            var be = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(be, data.Length);
            png.Write(be);
            byte[] t = Encoding.ASCII.GetBytes(tag);
            png.Write(t);
            png.Write(data);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(be, Crc32([.. t, .. data]));
            png.Write(be);
        }
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, side);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), side);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk("IHDR", ihdr);
        Chunk("IDAT", idat);
        Chunk("IEND", []);
        return png.ToArray();
    }

    public static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>A zip of the entries as given, each with a Unix mode when one is given.</summary>
    public static string RawZip(string zip, params (string Name, byte[] Bytes, int? Mode)[] entries)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (entryName, bytes, mode) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            if (mode is { } m) entry.ExternalAttributes = m << 16;
            using var s = entry.Open();
            s.Write(bytes);
        }
        return zip;
    }

    /// <summary>Makes the zip say that an entry is <paramref name="said"/> bytes when unpacked,
    /// in its local header and in its central directory.</summary>
    public static void Understate(string zip, string entryName, uint said)
    {
        byte[] bytes = File.ReadAllBytes(zip);
        byte[] name = Encoding.UTF8.GetBytes(entryName);
        int patched = 0;
        for (int i = 0; i + 46 < bytes.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(bytes, i);
            if (sig == 0x02014b50 && bytes.AsSpan(i + 46, name.Length).SequenceEqual(name) && BitConverter.ToUInt16(bytes, i + 28) == name.Length)
            {
                BitConverter.GetBytes(said).CopyTo(bytes, i + 24);
                patched++;
            }
            else if (sig == 0x04034b50 && i + 30 + name.Length <= bytes.Length && bytes.AsSpan(i + 30, name.Length).SequenceEqual(name))
            {
                BitConverter.GetBytes(said).CopyTo(bytes, i + 22);
                patched++;
            }
        }
        Assert.Equal(2, patched);
        File.WriteAllBytes(zip, bytes);
    }
}
