namespace Therion.Blender;

/// <summary>Encoding of a surface texture bitmap embedded in a <c>.lox</c> file.</summary>
public enum CaveBitmapType
{
    Jpeg = 0,
    Png = 1,
}

/// <summary>
/// A terrain height grid from a <c>.lox</c> file. Grid point (i, j) with
/// i ∈ [0, Width), j ∈ [0, Height) maps to world coordinates via the affine
/// <see cref="Calibration"/>:
/// X = c[0] + i·c[2] + j·c[3], Y = c[1] + i·c[4] + j·c[5], Z = Heights[j·Width + i].
/// </summary>
public sealed record CaveSurfaceGrid
{
    /// <summary>Surface id (referenced by <see cref="CaveSurfaceBitmap.SurfaceId"/>).</summary>
    public required uint Id { get; init; }

    /// <summary>Grid points along the X axis.</summary>
    public required uint Width { get; init; }

    /// <summary>Grid points along the Y axis.</summary>
    public required uint Height { get; init; }

    /// <summary>The 6 affine calibration coefficients (origin + 2×2 step matrix) as
    /// stored: [originX, originY, xStepPerColumn, xStepPerRow, yStepPerColumn, yStepPerRow].</summary>
    public required IReadOnlyList<double> Calibration { get; init; }

    /// <summary>Row-major elevation values, Width·Height entries (metres).</summary>
    public required IReadOnlyList<double> Heights { get; init; }
}

/// <summary>
/// A georeferenced surface texture image embedded in a <c>.lox</c> file, draped over
/// the corresponding <see cref="CaveSurfaceGrid"/>. The 6 calibration coefficients map
/// pixel (i, j) to world X/Y the same way as the grid calibration.
/// </summary>
public sealed record CaveSurfaceBitmap
{
    /// <summary>Id of the surface grid this image belongs to.</summary>
    public required uint SurfaceId { get; init; }

    /// <summary>Image encoding of <see cref="Data"/>.</summary>
    public CaveBitmapType Type { get; init; }

    /// <summary>The 6 affine pixel→world calibration coefficients as stored.</summary>
    public required IReadOnlyList<double> Calibration { get; init; }

    /// <summary>The encoded image bytes exactly as embedded in the file.</summary>
    public required byte[] Data { get; init; }
}
