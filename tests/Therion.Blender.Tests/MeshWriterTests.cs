// Mesh-writer tests: PLY/STL round-trip through tiny test-side re-readers, OBJ text
// assertions (incl. ro-RO culture — R-08), minimal-glb structural validity, and
// determinism. A cube fixture exercises multi-vertex/multi-face output.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Therion.Blender;
using Therion.Blender.Geometry;
using Therion.Blender.Writers;

namespace Therion.Blender.Tests;

public class MeshWriterTests
{
    // A unit cube: 8 vertices, 12 triangles, with per-vertex colours.
    private static CaveMesh Cube()
    {
        var v = new CaveVector3[]
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        };
        var t = new CaveTriangle[]
        {
            new(0, 1, 2), new(0, 2, 3), // bottom
            new(4, 6, 5), new(4, 7, 6), // top
            new(0, 4, 5), new(0, 5, 1), // sides
            new(1, 5, 6), new(1, 6, 2),
            new(2, 6, 7), new(2, 7, 3),
            new(3, 7, 4), new(3, 4, 0),
        };
        var colors = new CaveColor[v.Length];
        for (int i = 0; i < v.Length; i++) colors[i] = new CaveColor((byte)(i * 30), 100, (byte)(255 - i * 30));
        return new CaveMesh { Vertices = v, Triangles = t, VertexColors = colors };
    }

    [Fact]
    public void Ply_RoundTripsPositionsColorsAndFaces()
    {
        var mesh = Cube();
        var bytes = PlyWriter.Write(mesh);
        var (vertices, colors, faces) = ReadPly(bytes);

        Assert.Equal(mesh.Vertices.Count, vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
            AssertVecClose(mesh.Vertices[i], vertices[i]);
        Assert.Equal(mesh.VertexColors, colors);
        Assert.Equal(mesh.Triangles, faces);
    }

    [Fact]
    public void Ply_WithoutColors_OmitsColorProperties()
    {
        var mesh = Cube() with { VertexColors = null };
        var bytes = PlyWriter.Write(mesh);
        var header = Encoding.ASCII.GetString(bytes, 0, IndexOfHeaderEnd(bytes));
        Assert.DoesNotContain("property uchar red", header);
        var (vertices, colors, faces) = ReadPly(bytes);
        Assert.Null(colors);
        Assert.Equal(8, vertices.Count);
        Assert.Equal(12, faces.Count);
    }

    [Fact]
    public void Stl_RoundTripsTriangleGeometry()
    {
        var mesh = Cube();
        var bytes = StlWriter.Write(mesh);

        Assert.Equal(84 + 50 * mesh.Triangles.Count, bytes.Length); // 80 header + u32 + 50/tri
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80));
        Assert.Equal((uint)mesh.Triangles.Count, count);

        // First triangle's three vertices match the mesh (normal + 3 verts, offset 84).
        int off = 84 + 12; // skip normal
        var a = ReadVec(bytes, ref off);
        var b = ReadVec(bytes, ref off);
        var c = ReadVec(bytes, ref off);
        AssertVecClose(mesh.Vertices[(int)mesh.Triangles[0].A], a);
        AssertVecClose(mesh.Vertices[(int)mesh.Triangles[0].B], b);
        AssertVecClose(mesh.Vertices[(int)mesh.Triangles[0].C], c);
    }

    [Fact]
    public void Obj_EmitsInvariantFloats_EvenUnderRoRoCulture()
    {
        var mesh = new CaveMesh
        {
            Vertices = [new CaveVector3(1.5, -2.25, 0.125)],
            Triangles = [],
        };

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO"); // decimal comma locale
            var text = ObjWriter.Write(mesh);
            Assert.Contains("v 1.5 -2.25 0.125", text);
            Assert.DoesNotContain(",", text); // never a decimal comma
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Obj_FacesAreOneIndexed()
    {
        var text = ObjWriter.Write(Cube());
        Assert.Contains("f 1 2 3", text);   // triangle (0,1,2) → 1-indexed
        Assert.DoesNotContain("f 0 ", text);
    }

    [Fact]
    public void Glb_HasValidContainerAndParsableJson()
    {
        var mesh = Cube();
        var bytes = GlbWriter.Write(mesh);

        Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));            // "glTF"
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));           // version
        Assert.Equal((uint)bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));

        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        Assert.Equal(0x4E4F534Au, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16))); // "JSON"
        Assert.Equal(0u, jsonLen % 4);

        using var doc = JsonDocument.Parse(bytes.AsSpan(20, (int)jsonLen).ToArray());
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        Assert.Equal(mesh.Vertices.Count, root.GetProperty("accessors")[0].GetProperty("count").GetInt32());
        Assert.Equal(mesh.Triangles.Count * 3, root.GetProperty("accessors")[1].GetProperty("count").GetInt32());

        // BIN chunk follows the JSON chunk and matches the declared buffer length.
        int binHeader = 20 + (int)jsonLen;
        uint binLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binHeader));
        Assert.Equal(0x004E4942u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binHeader + 4))); // "BIN\0"
        int declaredBuffer = root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32();
        Assert.True(binLen >= declaredBuffer); // padded to a 4-byte boundary
    }

    [Theory]
    [InlineData("ply")]
    [InlineData("stl")]
    [InlineData("obj")]
    [InlineData("glb")]
    public void AllWriters_AreDeterministic(string format)
    {
        var mesh = Cube();
        byte[] Write() => format switch
        {
            "ply" => PlyWriter.Write(mesh),
            "stl" => StlWriter.Write(mesh),
            "obj" => Encoding.UTF8.GetBytes(ObjWriter.Write(mesh)),
            _ => GlbWriter.Write(mesh),
        };
        Assert.Equal(Write(), Write());
    }

    [Fact]
    public void RealAvCerbul_ConvertsToPlyWithWalls()
    {
        var result = GeometryStage.Build(Parsing.LoxReader.ReadFile(TestCorpus.AvCerbulLox()));
        var bytes = PlyWriter.Write(result.Walls);

        var (vertices, colors, faces) = ReadPly(bytes);
        Assert.Equal(result.Walls.Vertices.Count, vertices.Count);
        Assert.Equal(result.Walls.Triangles.Count, faces.Count);
        Assert.NotNull(colors); // depth tint on by default
    }

    // ---- tiny test-side readers ----

    private static (List<CaveVector3> Vertices, List<CaveColor>? Colors, List<CaveTriangle> Faces) ReadPly(byte[] bytes)
    {
        int headerEnd = IndexOfHeaderEnd(bytes);
        var header = Encoding.ASCII.GetString(bytes, 0, headerEnd);
        var lines = header.Split('\n');
        int vertexCount = 0, faceCount = 0;
        bool hasColor = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("element vertex ", StringComparison.Ordinal))
                vertexCount = int.Parse(line.AsSpan("element vertex ".Length), CultureInfo.InvariantCulture);
            else if (line.StartsWith("element face ", StringComparison.Ordinal))
                faceCount = int.Parse(line.AsSpan("element face ".Length), CultureInfo.InvariantCulture);
            else if (line.Contains("uchar red")) hasColor = true;
        }

        int pos = headerEnd;
        var vertices = new List<CaveVector3>(vertexCount);
        var colors = hasColor ? new List<CaveColor>(vertexCount) : null;
        for (int i = 0; i < vertexCount; i++)
        {
            float x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(pos)); pos += 4;
            float y = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(pos)); pos += 4;
            float z = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(pos)); pos += 4;
            vertices.Add(new CaveVector3(x, y, z));
            if (hasColor)
            {
                colors!.Add(new CaveColor(bytes[pos], bytes[pos + 1], bytes[pos + 2]));
                pos += 3;
            }
        }

        var faces = new List<CaveTriangle>(faceCount);
        for (int i = 0; i < faceCount; i++)
        {
            byte n = bytes[pos]; pos += 1;
            Assert.Equal(3, n);
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos)); pos += 4;
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos)); pos += 4;
            uint c = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos)); pos += 4;
            faces.Add(new CaveTriangle(a, b, c));
        }
        return (vertices, colors, faces);
    }

    private static int IndexOfHeaderEnd(byte[] bytes)
    {
        var marker = Encoding.ASCII.GetBytes("end_header\n");
        for (int i = 0; i <= bytes.Length - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
                if (bytes[i + j] != marker[j]) { match = false; break; }
            if (match) return i + marker.Length;
        }
        throw new InvalidOperationException("PLY header end not found.");
    }

    private static CaveVector3 ReadVec(byte[] bytes, ref int offset)
    {
        float x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)); offset += 4;
        float y = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)); offset += 4;
        float z = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)); offset += 4;
        return new CaveVector3(x, y, z);
    }

    private static void AssertVecClose(CaveVector3 expected, CaveVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
    }
}
