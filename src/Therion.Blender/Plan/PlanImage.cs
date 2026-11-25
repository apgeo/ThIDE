// The check an image passes before it travels with a plan as a photo: that it is a PNG or a JPEG
// by what it holds (not by its name), that its size can be read, and that it is not larger than a
// render can sensibly use. Only the head of the file is read; nothing is decoded.

using System.Buffers.Binary;

namespace Therion.Blender.Plan;

/// <summary>What an image file is.</summary>
public enum PlanImageFormat
{
    Png,
    Jpeg,
}

/// <summary>What the head of an image file says of it.</summary>
/// <param name="Width">In pixels.</param>
/// <param name="Height">In pixels.</param>
/// <param name="Bytes">The length of the file.</param>
public sealed record PlanImageInfo(PlanImageFormat Format, int Width, int Height, long Bytes);

/// <summary>Thrown when a file is not an image a plan can carry.</summary>
public sealed class PlanImageException(string message) : Exception(message);

/// <summary>Checks the images a plan carries as photos.</summary>
public static class PlanImage
{
    /// <summary>The largest image file: 32 MiB.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>The longest side, in pixels.</summary>
    public const int MaxSide = 16_384;

    /// <summary>The most pixels: 64 million. Blender holds four bytes for each.</summary>
    public const long MaxPixels = 64_000_000;

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the head of an image file and checks it.</summary>
    /// <exception cref="PlanImageException">The file is not a PNG or a JPEG, its size cannot be
    /// read, or it is too large; the message says which, without the file's name.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static PlanImageInfo Check(string path)
    {
        using var stream = File.OpenRead(path);
        return Check(stream);
    }

    /// <summary>Reads the head of an image from where <paramref name="stream"/> stands and
    /// checks it. A stream that cannot say how long it is is read to its end.</summary>
    /// <exception cref="PlanImageException">As for <see cref="Check(string)"/>.</exception>
    public static PlanImageInfo Check(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long start = stream.CanSeek ? stream.Position : 0;
        var counted = new CountingStream(stream);
        var (format, width, height) = ReadHead(counted);
        long bytes;
        if (stream.CanSeek) bytes = stream.Length - start;
        else
        {
            // Not past the cap: an endless stream is not read endlessly.
            var rest = new byte[81920];
            int read;
            while (counted.Count <= MaxBytes && (read = counted.Read(rest, 0, rest.Length)) > 0) { }
            bytes = counted.Count;
        }

        if (bytes > MaxBytes)
            throw new PlanImageException($"the image file is larger than {MaxBytes / (1024 * 1024)} MiB");
        if (width < 1 || height < 1)
            throw new PlanImageException("the image has no size");
        if (width > MaxSide || height > MaxSide)
            throw new PlanImageException($"the image is {width} x {height} pixels; a side is at most {MaxSide}");
        if ((long)width * height > MaxPixels)
            throw new PlanImageException($"the image is {width} x {height} pixels; an image has at most {MaxPixels / 1_000_000} million");
        return new PlanImageInfo(format, width, height, bytes);
    }

    private static (PlanImageFormat Format, int Width, int Height) ReadHead(Stream stream)
    {
        var head = new byte[8];
        int got = Fill(stream, head);
        if (got >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return ReadJpeg(stream, head.AsSpan(2, got - 2).ToArray());
        if (got == 8 && head.AsSpan().SequenceEqual(PngSignature))
        {
            // The first chunk is the header: its length (13), "IHDR", width, height.
            var header = new byte[16];
            if (Fill(stream, header) < 16 || BinaryPrimitives.ReadUInt32BigEndian(header) != 13
                || header[4] != 'I' || header[5] != 'H' || header[6] != 'D' || header[7] != 'R')
                throw new PlanImageException("the PNG file has no header");
            uint width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8)), height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
            if (width > int.MaxValue || height > int.MaxValue)
                throw new PlanImageException("the PNG file's size cannot be read");
            return (PlanImageFormat.Png, (int)width, (int)height);
        }
        throw new PlanImageException("the file is neither a PNG nor a JPEG image");
    }

    /// <summary>Walks the segments of a JPEG up to the one that has the picture's size (a start
    /// of frame). <paramref name="pending"/> are the bytes already read after the file's first
    /// two.</summary>
    private static (PlanImageFormat, int, int) ReadJpeg(Stream stream, byte[] pending)
    {
        var reader = new Pending(stream, pending);
        while (true)
        {
            int marker = reader.Next();
            if (marker != 0xFF) throw new PlanImageException("the JPEG file is damaged before its picture");
            do marker = reader.Next(); while (marker == 0xFF);  // fill bytes
            if (marker < 0) break;
            // Markers that stand alone; a picture's size comes before its scan and its end.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue;
            if (marker is 0xD9 or 0xDA) break;
            int high = reader.Next(), low = reader.Next();
            if (low < 0) break;
            int length = (high << 8) | low;
            if (length < 2) throw new PlanImageException("the JPEG file is damaged before its picture");
            bool frame = marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
            if (!frame)
            {
                if (!reader.Skip(length - 2)) break;
                continue;
            }
            // Precision, then the height and the width.
            var size = new byte[5];
            if (length < 7 || !reader.Fill(size)) break;
            return (PlanImageFormat.Jpeg, (size[3] << 8) | size[4], (size[1] << 8) | size[2]);
        }
        throw new PlanImageException("the JPEG file has no picture size");
    }

    private static int Fill(Stream stream, byte[] buffer)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int read = stream.Read(buffer, got, buffer.Length - got);
            if (read <= 0) break;
            got += read;
        }
        return got;
    }

    /// <summary>Reads a stream after some bytes that were read from it already.</summary>
    private sealed class Pending(Stream stream, byte[] first)
    {
        private int _at;
        private readonly byte[] _one = new byte[1];

        public int Next()
        {
            if (_at < first.Length) return first[_at++];
            return stream.Read(_one, 0, 1) == 1 ? _one[0] : -1;
        }

        public bool Fill(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                int value = Next();
                if (value < 0) return false;
                buffer[i] = (byte)value;
            }
            return true;
        }

        public bool Skip(int count)
        {
            while (count > 0 && _at < first.Length)
            {
                _at++;
                count--;
            }
            var scratch = new byte[Math.Min(Math.Max(count, 1), 8192)];
            while (count > 0)
            {
                int read = stream.Read(scratch, 0, Math.Min(count, scratch.Length));
                if (read <= 0) return false;
                count -= read;
            }
            return true;
        }
    }

    /// <summary>Counts what is read through it.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Count { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            if (read > 0) Count += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
