// The check an image passes before it travels with a plan as the world's image (an HDRI): that it
// is a Radiance HDR or an OpenEXR picture by what it holds (not by its name), that its size can be
// read, and that it is not larger than a render can sensibly hold. Only the head of the file is
// read; nothing is decoded.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Therion.Blender.Plan;

/// <summary>What the file of a world's image is.</summary>
public enum PlanEnvironmentFormat
{
    /// <summary>Radiance RGBE (<c>.hdr</c>).</summary>
    Hdr,
    /// <summary>OpenEXR (<c>.exr</c>).</summary>
    Exr,
}

/// <summary>What the head of a world's image says of it.</summary>
/// <param name="Width">In pixels.</param>
/// <param name="Height">In pixels.</param>
/// <param name="Bytes">The length of the file.</param>
public sealed record PlanEnvironmentInfo(PlanEnvironmentFormat Format, int Width, int Height, long Bytes);

/// <summary>Checks the image a plan carries as its world (<see cref="PlanWorld.Image"/>).</summary>
public static class PlanEnvironmentImage
{
    /// <summary>The largest file: 256 MiB. An image of 8192 x 4096 pixels is about 100 MiB as
    /// a Radiance file.</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    /// <summary>The longest side, in pixels.</summary>
    public const int MaxSide = 16_384;

    /// <summary>The most pixels: 16384 x 8192. Blender holds sixteen bytes for each.</summary>
    public const long MaxPixels = 16_384L * 8_192;

    /// <summary>How much of a file is read to find where its head ends.</summary>
    private const int MaxHeadBytes = 1024 * 1024;

    private static readonly byte[] ExrSignature = [0x76, 0x2F, 0x31, 0x01];

    /// <summary>True when <paramref name="path"/> has the name of a file a world's image can be:
    /// <c>.hdr</c> or <c>.exr</c>.</summary>
    public static bool HasExtension(string? path)
        => path is not null
           && (path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exr", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the head of an image file and checks it.</summary>
    /// <exception cref="PlanImageException">The file is not a Radiance HDR or an OpenEXR
    /// picture, its size cannot be read, or it is too large; the message says which, without the
    /// file's name.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static PlanEnvironmentInfo Check(string path)
    {
        using var stream = File.OpenRead(path);
        return Check(stream);
    }

    /// <summary>Reads the head of an image from where <paramref name="stream"/> stands and
    /// checks it. A stream that cannot say how long it is is read to its end, and not past the
    /// largest file.</summary>
    /// <exception cref="PlanImageException">As for <see cref="Check(string)"/>.</exception>
    public static PlanEnvironmentInfo Check(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long start = stream.CanSeek ? stream.Position : 0;
        var head = new Head(stream);
        var (format, width, height) = ReadHead(head);
        long bytes;
        if (stream.CanSeek) bytes = stream.Length - start;
        else
        {
            var rest = new byte[81920];
            int read;
            while (head.Count <= MaxBytes && (read = stream.Read(rest, 0, rest.Length)) > 0) head.Count += read;
            bytes = head.Count;
        }

        if (bytes > MaxBytes)
            throw new PlanImageException($"the image file is larger than {MaxBytes / (1024 * 1024)} MiB");
        if (width < 1 || height < 1)
            throw new PlanImageException("the image has no size");
        if (width > MaxSide || height > MaxSide)
            throw new PlanImageException($"the image is {width} x {height} pixels; a side is at most {MaxSide}");
        if ((long)width * height > MaxPixels)
            throw new PlanImageException($"the image is {width} x {height} pixels; a world's image has at most {MaxPixels / 1_000_000} million");
        return new PlanEnvironmentInfo(format, width, height, bytes);
    }

    private static (PlanEnvironmentFormat Format, int Width, int Height) ReadHead(Head head)
    {
        var first = new byte[4];
        int got = head.Fill(first);
        if (got == 4 && first.AsSpan().SequenceEqual(ExrSignature)) return ReadExr(head);
        if (got >= 2 && first[0] == '#' && first[1] == '?') return ReadRadiance(head, first.AsSpan(0, got).ToArray());
        throw new PlanImageException("the file is neither a Radiance HDR nor an OpenEXR image");
    }

    /// <summary>A Radiance picture: lines of text up to an empty one, then the line with the
    /// picture's size, as <c>-Y 512 +X 1024</c>.</summary>
    private static (PlanEnvironmentFormat, int, int) ReadRadiance(Head head, byte[] first)
    {
        // A file without a FORMAT line is taken as RGBE, as Radiance's own tools take it.
        string? line = head.Line(first);
        while (line is not null && line.Length > 0)
        {
            if (line.StartsWith("FORMAT=", StringComparison.Ordinal)
                && line["FORMAT=".Length..].Trim() is not ("32-bit_rle_rgbe" or "32-bit_rle_xyze"))
                throw new PlanImageException("the Radiance file is of a format that is not a picture's");
            line = head.Line();
        }
        string? size = line is null ? null : head.Line();
        if (size is null) throw new PlanImageException("the Radiance file has no picture size");

        string[] parts = size.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int width = 0, height = 0;
        bool read = parts.Length == 4;
        for (int i = 0; read && i < 4; i += 2)
        {
            if (parts[i].Length != 2 || parts[i][0] is not ('+' or '-')
                || !int.TryParse(parts[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int count))
                read = false;
            else if (parts[i][1] == 'X') width = count;
            else if (parts[i][1] == 'Y') height = count;
            else read = false;
        }
        if (!read || width == 0 || height == 0) throw new PlanImageException("the Radiance file's picture size cannot be read");
        return (PlanEnvironmentFormat.Hdr, width, height);
    }

    /// <summary>An OpenEXR picture: a version, then the attributes of its head, each a name, a
    /// type, a length and a value; <c>dataWindow</c> is the box of the pixels it holds.</summary>
    private static (PlanEnvironmentFormat, int, int) ReadExr(Head head)
    {
        var word = new byte[4];
        if (head.Fill(word) < 4) throw new PlanImageException("the OpenEXR file has no head");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(word);
        if ((version & 0xFF) != 2) throw new PlanImageException("the OpenEXR file is of a version that cannot be read");
        if ((version & 0x800) != 0) throw new PlanImageException("the OpenEXR file holds deep data, not a picture");

        while (true)
        {
            string? name = head.Name();
            if (name is null) throw new PlanImageException("the OpenEXR file's head is damaged");
            if (name.Length == 0) break; // the end of the head
            string? type = head.Name();
            if (type is null || head.Fill(word) < 4) throw new PlanImageException("the OpenEXR file's head is damaged");
            int length = BinaryPrimitives.ReadInt32LittleEndian(word);
            if (length < 0) throw new PlanImageException("the OpenEXR file's head is damaged");
            if (name == "dataWindow" && type == "box2i" && length == 16)
            {
                var box = new byte[16];
                if (head.Fill(box) < 16) throw new PlanImageException("the OpenEXR file's head is damaged");
                long xMin = BinaryPrimitives.ReadInt32LittleEndian(box), yMin = BinaryPrimitives.ReadInt32LittleEndian(box.AsSpan(4));
                long xMax = BinaryPrimitives.ReadInt32LittleEndian(box.AsSpan(8)), yMax = BinaryPrimitives.ReadInt32LittleEndian(box.AsSpan(12));
                long width = xMax - xMin + 1, height = yMax - yMin + 1;
                if (width < 1 || height < 1 || width > int.MaxValue || height > int.MaxValue)
                    throw new PlanImageException("the OpenEXR file's picture size cannot be read");
                return (PlanEnvironmentFormat.Exr, (int)width, (int)height);
            }
            if (!head.Skip(length)) throw new PlanImageException("the OpenEXR file's head is damaged");
        }
        throw new PlanImageException("the OpenEXR file has no picture size");
    }

    /// <summary>Reads the head of a file, and not more of it than a head can be.</summary>
    private sealed class Head(Stream stream)
    {
        private readonly byte[] _one = new byte[1];

        /// <summary>The bytes read so far.</summary>
        public long Count { get; set; }

        private int Next()
        {
            if (Count >= MaxHeadBytes) throw new PlanImageException("the image file's head does not end");
            if (stream.Read(_one, 0, 1) != 1) return -1;
            Count++;
            return _one[0];
        }

        public int Fill(byte[] buffer)
        {
            int got = 0;
            while (got < buffer.Length)
            {
                int value = Next();
                if (value < 0) break;
                buffer[got++] = (byte)value;
            }
            return got;
        }

        public bool Skip(int count)
        {
            if (Count + count > MaxHeadBytes) throw new PlanImageException("the image file's head does not end");
            var scratch = new byte[Math.Min(Math.Max(count, 1), 8192)];
            while (count > 0)
            {
                int read = stream.Read(scratch, 0, Math.Min(count, scratch.Length));
                if (read <= 0) return false;
                Count += read;
                count -= read;
            }
            return true;
        }

        /// <summary>A line of text up to its line feed, after <paramref name="first"/>, the
        /// bytes of it that were read already; null at the end of the file.</summary>
        public string? Line(byte[]? first = null)
        {
            var line = new StringBuilder();
            foreach (byte b in first ?? []) line.Append((char)b);
            if (first is not null && first.Contains((byte)'\n'))
                throw new PlanImageException("the Radiance file's head is damaged");
            while (true)
            {
                int value = Next();
                if (value < 0) return line.Length > 0 || first is not null ? line.ToString() : null;
                if (value == '\n') return line.ToString().TrimEnd('\r');
                line.Append((char)value);
            }
        }

        /// <summary>A name that ends in a zero byte; null when the file ends before it does.</summary>
        public string? Name()
        {
            var name = new StringBuilder();
            while (true)
            {
                int value = Next();
                if (value < 0) return null;
                if (value == 0) return name.ToString();
                if (name.Length >= 255) throw new PlanImageException("the OpenEXR file's head is damaged");
                name.Append((char)value);
            }
        }
    }
}
