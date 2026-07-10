namespace Therion.Blender.Geometry;

/// <summary>
/// An axis-aligned bounding box in world/local coordinates. Empty when no points were
/// supplied (<see cref="IsEmpty"/>); consumers should guard on that before using extents.
/// </summary>
public readonly record struct BoundingBox(CaveVector3 Min, CaveVector3 Max)
{
    public bool IsEmpty => Min.X > Max.X;

    /// <summary>The empty box (min &gt; max), the identity for <see cref="Union"/>.</summary>
    public static readonly BoundingBox Empty = new(
        new CaveVector3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new CaveVector3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public CaveVector3 Center => IsEmpty ? CaveVector3.Zero : (Min + Max) * 0.5;
    public CaveVector3 Size => IsEmpty ? CaveVector3.Zero : Max - Min;
    public double Diagonal => IsEmpty ? 0.0 : Size.Length;

    public BoundingBox Encapsulate(CaveVector3 p) => new(
        new CaveVector3(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)),
        new CaveVector3(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));

    public BoundingBox Union(BoundingBox other)
    {
        if (other.IsEmpty) return this;
        if (IsEmpty) return other;
        return new BoundingBox(
            new CaveVector3(Math.Min(Min.X, other.Min.X), Math.Min(Min.Y, other.Min.Y), Math.Min(Min.Z, other.Min.Z)),
            new CaveVector3(Math.Max(Max.X, other.Max.X), Math.Max(Max.Y, other.Max.Y), Math.Max(Max.Z, other.Max.Z)));
    }

    public BoundingBox Translate(CaveVector3 offset)
        => IsEmpty ? this : new BoundingBox(Min + offset, Max + offset);

    public static BoundingBox FromPoints(IEnumerable<CaveVector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var box = Empty;
        foreach (var p in points) box = box.Encapsulate(p);
        return box;
    }
}
