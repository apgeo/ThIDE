namespace Therion.Blender.Writers;

/// <summary>Shared triangle-normal helper for writers that need face normals (STL, glb).</summary>
internal static class MeshNormals
{
    /// <summary>The unit face normal of a triangle by its right-hand winding; returns
    /// <see cref="CaveVector3.UnitZ"/> for a degenerate (zero-area) triangle so writers
    /// never emit NaNs.</summary>
    public static CaveVector3 Face(CaveVector3 a, CaveVector3 b, CaveVector3 c)
    {
        var normal = (b - a).Cross(c - a);
        double length = normal.Length;
        return length < 1e-20 ? CaveVector3.UnitZ : normal / length;
    }
}
