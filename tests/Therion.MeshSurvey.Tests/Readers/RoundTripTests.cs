// Round trips: a small mesh through each writer and back through the matching reader.

using Therion.Cave3D;
using Therion.CaveGeometry;
using Therion.CaveGeometry.Writers;

namespace Therion.MeshSurvey.Tests.Readers;

public sealed class RoundTripTests : ReaderTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stl(bool colored)
    {
        var mesh = Meshes.Tetra(colored);
        var path = PathOf("t.stl");
        StlWriter.WriteFile(mesh, path);
        var file = ModelReader.Read(path);
        Assert.Equal(ModelFileFormat.Stl, file.Format);
        Assert.Equal(ModelUpAxis.Z, file.UpAxisHint);
        Assert.False(file.UnitsKnown);
        Assert.Equal(4, file.Mesh!.Vertices.Count); // welded back to the shared corners
        Assert.Equal(Meshes.Canon(mesh), Meshes.Canon(file.Mesh));
        Assert.Empty(file.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ply(bool colored)
    {
        var mesh = Meshes.Tetra(colored) with
        {
            TextureCoordinates = [new(0, 0), new(1, 0), new(0, 1), new(0.5, 0.5)],
            Reveal = [1, 2, 3, 4],
        };
        var path = PathOf("t.ply");
        PlyWriter.WriteFile(mesh, path);
        var file = ModelReader.Read(path);
        Assert.Equal(ModelFileFormat.Ply, file.Format);
        var m = file.Mesh!;
        Assert.Equal(mesh.Vertices, m.Vertices);
        Assert.Equal(mesh.Triangles, m.Triangles);
        Assert.Equal(mesh.VertexColors, m.VertexColors);
        Assert.Equal(mesh.TextureCoordinates, m.TextureCoordinates);
        Assert.Equal(mesh.Reveal, m.Reveal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Obj(bool colored)
    {
        var mesh = Meshes.Tetra(colored); // OBJ carries no colours
        var path = PathOf("t.obj");
        ObjWriter.WriteFile(mesh, path);
        var file = ModelReader.Read(path);
        Assert.Equal(ModelFileFormat.Obj, file.Format);
        Assert.Equal(mesh.Vertices, file.Mesh!.Vertices);
        Assert.Equal(mesh.Triangles, file.Mesh.Triangles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Glb(bool colored)
    {
        var mesh = Meshes.Tetra(colored);
        var path = PathOf("t.glb");
        GlbWriter.WriteFile(mesh, path);
        var file = ModelReader.Read(path);
        Assert.Equal(ModelFileFormat.Glb, file.Format);
        Assert.Equal(ModelUpAxis.Y, file.UpAxisHint);
        Assert.True(file.UnitsKnown);
        // The writer's Y-up coordinates, as stored: (x, y, z) is written as (x, z, -y).
        var expected = mesh.Vertices.Select(v => new CaveVector3(v.X, v.Z, -v.Y)).ToList();
        Assert.Equal(expected, file.Mesh!.Vertices);
        Assert.Equal(mesh.Triangles, file.Mesh.Triangles);
    }
}
