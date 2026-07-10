// Binary STL writer (optional user-facing export, FR-02): 80-byte header, uint32
// triangle count, then per-triangle float32 normal + 3 float32 vertices + uint16 attr.
// Normals are computed from triangle winding.

using System.Buffers.Binary;

namespace Therion.Blender.Writers;

/// <summary>Writes a <see cref="Therion.Blender.Geometry.CaveMesh"/> as a binary STL file.</summary>
public static class StlWriter
{
    public static void WriteFile(Geometry.CaveMesh mesh, string path)
        => File.WriteAllBytes(path, Write(mesh));

    public static byte[] Write(Geometry.CaveMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        using var stream = new MemoryStream();

        Span<byte> header = stackalloc byte[80];
        header.Clear();
        System.Text.Encoding.ASCII.GetBytes("ThIDE Blender module", header);
        stream.Write(header);

        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)mesh.Triangles.Count);
        stream.Write(u32);

        var vertices = mesh.Vertices;
        Span<byte> f4 = stackalloc byte[4];
        foreach (var t in mesh.Triangles)
        {
            var a = vertices[(int)t.A];
            var b = vertices[(int)t.B];
            var c = vertices[(int)t.C];
            var normal = MeshNormals.Face(a, b, c);
            WriteVec(stream, f4, normal);
            WriteVec(stream, f4, a);
            WriteVec(stream, f4, b);
            WriteVec(stream, f4, c);
            stream.WriteByte(0); // attribute byte count
            stream.WriteByte(0);
        }

        return stream.ToArray();
    }

    private static void WriteVec(MemoryStream stream, Span<byte> buffer, CaveVector3 v)
    {
        WriteFloat(stream, buffer, (float)v.X);
        WriteFloat(stream, buffer, (float)v.Y);
        WriteFloat(stream, buffer, (float)v.Z);
    }

    private static void WriteFloat(MemoryStream stream, Span<byte> buffer, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
