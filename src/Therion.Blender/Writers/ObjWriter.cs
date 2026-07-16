// Wavefront OBJ writer (optional user-facing export, FR-02): ASCII `v x y z` lines and
// 1-indexed `f a b c` faces. All floats formatted with InvariantCulture (R-08).

using System.Globalization;
using System.Text;

namespace Therion.Blender.Writers;

/// <summary>Writes a <see cref="Therion.Blender.Geometry.CaveMesh"/> as a Wavefront OBJ file.</summary>
public static class ObjWriter
{
    public static void WriteFile(Geometry.CaveMesh mesh, string path)
        => File.WriteAllText(path, Write(mesh), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static string Write(Geometry.CaveMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var sb = new StringBuilder();
        sb.Append("# Created by ThIDE Blender module\n");
        sb.Append("o cave\n");

        foreach (var v in mesh.Vertices)
            sb.Append(CultureInfo.InvariantCulture, $"v {Fmt(v.X)} {Fmt(v.Y)} {Fmt(v.Z)}\n");

        foreach (var t in mesh.Triangles)
            sb.Append(CultureInfo.InvariantCulture, $"f {t.A + 1} {t.B + 1} {t.C + 1}\n");

        return sb.ToString();
    }

    // Round-trippable, culture-invariant float text ("R" avoids trailing-zero noise).
    private static string Fmt(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
