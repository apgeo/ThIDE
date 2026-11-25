// An invented terrain surface for an invented cave: a height grid over the cave and some
// margin, a gentle swell a few metres above its highest station, and a tiny image in four
// coloured quadrants draped over the whole grid, so which way up the image lands can be read
// back. Nothing here is surveyed data. Also the minimal PNG encoder and decoder the terrain
// tests need (8-bit RGB/RGBA, no interlacing).

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Therion.Cave3D;

namespace Therion.Blender.Tests.SceneProbe;

internal static class InventedTerrain
{
    public const int ImageWidth = 8;
    public const int ImageHeight = 4;

    // The image's quadrants, as they are seen on a map with north up.
    public static readonly (byte R, byte G, byte B) SouthWest = (255, 0, 0);
    public static readonly (byte R, byte G, byte B) SouthEast = (0, 255, 0);
    public static readonly (byte R, byte G, byte B) NorthWest = (0, 0, 255);
    public static readonly (byte R, byte G, byte B) NorthEast = (255, 255, 255);

    /// <summary><paramref name="model"/> with a terrain grid over it (and the quadrant image when
    /// <paramref name="withImage"/>). The grid runs <paramref name="margin"/> metres past the
    /// stations on every side, <paramref name="spacing"/> metres between points, and lies
    /// <paramref name="clearance"/> metres above the highest station.</summary>
    public static CaveModel Add(CaveModel model, bool withImage = true, double spacing = 2.0, double margin = 60.0, double clearance = 8.0)
    {
        double minX = model.Stations.Min(s => s.Position.X) - margin, maxX = model.Stations.Max(s => s.Position.X) + margin;
        double minY = model.Stations.Min(s => s.Position.Y) - margin, maxY = model.Stations.Max(s => s.Position.Y) + margin;
        double top = model.Stations.Max(s => s.Position.Z) + clearance;
        int width = (int)Math.Ceiling((maxX - minX) / spacing) + 1;
        int height = (int)Math.Ceiling((maxY - minY) / spacing) + 1;

        var heights = new double[width * height];
        for (int j = 0; j < height; j++)
            for (int i = 0; i < width; i++)
                heights[j * width + i] = Math.Round(top + 2.0 + 2.0 * Math.Sin(i * 0.15) * Math.Cos(j * 0.1), 3);

        var grid = new CaveSurfaceGrid
        {
            Id = 0,
            Width = (uint)width,
            Height = (uint)height,
            Calibration = [minX, minY, spacing, 0.0, 0.0, spacing],
            Heights = heights,
        };
        double spanX = (width - 1) * spacing, spanY = (height - 1) * spacing;
        var bitmaps = withImage
            ? new[]
            {
                new CaveSurfaceBitmap
                {
                    SurfaceId = 0,
                    Type = CaveBitmapType.Png,
                    // Pixel (0, 0) is the bottom-left corner of the image, as Therion counts it.
                    Calibration = [minX, minY, spanX / ImageWidth, 0.0, 0.0, spanY / ImageHeight],
                    Data = QuadrantPng(),
                },
            }
            : [];

        return new CaveModel
        {
            SourcePath = model.SourcePath,
            SourceFormat = model.SourceFormat,
            FormatVersion = model.FormatVersion,
            Title = model.Title,
            CoordinateSystem = model.CoordinateSystem,
            SeparatorChar = model.SeparatorChar,
            Datestamp = model.Datestamp,
            Timestamp = model.Timestamp,
            IsExtendedElevation = model.IsExtendedElevation,
            Surveys = model.Surveys,
            Stations = model.Stations,
            Shots = model.Shots,
            Scraps = model.Scraps,
            Surfaces = [grid],
            SurfaceBitmaps = bitmaps,
            Passages = model.Passages,
            TraverseErrors = model.TraverseErrors,
        };
    }

    /// <summary>The 8 × 4 image: south (bottom) half red | green, north (top) half blue | white.</summary>
    public static byte[] QuadrantPng()
    {
        var rgb = new byte[ImageWidth * ImageHeight * 3];
        for (int row = 0; row < ImageHeight; row++) // PNG rows run top to bottom
        {
            bool north = row < ImageHeight / 2;
            for (int x = 0; x < ImageWidth; x++)
            {
                bool east = x >= ImageWidth / 2;
                var c = north ? (east ? NorthEast : NorthWest) : (east ? SouthEast : SouthWest);
                int at = (row * ImageWidth + x) * 3;
                (rgb[at], rgb[at + 1], rgb[at + 2]) = c;
            }
        }
        return Png.EncodeRgb(ImageWidth, ImageHeight, rgb);
    }
}

internal static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] EncodeRgb(int width, int height, byte[] rgb) => Encode(width, height, rgb, channels: 3);

    /// <summary>An 8-bit PNG with transparency, from RGBA rows, top row first.</summary>
    public static byte[] EncodeRgba(int width, int height, byte[] rgba) => Encode(width, height, rgba, channels: 4);

    private static byte[] Encode(int width, int height, byte[] rgb, int channels)
    {
        using var raw = new MemoryStream();
        for (int row = 0; row < height; row++)
        {
            raw.WriteByte(0); // filter: none
            raw.Write(rgb, row * width * channels, width * channels);
        }
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.ToArray());

        using var png = new MemoryStream();
        png.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = (byte)(channels == 4 ? 6 : 2); // colour type: RGB, or RGB with alpha
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", packed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Decodes an 8-bit RGB or RGBA, non-interlaced PNG to RGBA rows, top row first.</summary>
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] png)
    {
        if (!png.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("not a PNG");
        int width = 0, height = 0, channels = 0;
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 8 || data[12] != 0) throw new InvalidDataException("only 8-bit, non-interlaced PNGs");
                channels = data[9] switch { 2 => 3, 6 => 4, _ => throw new InvalidDataException("only RGB/RGBA PNGs") };
            }
            else if (type == "IDAT") idat.Write(data);
            else if (type == "IEND") break;
            at += 12 + length;
        }

        using var inflated = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
            z.CopyTo(inflated);
        var raw = inflated.ToArray();
        int stride = width * channels;
        var rows = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? rows[y * stride + x - channels] : 0;
                int b = y > 0 ? rows[(y - 1) * stride + x] : 0;
                int c = x >= channels && y > 0 ? rows[(y - 1) * stride + x - channels] : 0;
                int value = raw[y * (stride + 1) + 1 + x];
                value += filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("bad PNG filter"),
                };
                rows[y * stride + x] = (byte)value;
            }
        }

        var rgba = new byte[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            rgba[p * 4] = rows[p * channels];
            rgba[p * 4 + 1] = rows[p * channels + 1];
            rgba[p * 4 + 2] = rows[p * channels + 2];
            rgba[p * 4 + 3] = channels == 4 ? rows[p * channels + 3] : (byte)255;
        }
        return (width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        png.Write(four);
        var typed = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(four, Crc32(typed));
        png.Write(four);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}
