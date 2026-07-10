// Minimal binary glTF (.glb) writer (optional user-facing export, FR-02): a single
// self-contained buffer with float32 POSITION and uint32 indices, one mesh/node/scene.
// No normals/materials — "minimal" per D-06/plan; Blender computes normals on import.
// SharpGLTF (MIT) is only pulled in later if glb needs outgrow this.

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Therion.Blender.Writers;

/// <summary>Writes a <see cref="Therion.Blender.Geometry.CaveMesh"/> as a binary glTF 2.0 file.</summary>
public static class GlbWriter
{
    private const uint Magic = 0x46546C67;      // "glTF"
    private const uint Version = 2;
    private const uint JsonChunkType = 0x4E4F534A; // "JSON"
    private const uint BinChunkType = 0x004E4942;  // "BIN\0"

    public static void WriteFile(Geometry.CaveMesh mesh, string path)
        => File.WriteAllBytes(path, Write(mesh));

    public static byte[] Write(Geometry.CaveMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        int vertexCount = mesh.Vertices.Count;
        int indexCount = mesh.Triangles.Count * 3;

        // ---- binary buffer: positions (float32×3) then indices (uint32) ----
        int positionBytes = vertexCount * 12;
        int indexBytes = indexCount * 4;
        var bin = new byte[positionBytes + indexBytes];

        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < vertexCount; i++)
        {
            var v = mesh.Vertices[i];
            float x = (float)v.X, y = (float)v.Y, z = (float)v.Z;
            BinaryPrimitives.WriteSingleLittleEndian(bin.AsSpan(i * 12), x);
            BinaryPrimitives.WriteSingleLittleEndian(bin.AsSpan(i * 12 + 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(bin.AsSpan(i * 12 + 8), z);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        if (vertexCount == 0)
        {
            minX = minY = minZ = 0;
            maxX = maxY = maxZ = 0;
        }

        int offset = positionBytes;
        foreach (var t in mesh.Triangles)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(offset), t.A); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(offset), t.B); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(offset), t.C); offset += 4;
        }

        // ---- glTF JSON ----
        var gltf = new
        {
            asset = new { version = "2.0", generator = "ThIDE Blender module" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0 } },
            meshes = new[]
            {
                new { primitives = new[] { new { attributes = new { POSITION = 0 }, indices = 1 } } },
            },
            buffers = new[] { new { byteLength = bin.Length } },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = positionBytes, target = 34962 },        // ARRAY_BUFFER
                new { buffer = 0, byteOffset = positionBytes, byteLength = indexBytes, target = 34963 }, // ELEMENT_ARRAY_BUFFER
            },
            accessors = new object[]
            {
                new
                {
                    bufferView = 0, componentType = 5126 /* FLOAT */, count = vertexCount, type = "VEC3",
                    min = new[] { minX, minY, minZ }, max = new[] { maxX, maxY, maxZ },
                },
                new { bufferView = 1, componentType = 5125 /* UNSIGNED_INT */, count = indexCount, type = "SCALAR" },
            },
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(gltf);

        // ---- assemble container (each chunk padded to a 4-byte boundary) ----
        var jsonChunk = Pad(jsonBytes, 0x20);   // JSON padded with spaces
        var binChunk = Pad(bin, 0x00);          // BIN padded with zeros

        int total = 12 + 8 + jsonChunk.Length + 8 + binChunk.Length;
        using var stream = new MemoryStream(total);
        Span<byte> word = stackalloc byte[4];

        WriteU32(stream, word, Magic);
        WriteU32(stream, word, Version);
        WriteU32(stream, word, (uint)total);

        WriteU32(stream, word, (uint)jsonChunk.Length);
        WriteU32(stream, word, JsonChunkType);
        stream.Write(jsonChunk);

        WriteU32(stream, word, (uint)binChunk.Length);
        WriteU32(stream, word, BinChunkType);
        stream.Write(binChunk);

        return stream.ToArray();
    }

    private static byte[] Pad(byte[] data, byte padByte)
    {
        int padded = (data.Length + 3) & ~3;
        if (padded == data.Length) return data;
        var result = new byte[padded];
        data.CopyTo(result, 0);
        for (int i = data.Length; i < padded; i++) result[i] = padByte;
        return result;
    }

    private static void WriteU32(MemoryStream stream, Span<byte> word, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(word, value);
        stream.Write(word);
    }
}
