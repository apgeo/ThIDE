// read raster image pixel dimensions from the file header only (no full decode), so the
// media manager can show a resolution for orphan scans cheaply. Supports PNG / GIF / BMP / JPEG.

using System;
using System.IO;

namespace ThIDE.Services;

public static class ImageDimensions
{
    public static (int Width, int Height)? TryGet(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> h = stackalloc byte[32];
            int n = fs.Read(h);
            if (n < 24) return null;

            // PNG: 89 50 4E 47 ... IHDR width@16, height@20 (big-endian).
            if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47)
                return (BE32(h, 16), BE32(h, 20));

            // GIF: "GIF8" then width@6, height@8 (little-endian uint16).
            if (h[0] == (byte)'G' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'8')
                return (LE16(h, 6), LE16(h, 8));

            // BMP: "BM" then width@18, height@22 (little-endian int32).
            if (h[0] == (byte)'B' && h[1] == (byte)'M')
                return (LE32(h, 18), Math.Abs(LE32(h, 22)));

            // JPEG: scan SOF markers for height/width (big-endian).
            if (h[0] == 0xFF && h[1] == 0xD8)
                return JpegSize(fs);

            return null;
        }
        catch { return null; }
    }

    private static (int, int)? JpegSize(FileStream fs)
    {
        fs.Position = 2;
        Span<byte> seg = stackalloc byte[9];
        while (true)
        {
            int b = fs.ReadByte();
            if (b != 0xFF) { if (b < 0) return null; continue; }
            int marker = fs.ReadByte();
            if (marker < 0) return null;
            // Standalone markers (no length): RSTn, SOI, EOI, TEM.
            if (marker is 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7) or 0x01) continue;

            int len = (fs.ReadByte() << 8) | fs.ReadByte();
            if (len < 2) return null;
            bool isSof = marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7)
                                or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF);
            if (isSof)
            {
                if (fs.Read(seg[..5]) < 5) return null;
                int height = (seg[1] << 8) | seg[2];
                int width = (seg[3] << 8) | seg[4];
                return (width, height);
            }
            fs.Position += len - 2;   // skip this segment's payload
        }
    }

    private static int BE32(ReadOnlySpan<byte> b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
    private static int LE32(ReadOnlySpan<byte> b, int i) => b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24);
    private static int LE16(ReadOnlySpan<byte> b, int i) => b[i] | (b[i + 1] << 8);
}
