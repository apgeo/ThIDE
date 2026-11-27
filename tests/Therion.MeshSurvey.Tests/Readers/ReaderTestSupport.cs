// Shared helpers for the model reader tests: a temp directory per test class, small meshes,
// a glb builder and an order-independent comparison of the triangles of two meshes.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.MeshSurvey.Tests.Readers;

public abstract class ReaderTestBase : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-readers-" + Guid.NewGuid().ToString("N"));

    protected ReaderTestBase() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    protected string PathOf(string name) => Path.Combine(_dir, name);

    protected string WriteBytes(string name, byte[] data)
    {
        var p = PathOf(name);
        File.WriteAllBytes(p, data);
        return p;
    }

    protected string WriteText(string name, string text)
    {
        var p = PathOf(name);
        File.WriteAllText(p, text, new UTF8Encoding(false));
        return p;
    }

    protected static ModelFile ReadBytes(byte[] data, ModelFileFormat format)
        => ModelReader.Read(new MemoryStream(data), format);

    protected static ModelFile ReadText(string text, ModelFileFormat format)
        => ReadBytes(Encoding.UTF8.GetBytes(text), format);
}

internal static class Meshes
{
    /// <summary>A tetrahedron with float-exact coordinates.</summary>
    public static CaveMesh Tetra(bool colored) => new()
    {
        Vertices =
        [
            new CaveVector3(0, 0, 0), new CaveVector3(2, 0, 0.5), new CaveVector3(0, 3, 1.25), new CaveVector3(1, 1, 4),
        ],
        Triangles = [new(0, 2, 1), new(0, 1, 3), new(1, 2, 3), new(2, 0, 3)],
        VertexColors = colored
            ? [new CaveColor(255, 0, 0), new CaveColor(0, 255, 0), new CaveColor(0, 0, 255), new CaveColor(10, 20, 30)]
            : null,
    };

    /// <summary>The triangles of a mesh as sorted, rotation-normalised coordinate strings, so two
    /// meshes compare equal whatever their vertex numbering; <paramref name="map"/> moves each vertex first.</summary>
    public static List<string> Canon(CaveMesh mesh, Func<CaveVector3, CaveVector3>? map = null)
    {
        var result = new List<string>();
        foreach (var t in mesh.Triangles)
        {
            var corners = new[] { t.A, t.B, t.C }
                .Select(i => Fmt(map is null ? mesh.Vertices[(int)i] : map(mesh.Vertices[(int)i]))).ToArray();
            int min = Array.IndexOf(corners, corners.Min(StringComparer.Ordinal));
            result.Add(string.Join("|", corners[min], corners[(min + 1) % 3], corners[(min + 2) % 3]));
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string Fmt(CaveVector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.####},{v.Y:0.####},{v.Z:0.####}");

    public static byte[] BuildGlb(string json, byte[] bin)
    {
        var j = Encoding.UTF8.GetBytes(json);
        var jp = new byte[(j.Length + 3) & ~3];
        Array.Fill(jp, (byte)' ');
        j.CopyTo(jp, 0);
        var bp = new byte[(bin.Length + 3) & ~3];
        bin.CopyTo(bp, 0);
        using var ms = new MemoryStream();
        void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }
        U32(0x46546C67); U32(2); U32((uint)(12 + 8 + jp.Length + 8 + bp.Length));
        U32((uint)jp.Length); U32(0x4E4F534A); ms.Write(jp);
        U32((uint)bp.Length); U32(0x004E4942); ms.Write(bp);
        return ms.ToArray();
    }
}
