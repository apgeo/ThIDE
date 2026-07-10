namespace Therion.Blender;

/// <summary>
/// A 3D point/vector in the source file's own world coordinate system, kept in double
/// precision (cave models routinely carry UTM-scale coordinates, 10⁵–10⁷ m, which do
/// not survive float32 — recentering happens in the geometry stage, BA-B3/D-15).
/// Axes are (X, Y, Z) = (Easting, Northing, Up) in metres, which maps directly onto
/// Blender's Z-up world with no axis swap.
/// </summary>
public readonly record struct CaveVector3(double X, double Y, double Z)
{
    public static readonly CaveVector3 Zero = new(0, 0, 0);
    public static readonly CaveVector3 UnitZ = new(0, 0, 1);

    public static CaveVector3 operator +(CaveVector3 a, CaveVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static CaveVector3 operator -(CaveVector3 a, CaveVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static CaveVector3 operator *(CaveVector3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static CaveVector3 operator *(double s, CaveVector3 a) => a * s;
    public static CaveVector3 operator /(CaveVector3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(CaveVector3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public CaveVector3 Cross(CaveVector3 b) =>
        new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Unit vector in the same direction; returns <see cref="Zero"/> for a (near-)zero vector.</summary>
    public CaveVector3 Normalized()
    {
        double length = Length;
        return length < 1e-12 ? Zero : this / length;
    }

    /// <summary>Linear interpolation from this to <paramref name="b"/> at <paramref name="t"/>.</summary>
    public CaveVector3 Lerp(CaveVector3 b, double t) => this + (b - this) * t;

    public override string ToString()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");
}
