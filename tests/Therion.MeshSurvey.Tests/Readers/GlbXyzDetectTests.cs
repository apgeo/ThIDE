// Hand-built glb files, XYZ/PTS text, format detection and the one-million-triangle STL.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;
using Therion.CaveGeometry.Writers;

namespace Therion.MeshSurvey.Tests.Readers;

public sealed class GlbXyzDetectTests : ReaderTestBase
{
    private static byte[] Floats(params float[] values)
    {
        var b = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(i * 4), values[i]);
        return b;
    }

    private const string TriangleBufferViews =
        "\"buffers\":[{\"byteLength\":36}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36}],"
        + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}]";

    [Fact]
    public void Glb_NodeHierarchyTransforms()
    {
        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],"
            + "\"nodes\":[{\"translation\":[10,0,0],\"scale\":[2,2,2],\"children\":[1]},"
            + "{\"rotation\":[0,0,0.7071067811865476,0.7071067811865476],\"mesh\":0}],"
            + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]," + TriangleBufferViews + "}";
        var file = ReadBytes(Meshes.BuildGlb(json, Floats(1, 0, 0, 0, 1, 0, 0, 0, 0)), ModelFileFormat.Glb);
        var v = file.Mesh!.Vertices;
        Assert.Equal(10, v[0].X, 6); Assert.Equal(2, v[0].Y, 6); Assert.Equal(0, v[0].Z, 6);
        Assert.Equal(8, v[1].X, 6); Assert.Equal(0, v[1].Y, 6);
        Assert.Equal(10, v[2].X, 6); Assert.Equal(0, v[2].Y, 6);
        Assert.Single(file.Mesh.Triangles);
    }

    [Fact]
    public void Glb_U8AndU32Indices_NoScenes()
    {
        var bin = new List<byte>();
        bin.AddRange(Floats(0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0)); // 4 vertices, 48 bytes
        bin.AddRange(new byte[] { 0, 1, 2, 0, 2, 3, 0, 0 });       // u8 indices at 48, 6 bytes (+2 pad)
        foreach (uint i in new uint[] { 0, 1, 2, 0, 2, 3 }) bin.AddRange(BitConverter.GetBytes(i)); // u32 at 56
        var json = "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"mesh\":0}],"
            + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1},{\"attributes\":{\"POSITION\":0},\"indices\":2}]}],"
            + "\"buffers\":[{\"byteLength\":80}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":48},"
            + "{\"buffer\":0,\"byteOffset\":48,\"byteLength\":6},{\"buffer\":0,\"byteOffset\":56,\"byteLength\":24}],"
            + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"},"
            + "{\"bufferView\":1,\"componentType\":5121,\"count\":6,\"type\":\"SCALAR\"},"
            + "{\"bufferView\":2,\"componentType\":5125,\"count\":6,\"type\":\"SCALAR\"}]}";
        var file = ReadBytes(Meshes.BuildGlb(json, bin.ToArray()), ModelFileFormat.Glb);
        Assert.Equal(4, file.Mesh!.Triangles.Count);
        Assert.Equal(8, file.Mesh.Vertices.Count);
        Assert.Equal(new CaveTriangle(4, 5, 6), file.Mesh.Triangles[2]);
    }

    [Fact]
    public void Glb_RequiredDracoExtension_Throws()
    {
        var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsRequired\":[\"KHR_draco_mesh_compression\"]}";
        var ex = Assert.Throws<CaveFileFormatException>(() => ReadBytes(Meshes.BuildGlb(json, []), ModelFileFormat.Glb));
        Assert.Contains("KHR_draco_mesh_compression", ex.Message);
    }

    [Fact]
    public void Glb_BadMagic_Throws()
    {
        var data = Meshes.BuildGlb("{\"asset\":{\"version\":\"2.0\"}}", []);
        data[0] = (byte)'X';
        var path = WriteBytes("bad.glb", data);
        var ex = Assert.Throws<CaveFileFormatException>(() => ModelReader.Read(path));
        Assert.Contains("magic", ex.Message);
    }

    [Fact]
    public void Glb_PointsOnly_IsPointCloud_LinesWarn()
    {
        var json = "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"mesh\":0}],"
            + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"mode\":0},{\"attributes\":{\"POSITION\":0},\"mode\":1}]}]," + TriangleBufferViews + "}";
        var file = ReadBytes(Meshes.BuildGlb(json, Floats(1, 2, 3, 4, 5, 6, 7, 8, 9)), ModelFileFormat.Glb);
        Assert.True(file.IsPointCloud);
        Assert.Equal(3, file.Points!.Points.Count);
        Assert.Contains("ignored 1 line primitives", file.Warnings);
    }

    [Fact]
    public void Glb_AccessorPastEnd_Throws()
    {
        var json = "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"mesh\":0}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}],"
            + "\"buffers\":[{\"byteLength\":36}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":36}],"
            + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3000000,\"type\":\"VEC3\"}]}";
        Assert.Throws<CaveFileFormatException>(() => ReadBytes(Meshes.BuildGlb(json, new byte[36]), ModelFileFormat.Glb));
    }

    [Fact]
    public void Xyz_CommasHeaderCommentsColours()
    {
        const string text = "x,y,z,r,g,b\n# comment\n// another\n\n1.5,2,3,255,0,10\n4;5;6;1;2;3\n7 8 9 0 0 300\n";
        var file = ReadText(text, ModelFileFormat.Xyz);
        Assert.True(file.IsPointCloud);
        Assert.Equal(3, file.Points!.Points.Count);
        Assert.Equal(new CaveVector3(1.5, 2, 3), file.Points.Points[0]);
        Assert.Null(file.Points.Colors); // the last row has no valid colour, so none are kept
        Assert.Contains(file.Warnings, w => w.Contains("skipped 1"));

        var coloured = ReadText("1 2 3 255 0 10\n4 5 6 1 2 3\n", ModelFileFormat.Xyz);
        Assert.Equal(new CaveColor(255, 0, 10), coloured.Points!.Colors![0]);
    }

    [Fact]
    public void Pts_CountLineSkipped()
    {
        var file = ReadText("2\n1 2 3\n4 5 6\n", ModelFileFormat.Xyz);
        Assert.Equal(2, file.Points!.Points.Count);
        Assert.Empty(file.Warnings);
    }

    [Fact]
    public void Detect_ByContentOverExtension()
    {
        void Check(string name, byte[] data, ModelFileFormat? expected)
        {
            var path = WriteBytes(name, data);
            bool ok = ModelReader.TryDetectFormat(path, out var f);
            Assert.Equal(expected is not null, ok);
            if (expected is not null) Assert.Equal(expected, f);
        }

        var mesh = Meshes.Tetra(false);
        Check("a.dat", StlWriter.Write(mesh), ModelFileFormat.Stl);
        Check("b.dat", Encoding.ASCII.GetBytes("solid x\n facet normal 0 0 1\n"), ModelFileFormat.Stl);
        Check("c.txt", Encoding.ASCII.GetBytes("ply\nformat ascii 1.0\nend_header\n"), ModelFileFormat.Ply);
        Check("d.bin", PlyWriter.Write(mesh), ModelFileFormat.Ply);
        Check("e.bin", GlbWriter.Write(mesh), ModelFileFormat.Glb);
        Check("f.dat", Encoding.ASCII.GetBytes("# hi\nv 1 2 3\nf 1 1 1\n"), ModelFileFormat.Obj);
        Check("g.OBJ", Encoding.ASCII.GetBytes("o nothing\n"), ModelFileFormat.Obj);
        Check("h.XYZ", Encoding.ASCII.GetBytes("1 2 3\n"), ModelFileFormat.Xyz);
        Check("i.pts", Encoding.ASCII.GetBytes("1\n1 2 3\n"), ModelFileFormat.Xyz);
        Check("j.csv", Encoding.ASCII.GetBytes("a,b,c\n1,2,3\n"), ModelFileFormat.Xyz);
        Check("k.txt", Encoding.ASCII.GetBytes("1 2 3\n"), ModelFileFormat.Xyz);
        Check("l.txt", Encoding.ASCII.GetBytes("just some prose\n"), null);
        Check("m.dat", Encoding.ASCII.GetBytes("1 2 3\n"), null);
        Check("n.STL", Encoding.ASCII.GetBytes("garbage"), ModelFileFormat.Stl);
        Assert.False(ModelReader.TryDetectFormat(PathOf("missing.stl"), out _));
    }

    [Fact]
    public void MillionTriangleStl_ReadsFastAndWelds()
    {
        const int n = 708; // n x n cells, two triangles each
        var path = PathOf("big.stl");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            var header = new byte[84];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), (uint)(2 * n * n));
            fs.Write(header);
            var rec = new byte[50];
            void Tri(int x0, int y0, int x1, int y1, int x2, int y2)
            {
                float[] p = [x0, y0, 0, x1, y1, 0, x2, y2, 0];
                for (int k = 0; k < 9; k++) BinaryPrimitives.WriteSingleLittleEndian(rec.AsSpan(12 + 4 * k), p[k]);
                fs.Write(rec);
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Tri(x, y, x + 1, y, x, y + 1);
                    Tri(x + 1, y, x + 1, y + 1, x, y + 1);
                }
        }

        var sw = Stopwatch.StartNew();
        var file = ModelReader.Read(path);
        sw.Stop();
        Assert.Equal(2 * n * n, file.Mesh!.Triangles.Count);
        Assert.Equal((n + 1) * (n + 1), file.Mesh.Vertices.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }
}
