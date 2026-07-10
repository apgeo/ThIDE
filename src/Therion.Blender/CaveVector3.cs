namespace Therion.Blender;

/// <summary>
/// A 3D point/vector in the source file's own world coordinate system, kept in double
/// precision (cave models routinely carry UTM-scale coordinates, 10⁵–10⁷ m, which do
/// not survive float32 — recentering happens in the geometry stage, BA-B3/D-15).
/// </summary>
public readonly record struct CaveVector3(double X, double Y, double Z)
{
    public override string ToString()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");
}
