// Terrain mesher: a .lox surface height grid → a triangle mesh in the model's local
// (recentered) frame, with texture coordinates from the surface image's calibration when
// there is one. The geometry stage passes surfaces through in world coordinates; this applies
// the same offset the walls got, so terrain and cave line up.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>A meshed terrain surface.</summary>
public sealed record TerrainMesh
{
    /// <summary>The terrain triangles in local coordinates (world = local + offset), with texture
    /// coordinates when an image was given.</summary>
    public required CaveMesh Mesh { get; init; }

    /// <summary>Grid step between two kept points along each axis: 1 keeps every point, 2 every
    /// second one, and so on (see <see cref="TerrainMesher.DefaultMaxVertices"/>).</summary>
    public required int Stride { get; init; }
}

/// <summary>A surface image whose format and pixel size were read from its header, ready to be
/// draped: the extension to save it under and the size its calibration is measured in.</summary>
public sealed record SurfaceImage
{
    public required CaveSurfaceBitmap Bitmap { get; init; }

    /// <summary>".png" or ".jpg", from the image's own signature (not the declared type).</summary>
    public required string Extension { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Reads the format and size of <paramref name="bitmap"/>; null when the bytes are
    /// neither a PNG nor a baseline/progressive JPEG with a readable size.</summary>
    public static SurfaceImage? TryRead(CaveSurfaceBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var data = bitmap.Data;
        if (TryPngSize(data, out int w, out int h))
            return new SurfaceImage { Bitmap = bitmap, Extension = ".png", Width = w, Height = h };
        if (TryJpegSize(data, out w, out h))
            return new SurfaceImage { Bitmap = bitmap, Extension = ".jpg", Width = w, Height = h };
        return null;
    }

    private static bool TryPngSize(byte[] d, out int width, out int height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (d.Length < 24 || !d.AsSpan(0, 8).SequenceEqual(signature)) return false;
        if (d[12] != 'I' || d[13] != 'H' || d[14] != 'D' || d[15] != 'R') return false;
        width = BigEndian32(d, 16);
        height = BigEndian32(d, 20);
        return width > 0 && height > 0;
    }

    private static bool TryJpegSize(byte[] d, out int width, out int height)
    {
        width = height = 0;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
        int at = 2;
        while (at + 3 < d.Length)
        {
            if (d[at] != 0xFF) return false;
            byte marker = d[at + 1];
            if (marker == 0xFF) { at++; continue; } // fill byte
            at += 2;
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue; // markers without a length
            if (marker is 0xD9 or 0xDA) return false; // end of image / start of scan, no frame header
            if (at + 1 >= d.Length) return false;
            int length = (d[at] << 8) | d[at + 1];
            bool frame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (frame)
            {
                if (at + 6 >= d.Length) return false;
                height = (d[at + 3] << 8) | d[at + 4];
                width = (d[at + 5] << 8) | d[at + 6];
                return width > 0 && height > 0;
            }
            if (length < 2) return false;
            at += length;
        }
        return false;
    }

    private static int BigEndian32(byte[] d, int at)
    {
        long v = ((long)d[at] << 24) | ((long)d[at + 1] << 16) | ((long)d[at + 2] << 8) | d[at + 3];
        return v > int.MaxValue ? 0 : (int)v;
    }
}

/// <summary>Turns a <see cref="CaveSurfaceGrid"/> into a <see cref="TerrainMesh"/>.</summary>
public static class TerrainMesher
{
    /// <summary>
    /// The most grid points a terrain keeps (512 × 512). A larger grid is thinned with one
    /// stride for both axes — the smallest that brings the kept points under this cap — and the
    /// last row and column are always kept, so the terrain covers the same ground. A terrain is
    /// backdrop to the cave: a quarter of a million points is a fine surface in any render, and
    /// keeps the transport file under ~10 MB.
    /// </summary>
    public const int DefaultMaxVertices = 512 * 512;

    /// <summary>
    /// Meshes <paramref name="grid"/>: grid point (i, j) becomes a vertex at its calibrated world
    /// position minus <paramref name="offset"/>, two triangles per grid cell, facing up. With
    /// <paramref name="image"/>, every vertex also gets the texture coordinate of its world X/Y
    /// in the image (the image calibration inverted, pixel origin at the image's bottom-left
    /// corner, as Therion and Loch use it). Points with a non-finite height are left out of the
    /// triangles. Null when the grid has fewer than 2 × 2 points, an unusable calibration, or no
    /// triangle survives.
    /// </summary>
    public static TerrainMesh? Build(
        CaveSurfaceGrid grid, CaveVector3 offset, SurfaceImage? image = null, int maxVertices = DefaultMaxVertices)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVertices, 4);
        int width = (int)Math.Min(grid.Width, int.MaxValue), height = (int)Math.Min(grid.Height, int.MaxValue);
        if (width < 2 || height < 2 || (long)width * height > grid.Heights.Count) return null;
        if (!TryAffine(grid.Calibration, out var g) || g.Det == 0) return null;
        Affine? uvMap = null;
        if (image is not null && TryAffine(image.Bitmap.Calibration, out var p) && p.Det != 0) uvMap = p;

        int stride = StrideFor(width, height, maxVertices);
        var columns = Samples(width, stride);
        var rows = Samples(height, stride);

        var vertices = new CaveVector3[columns.Length * rows.Length];
        var valid = new bool[vertices.Length];
        var uvs = uvMap is null ? null : new CaveUv[vertices.Length];
        int n = 0;
        foreach (int j in rows)
        {
            foreach (int i in columns)
            {
                double x = g.C0 + i * g.C2 + j * g.C3;
                double y = g.C1 + i * g.C4 + j * g.C5;
                double z = grid.Heights[j * width + i];
                valid[n] = double.IsFinite(z);
                vertices[n] = new CaveVector3(x - offset.X, y - offset.Y, (valid[n] ? z : offset.Z) - offset.Z);
                if (uvs is not null)
                {
                    var (px, py) = uvMap!.Value.Invert(x, y);
                    uvs[n] = new CaveUv(px / image!.Width, py / image.Height);
                }
                n++;
            }
        }

        // Counter-clockwise seen from above when the grid axes are right-handed (det > 0);
        // a mirrored calibration flips the winding so the faces still point up.
        bool upright = g.Det > 0;
        int cols = columns.Length;
        var triangles = new List<CaveTriangle>(2 * (cols - 1) * (rows.Length - 1));
        for (int r = 0; r + 1 < rows.Length; r++)
        {
            for (int c = 0; c + 1 < cols; c++)
            {
                uint v00 = (uint)(r * cols + c), v10 = v00 + 1, v01 = v00 + (uint)cols, v11 = v01 + 1;
                AddTriangle(triangles, valid, upright, v00, v10, v11);
                AddTriangle(triangles, valid, upright, v00, v11, v01);
            }
        }
        if (triangles.Count == 0) return null;

        return new TerrainMesh
        {
            Mesh = new CaveMesh { Vertices = vertices, Triangles = triangles, TextureCoordinates = uvs },
            Stride = stride,
        };
    }

    /// <summary>The smallest stride that keeps at most <paramref name="maxVertices"/> points of a
    /// <paramref name="width"/> × <paramref name="height"/> grid.</summary>
    public static int StrideFor(int width, int height, int maxVertices = DefaultMaxVertices)
    {
        int stride = Math.Max(1, (int)Math.Floor(Math.Sqrt((double)width * height / maxVertices)));
        while ((long)Count(width, stride) * Count(height, stride) > maxVertices) stride++;
        return stride;
    }

    private static int Count(int n, int stride) => (n - 2) / stride + 2;

    // 0, s, 2s, … below n − 1, then n − 1: the far edge is always kept.
    private static int[] Samples(int n, int stride)
    {
        var kept = new int[Count(n, stride)];
        for (int k = 0; k < kept.Length - 1; k++) kept[k] = k * stride;
        kept[^1] = n - 1;
        return kept;
    }

    private static void AddTriangle(List<CaveTriangle> into, bool[] valid, bool upright, uint a, uint b, uint c)
    {
        if (!valid[a] || !valid[b] || !valid[c]) return;
        into.Add(upright ? new CaveTriangle(a, b, c) : new CaveTriangle(a, c, b));
    }

    /// <summary>The 6 stored coefficients: X = C0 + i·C2 + j·C3, Y = C1 + i·C4 + j·C5.</summary>
    private readonly record struct Affine(double C0, double C1, double C2, double C3, double C4, double C5)
    {
        public double Det => C2 * C5 - C3 * C4;

        /// <summary>World X/Y → (i, j).</summary>
        public (double I, double J) Invert(double x, double y)
        {
            double dx = x - C0, dy = y - C1, det = Det;
            return ((C5 * dx - C3 * dy) / det, (C2 * dy - C4 * dx) / det);
        }
    }

    private static bool TryAffine(IReadOnlyList<double> c, out Affine affine)
    {
        affine = default;
        if (c is null || c.Count != 6) return false;
        for (int k = 0; k < 6; k++) if (!double.IsFinite(c[k])) return false;
        affine = new Affine(c[0], c[1], c[2], c[3], c[4], c[5]);
        return true;
    }
}
