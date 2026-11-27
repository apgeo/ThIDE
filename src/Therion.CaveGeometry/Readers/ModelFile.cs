// The result of reading a 3-D model file: either a triangle mesh or a point cloud, in the file's
// own coordinates, plus hints a later stage needs to normalise it (up axis, whether units are known).

namespace Therion.CaveGeometry;

/// <summary>The model file formats <see cref="ModelReader"/> understands.</summary>
public enum ModelFileFormat
{
    /// <summary>STL, binary or ASCII.</summary>
    Stl,
    /// <summary>Stanford PLY, ASCII or binary.</summary>
    Ply,
    /// <summary>Wavefront OBJ.</summary>
    Obj,
    /// <summary>Binary glTF 2.0.</summary>
    Glb,
    /// <summary>A text list of points (xyz, pts, csv, txt).</summary>
    Xyz,
}

/// <summary>Which axis a file's coordinates take as up.</summary>
public enum ModelUpAxis
{
    /// <summary>+Z is up (the convention of the model files ThIDE writes).</summary>
    Z,
    /// <summary>+Y is up (glTF).</summary>
    Y,
}

/// <summary>A model file as read: exactly one of <see cref="Mesh"/> and <see cref="Points"/> is
/// non-null. Coordinates are exactly as stored: no axis swap, scaling or recentring.</summary>
public sealed record ModelFile
{
    /// <summary>The format the file was read as.</summary>
    public required ModelFileFormat Format { get; init; }

    /// <summary>The triangle mesh, or null when the file is a point cloud.</summary>
    public CaveMesh? Mesh { get; init; }

    /// <summary>The point cloud, or null when the file is a mesh.</summary>
    public CavePointCloud? Points { get; init; }

    /// <summary>True when the file is a point cloud.</summary>
    public bool IsPointCloud => Points is not null;

    /// <summary>The axis the format takes as up: Y for glb (glTF is +Y up), Z otherwise.</summary>
    public ModelUpAxis UpAxisHint { get; init; } = ModelUpAxis.Z;

    /// <summary>True when the format fixes the unit (glb: metres); false for the others.</summary>
    public bool UnitsKnown { get; init; }

    /// <summary>What the reader left out or repaired ("ignored 12 line elements", ...).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
