// What the reader of a bundle checks of its zip before .NET opens it, and of each entry as it is
// read. .NET reads the whole central directory into memory when it opens a zip (an object for every
// entry), however many entries the end of the zip says there are: a small zip of very many empty
// entries costs gigabytes before any limit can be held. So the entries are counted first, the way
// .NET finds them, with no more memory than a buffer. And .NET stops reading an entry at the size
// the central directory gives it, so a zip that says an entry is smaller than it is would be cut
// short without a word: the CRC-32 of what is read is held to the one the zip gives.

using System.Buffers.Binary;

namespace Therion.Blender.Player;

internal static class BundleZip
{
    private const uint EndSignature = 0x06054b50;
    private const uint Zip64EndSignature = 0x06064b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint CentralSignature = 0x02014b50;
    private const int EndLength = 22;
    private const int CentralLength = 46;

    /// <summary>
    /// The entries of the zip at <paramref name="path"/>, counted as .NET will read them: from
    /// where its end record (or its Zip64 end record) says the central directory starts, one
    /// header after the other, but no further than <paramref name="most"/> + 1.
    /// </summary>
    /// <exception cref="RenderBundleRefusedException">It has no end record: it is not a zip.</exception>
    public static long CountEntries(string path, int most)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        long length = stream.Length;
        if (length < EndLength) throw NotAZip();
        // The end record is the last one in the zip's last 64 KiB and 22 bytes (its comment may be that long).
        int tail = (int)Math.Min(length, EndLength + ushort.MaxValue);
        var end = new byte[tail];
        stream.Seek(length - tail, SeekOrigin.Begin);
        stream.ReadExactly(end);
        int at = -1;
        for (int i = tail - EndLength; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(i)) == EndSignature)
            {
                at = i;
                break;
            }
        if (at < 0) throw NotAZip();
        long declared = BinaryPrimitives.ReadUInt16LittleEndian(end.AsSpan(at + 10));
        long start = BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(at + 16));

        // A Zip64 locator just before the end record points at the Zip64 end record, whose numbers win.
        long endAt = length - tail + at;
        if (endAt >= 20)
        {
            var locator = new byte[20];
            stream.Seek(endAt - 20, SeekOrigin.Begin);
            stream.ReadExactly(locator);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) == Zip64LocatorSignature)
            {
                long zip64At = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8)), long.MaxValue);
                if (zip64At >= 0 && zip64At + 56 <= length)
                {
                    var zip64 = new byte[56];
                    stream.Seek(zip64At, SeekOrigin.Begin);
                    stream.ReadExactly(zip64);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) == Zip64EndSignature)
                    {
                        declared = Math.Max(declared, (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32)), long.MaxValue));
                        start = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(48)), long.MaxValue);
                    }
                }
            }
        }
        if (declared > most) return declared;

        // The headers themselves: .NET reads them while their signature holds.
        if (start < 0 || start > length) return declared;
        stream.Seek(start, SeekOrigin.Begin);
        var header = new byte[CentralLength];
        long count = 0;
        while (count <= most)
        {
            if (stream.ReadAtLeast(header, CentralLength, throwOnEndOfStream: false) < CentralLength) break;
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralSignature) break;
            count++;
            long skip = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28))
                        + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30))
                        + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
            stream.Seek(skip, SeekOrigin.Current);
        }
        return Math.Max(count, declared);
    }

    private static RenderBundleRefusedException NotAZip() => new("The bundle is not a zip file: it has no end of central directory.");

    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>The running CRC-32 (that of zip) of <paramref name="crc"/> and then <paramref name="data"/>;
    /// start from 0.</summary>
    public static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
