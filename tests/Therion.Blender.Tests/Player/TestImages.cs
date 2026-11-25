// Invented images for the tests of photos: a flag whose parts tell left from right and top from
// bottom, so a picture that is mirrored or upside down is seen at once. And for the tests of a
// world's image: Radiance HDR and OpenEXR pictures made here, byte by byte.

using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests.Player;

internal static class TestImages
{
    /// <summary>A PNG of the flag: green across its top quarter, and below that red on the left
    /// and blue on the right.</summary>
    public static byte[] FlagPng(int width = 64, int height = 32)
    {
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int channel = y < height / 4 ? 1 : x < width / 2 ? 0 : 2;
                rgb[(y * width + x) * 3 + channel] = 255;
            }
        return Png.EncodeRgb(width, height, rgb);
    }

    /// <summary>A PNG of one colour.</summary>
    public static byte[] PlainPng(int width, int height, byte r, byte g, byte b)
    {
        var rgb = new byte[width * height * 3];
        for (int p = 0; p < width * height; p++)
        {
            rgb[p * 3] = r;
            rgb[p * 3 + 1] = g;
            rgb[p * 3 + 2] = b;
        }
        return Png.EncodeRgb(width, height, rgb);
    }

    /// <summary>The same flag as a JPEG, 32 by 16 pixels (made once with an image library and
    /// kept here as its bytes: .NET has no JPEG writer of its own).</summary>
    public static byte[] FlagJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/" +
        "2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQACADAREAAhEBAxEB/8QA" +
        "FgABAQEAAAAAAAAAAAAAAAAAAAYH/8QAGhAAAgIDAAAAAAAAAAAAAAAAABhl42akpf/EABcBAQEBAQAAAAAAAAAAAAAAAAAICQX/xAAhEQABAQcFAAAAAAAA" +
        "AAAAAAAAGBYXY2RlpORmoqPh4v/aAAwDAQACEQMRAD8Al2GgN2slZmo23s7aK9Q2uQGGgN2sM1G29hFeobXIIB28M6tJcSQK9bZBHLsZzj9h28M6tISBXrbI" +
        "DsZzj9k2Smb/AAAMCNxjHQAH/9k=");

    /// <summary>
    /// A Radiance HDR picture (<c>.hdr</c>), made here: a head of text and the pixels as four
    /// bytes each (three mantissas and one exponent), row by row from the top, not run-length
    /// packed. A world's image lies round the scene by longitude and latitude:
    /// <paramref name="colour"/> gets u, from 0 at the left edge to 1 at the right, and v, from
    /// 0 at the bottom to 1 at the top, and gives linear red, green and blue.
    /// </summary>
    public static byte[] RadianceHdr(int width, int height, Func<double, double, (double R, double G, double B)> colour)
    {
        byte[] head = System.Text.Encoding.ASCII.GetBytes($"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n");
        var file = new byte[head.Length + width * height * 4];
        head.CopyTo(file, 0);
        int at = head.Length;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++, at += 4)
            {
                var (r, g, b) = colour((x + 0.5) / width, 1 - (y + 0.5) / height);
                double strongest = Math.Max(r, Math.Max(g, b));
                if (strongest < 1e-32) continue;
                // strongest = mantissa x 2^exponent, with the mantissa from a half up to one.
                int exponent = (int)Math.Floor(Math.Log2(strongest)) + 1;
                double scale = 256.0 / Math.Pow(2, exponent);
                file[at] = (byte)Math.Min(255, r * scale);
                file[at + 1] = (byte)Math.Min(255, g * scale);
                file[at + 2] = (byte)Math.Min(255, b * scale);
                file[at + 3] = (byte)(exponent + 128);
            }
        return file;
    }

    /// <summary>
    /// An OpenEXR picture (<c>.exr</c>), made here: one part of scan lines, not compressed, with
    /// the channels B, G and R as 32-bit floats. <paramref name="colour"/> as for
    /// <see cref="RadianceHdr"/>.
    /// </summary>
    public static byte[] OpenExr(int width, int height, Func<double, double, (double R, double G, double B)> colour)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        void Name(string name)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
            writer.Write((byte)0);
        }
        void Attribute(string name, string type, int size)
        {
            Name(name);
            Name(type);
            writer.Write(size);
        }
        void Box()
        {
            writer.Write(0);
            writer.Write(0);
            writer.Write(width - 1);
            writer.Write(height - 1);
        }

        writer.Write([(byte)0x76, (byte)0x2F, (byte)0x31, (byte)0x01]);
        writer.Write(2);
        Attribute("channels", "chlist", 3 * 18 + 1);
        foreach (string channel in new[] { "B", "G", "R" })
        {
            Name(channel);
            writer.Write(2);   // 32-bit float
            writer.Write(0);   // not linear, and three bytes kept free
            writer.Write(1);   // every pixel across
            writer.Write(1);   // and down
        }
        writer.Write((byte)0);
        Attribute("compression", "compression", 1);
        writer.Write((byte)0);
        Attribute("dataWindow", "box2i", 16);
        Box();
        Attribute("displayWindow", "box2i", 16);
        Box();
        Attribute("lineOrder", "lineOrder", 1);
        writer.Write((byte)0);
        Attribute("pixelAspectRatio", "float", 4);
        writer.Write(1f);
        Attribute("screenWindowCenter", "v2f", 8);
        writer.Write(0f);
        writer.Write(0f);
        Attribute("screenWindowWidth", "float", 4);
        writer.Write(1f);
        writer.Write((byte)0);

        // Where each scan line starts, then the lines: the row, the length, and channel by channel.
        long lines = stream.Position + 8L * height;
        int lineBytes = 8 + width * 12;
        for (int y = 0; y < height; y++) writer.Write(lines + (long)y * lineBytes);
        for (int y = 0; y < height; y++)
        {
            writer.Write(y);
            writer.Write(width * 12);
            var row = Enumerable.Range(0, width).Select(x => colour((x + 0.5) / width, 1 - (y + 0.5) / height)).ToList();
            foreach (var pixel in row) writer.Write((float)pixel.B);
            foreach (var pixel in row) writer.Write((float)pixel.G);
            foreach (var pixel in row) writer.Write((float)pixel.R);
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>A world's image whose four quarters, by the compass, are of four colours. An
    /// image that is not turned has its left edge in the west and runs, as someone turning to
    /// the right sees it, by the north and the east (its middle) and the south back to the west:
    /// red from west to north, green from north to east, blue from east to south and yellow
    /// from south to west.</summary>
    public static (double R, double G, double B) Compass(double u, double v)
        => u < 0.25 ? (1, 0, 0) : u < 0.5 ? (0, 1, 0) : u < 0.75 ? (0, 0, 1) : (1, 1, 0);

    /// <summary>A world's image that is white from east round by south to west (its right half)
    /// and black round the north: a light on one side.</summary>
    public static (double R, double G, double B) LitFromTheSouth(double u, double v) => u >= 0.5 ? (1, 1, 1) : (0, 0, 0);

    /// <summary>Writes an image into a folder and returns its path.</summary>
    public static string Write(string folder, string name, byte[] image)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, image);
        return path;
    }
}
