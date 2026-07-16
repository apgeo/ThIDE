// Binary PLY writer — the module's transport format (D-06): Blender's native C++ PLY
// importer is always present headless, and the format is a trivial dependency-free
// write. Emits binary_little_endian 1.0 with float32 positions, optional uchar RGB
// vertex colours (depth tint), and uchar-count/int32 triangle faces.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Therion.Blender.Writers;

/// <summary>Writes a <see cref="Therion.Blender.Geometry.CaveMesh"/> as a binary PLY file.</summary>
public static class PlyWriter
{
    public static void WriteFile(Geometry.CaveMesh mesh, string path)
        => File.WriteAllBytes(path, Write(mesh));

    public static byte[] Write(Geometry.CaveMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        bool colored = mesh.HasColors;
        if (colored && mesh.VertexColors!.Count != mesh.Vertices.Count)
            throw new InvalidOperationException("Vertex colour count must match vertex count.");

        using var stream = new MemoryStream();

        // Header (ASCII, LF line endings — required by the format).
        var header = new StringBuilder();
        header.Append("ply\n");
        header.Append("format binary_little_endian 1.0\n");
        header.Append("comment Created by ThIDE Blender module\n");
        header.Append(CultureInfo.InvariantCulture, $"element vertex {mesh.Vertices.Count}\n");
        header.Append("property float x\nproperty float y\nproperty float z\n");
        if (colored)
            header.Append("property uchar red\nproperty uchar green\nproperty uchar blue\n");
        header.Append(CultureInfo.InvariantCulture, $"element face {mesh.Triangles.Count}\n");
        header.Append("property list uchar int vertex_indices\n");
        header.Append("end_header\n");
        stream.Write(Encoding.ASCII.GetBytes(header.ToString()));

        Span<byte> f4 = stackalloc byte[4];
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            WriteFloat(stream, f4, (float)v.X);
            WriteFloat(stream, f4, (float)v.Y);
            WriteFloat(stream, f4, (float)v.Z);
            if (colored)
            {
                var c = mesh.VertexColors![i];
                stream.WriteByte(c.R);
                stream.WriteByte(c.G);
                stream.WriteByte(c.B);
            }
        }

        foreach (var t in mesh.Triangles)
        {
            stream.WriteByte(3);
            WriteInt(stream, f4, (int)t.A);
            WriteInt(stream, f4, (int)t.B);
            WriteInt(stream, f4, (int)t.C);
        }

        return stream.ToArray();
    }

    private static void WriteFloat(MemoryStream stream, Span<byte> buffer, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt(MemoryStream stream, Span<byte> buffer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
