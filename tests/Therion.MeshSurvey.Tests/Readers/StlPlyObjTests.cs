// Hand-written STL, PLY and OBJ files.

using System.Buffers.Binary;
using System.Text;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;

namespace Therion.MeshSurvey.Tests.Readers;

public sealed class StlPlyObjTests : ReaderTestBase
{
    private const string AsciiStl = """
        solid demo
          facet normal 0 0 1
            outer loop
              vertex 0 0 0
              vertex 1 0 0
              vertex 0 1 0
            endloop
          endfacet
          facet normal 0 0 1
            outer loop
              vertex 1 0 0
              vertex 1 1 0
              vertex 0 1 0
            endloop
          endfacet
          facet normal 0 0 1
            outer loop
              vertex 5 5 5
              vertex 5 5 5
              vertex 6 5 5
            endloop
          endfacet
        endsolid demo
        """;

    [Fact]
    public void AsciiStl_WeldsAndSkipsDegenerate()
    {
        var file = ReadText(AsciiStl, ModelFileFormat.Stl);
        Assert.Equal(2, file.Mesh!.Triangles.Count);
        Assert.Equal(4, file.Mesh.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct().Count());
        Assert.Contains("skipped 1 degenerate faces", file.Warnings);
    }

    private static byte[] BinaryStl(string header, params float[][] triangles)
    {
        var data = new byte[84 + 50 * triangles.Length];
        Encoding.ASCII.GetBytes(header).CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), (uint)triangles.Length);
        for (int i = 0; i < triangles.Length; i++)
            for (int k = 0; k < 9; k++)
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(84 + 50 * i + 12 + 4 * k), triangles[i][k]);
        return data;
    }

    [Fact]
    public void BinaryStl_WithSolidHeader()
    {
        var data = BinaryStl("solid but binary", [0, 0, 0, 1, 0, 0, 0, 1, 0], [1, 0, 0, 1, 1, 0, 0, 1, 0]);
        var path = WriteBytes("a.stl", data);
        Assert.True(ModelReader.TryDetectFormat(path, out var f));
        Assert.Equal(ModelFileFormat.Stl, f);
        var file = ModelReader.Read(path);
        Assert.Equal(4, file.Mesh!.Vertices.Count);
        Assert.Equal(2, file.Mesh.Triangles.Count);
    }

    [Fact]
    public void Ply_AsciiQuadWithUnknownElement()
    {
        const string ply = """
            ply
            format ascii 1.0
            comment hello
            obj_info something
            element vertex 4
            property float x
            property float y
            property float z
            property uchar red
            property uchar green
            property uchar blue
            property float quality
            element edge 2
            property list uchar int ends
            property float weight
            element face 1
            property uchar flags
            property list uchar uint vertex_indices
            element material 1
            property list ushort float params
            end_header
            0 0 0 255 0 0 0.5
            1 0 0 0 255 0 0.5
            1 1 0 0 0 255 0.5
            0 1 0 1 2 3 0.5
            2 0 1 0.25
            2 1 2 0.75
            7 4 0 1 2 3
            2 1.5 2.5
            """;
        var file = ReadText(ply, ModelFileFormat.Ply);
        var m = file.Mesh!;
        Assert.Equal(4, m.Vertices.Count);
        Assert.Equal(new CaveColor(0, 0, 255), m.VertexColors![2]);
        Assert.Equal([new CaveTriangle(0, 1, 2), new CaveTriangle(0, 2, 3)], m.Triangles);
        Assert.Contains("ignored 2 edge elements", file.Warnings);
    }

    [Fact]
    public void Ply_BinaryBigEndian_WithSkippedProperty()
    {
        var header = "ply\nformat binary_big_endian 1.0\nelement vertex 3\nproperty float32 x\nproperty float64 y\nproperty int16 z\nproperty uint8 pad\n"
            + "element face 1\nproperty list uint8 int32 vertex_index\nend_header\n";
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(header));
        void F(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleBigEndian(b, v); ms.Write(b); }
        void D(double v) { var b = new byte[8]; BinaryPrimitives.WriteDoubleBigEndian(b, v); ms.Write(b); }
        void S(short v) { var b = new byte[2]; BinaryPrimitives.WriteInt16BigEndian(b, v); ms.Write(b); }
        void I(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); ms.Write(b); }
        F(1.5f); D(2.25); S(-3); ms.WriteByte(9);
        F(4); D(5); S(6); ms.WriteByte(9);
        F(7); D(8); S(9); ms.WriteByte(9);
        ms.WriteByte(3); I(0); I(1); I(2);
        var file = ReadBytes(ms.ToArray(), ModelFileFormat.Ply);
        Assert.Equal(new CaveVector3(1.5, 2.25, -3), file.Mesh!.Vertices[0]);
        Assert.Equal(new CaveVector3(7, 8, 9), file.Mesh.Vertices[2]);
        Assert.Single(file.Mesh.Triangles);
    }

    [Fact]
    public void Ply_VerticesOnly_IsPointCloud()
    {
        var file = ReadText("ply\nformat ascii 1.0\nelement vertex 2\nproperty float x\nproperty float y\nproperty float z\nproperty float nx\nproperty float ny\nproperty float nz\nend_header\n1 2 3 0 0 1\n4 5 6 0 1 0\n", ModelFileFormat.Ply);
        Assert.True(file.IsPointCloud);
        Assert.Null(file.Mesh);
        Assert.Equal(2, file.Points!.Points.Count);
        Assert.Equal(new CaveVector3(0, 1, 0), file.Points.Normals![1]);
        Assert.Null(file.Points.Colors);
    }

    [Fact]
    public void Ply_VertexCountLargerThanData_Throws()
    {
        var ex = Assert.Throws<CaveFileFormatException>(() =>
            ReadText("ply\nformat ascii 1.0\nelement vertex 4000000000\nproperty float x\nproperty float y\nproperty float z\nend_header\n1 2 3\n", ModelFileFormat.Ply));
        Assert.Contains("vertex", ex.Message);
        Assert.Throws<CaveFileFormatException>(() =>
            ReadBytes(Encoding.ASCII.GetBytes("ply\nformat binary_little_endian 1.0\nelement vertex 5\nproperty float x\nproperty float y\nproperty float z\nend_header\n").Concat(new byte[24]).ToArray(), ModelFileFormat.Ply));
    }

    [Fact]
    public void Obj_NegativeIndices_Slashes_NGon_Continuation()
    {
        const string obj = """
            # comment
            mtllib x.mtl
            o thing
            v 0 0 0
            v 1 0 0
            v 1 1 0 # trailing comment
            v 0 1 0
            v 0 2 0 1
            vt 0 0
            vn 0 0 1
            usemtl m
            s off
            g grp
            f 1/1/1 2/1/1 3/1/1
            f -5//1 -4//1 \
               -3//1 -2//1
            f 1/1 3/2 5/3
            l 1 2
            """;
        var file = ReadText(obj, ModelFileFormat.Obj);
        var m = file.Mesh!;
        Assert.Equal(5, m.Vertices.Count);
        Assert.Equal(
            [new CaveTriangle(0, 1, 2), new CaveTriangle(0, 1, 2), new CaveTriangle(0, 2, 3), new CaveTriangle(0, 2, 4)],
            m.Triangles);
        Assert.Contains("ignored 1 line elements", file.Warnings);
    }

    [Fact]
    public void Obj_VerticesOnly_IsPointCloudWithColours()
    {
        var file = ReadText("v 1 2 3 1 0 0.5\nv 4 5 6 0 1 0\n", ModelFileFormat.Obj);
        Assert.True(file.IsPointCloud);
        Assert.Equal(new CaveColor(255, 0, 128), file.Points!.Colors![0]);
    }

    [Fact]
    public void Obj_FaceIndexOutOfRange_Throws()
    {
        var ex = Assert.Throws<CaveFileFormatException>(() => ReadText("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 4\n", ModelFileFormat.Obj));
        Assert.Contains("out of range", ex.Message);
        Assert.Throws<CaveFileFormatException>(() => ReadText("v 0 0 0\nf -2 -1 -1\n", ModelFileFormat.Obj));
    }

    [Fact]
    public void Stl_Truncated_Throws()
    {
        var data = BinaryStl("hdr", [0, 0, 0, 1, 0, 0, 0, 1, 0], [1, 0, 0, 1, 1, 0, 0, 1, 0]);
        var cut = data[..^10];
        var path = WriteBytes("cut.stl", cut);
        var ex = Assert.Throws<CaveFileFormatException>(() => ModelReader.Read(path));
        Assert.Contains("truncated", ex.Message);
    }
}
