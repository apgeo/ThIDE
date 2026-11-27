// STL reader, binary and ASCII. Streams the triangles and welds vertices that are equal, so
// triangles share vertices (lossless; the format stores every corner of every triangle). Stored
// normals are ignored; triangles that name one vertex twice are skipped and counted.

using System.Buffers.Binary;
using System.Globalization;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.CaveGeometry;

/// <summary>Reads STL files (binary and ASCII) into a <see cref="CaveMesh"/>.</summary>
public static class StlReader
{
    private const int HeaderBytes = 80;
    private const int BinaryTriangleBytes = 50;

    /// <summary>True when a file of <paramref name="length"/> bytes whose count field (bytes 80-83)
    /// is <paramref name="triangleCount"/> has exactly the size of a binary STL.</summary>
    public static bool IsBinarySize(long length, uint triangleCount)
        => length == HeaderBytes + 4 + BinaryTriangleBytes * (long)triangleCount;

    /// <summary>Reads an STL from the stream's current position to its end.</summary>
    public static ModelFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long start = stream.Position;
        long length = stream.Length - start;

        var head = new byte[HeaderBytes + 4];
        int got = 0;
        while (got < head.Length)
        {
            int n = stream.Read(head, got, head.Length - got);
            if (n <= 0) break;
            got += n;
        }

        bool binary;
        if (got == head.Length && IsBinarySize(length, BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(HeaderBytes))))
            binary = true;
        else if (StartsWithSolid(head, got))
            binary = false;
        else if (got == head.Length)
            binary = true; // sized wrongly: read as binary so the error says what is missing
        else
            throw new CaveFileFormatException($"STL: file is {length} bytes, too short for a binary STL header (84 bytes) and not an ASCII STL.");

        var warnings = new List<string>();
        var welder = new Welder();
        if (binary)
        {
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(HeaderBytes));
            ReadBinary(stream, start + head.Length, length - head.Length, count, welder, warnings);
        }
        else
        {
            stream.Position = start;
            ReadAscii(stream, welder, warnings);
        }

        if (welder.Skipped > 0)
            warnings.Add($"skipped {welder.Skipped} degenerate faces");
        return new ModelFile
        {
            Format = ModelFileFormat.Stl,
            Mesh = new CaveMesh { Vertices = welder.Vertices, Triangles = welder.Triangles },
            Warnings = warnings,
        };
    }

    private static bool StartsWithSolid(byte[] head, int got)
    {
        int i = 0;
        while (i < got && head[i] <= ' ') i++;
        return got - i >= 5 && head[i] == 's' && head[i + 1] == 'o' && head[i + 2] == 'l'
            && head[i + 3] == 'i' && head[i + 4] == 'd';
    }

    private static void ReadBinary(Stream stream, long bodyStart, long bodyLength, uint count, Welder welder, List<string> warnings)
    {
        long needed = BinaryTriangleBytes * (long)count;
        if (needed > bodyLength)
            throw new CaveFileFormatException(
                $"STL: truncated binary file: the header declares {count} triangles ({needed} bytes) but only {bodyLength} bytes follow it (about {bodyLength / BinaryTriangleBytes} triangles).");
        if (bodyLength > needed)
            warnings.Add($"ignored {bodyLength - needed} bytes after the last triangle");

        stream.Position = bodyStart;
        var reader = new ByteReader(stream);
        Span<byte> rec = stackalloc byte[BinaryTriangleBytes];
        for (uint i = 0; i < count; i++)
        {
            try { reader.ReadExactly(rec); }
            catch (EndOfStreamException)
            {
                throw new CaveFileFormatException($"STL: truncated at triangle {i} of {count}.");
            }
            // 12 bytes of normal, then three float32 vertices, then a 2-byte attribute.
            uint a = welder.Add(ReadVertex(rec[12..]));
            uint b = welder.Add(ReadVertex(rec[24..]));
            uint c = welder.Add(ReadVertex(rec[36..]));
            welder.AddTriangle(a, b, c);
        }
    }

    private static CaveVector3 ReadVertex(ReadOnlySpan<byte> s) => new(
        BinaryPrimitives.ReadSingleLittleEndian(s),
        BinaryPrimitives.ReadSingleLittleEndian(s[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(s[8..]));

    private static void ReadAscii(Stream stream, Welder welder, List<string> warnings)
    {
        var reader = new ByteReader(stream);
        var pending = new uint[3];
        int pendingCount = 0;
        int lineNo = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            var s = line.AsSpan().Trim();
            if (!s.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = s[6..].ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                throw new CaveFileFormatException($"STL: line {lineNo}: malformed vertex.");
            pending[pendingCount++] = welder.Add(new CaveVector3(x, y, z));
            if (pendingCount == 3)
            {
                welder.AddTriangle(pending[0], pending[1], pending[2]);
                pendingCount = 0;
            }
        }
        if (pendingCount != 0)
            throw new CaveFileFormatException($"STL: truncated: the file ends after {pendingCount} vertices of a facet (line {lineNo}).");
        if (welder.Triangles.Count == 0 && welder.Skipped == 0)
            throw new CaveFileFormatException("STL: no facets found (a truncated binary STL that starts with \"solid\" looks like this).");
    }

    // Shares equal vertices between triangles.
    private sealed class Welder
    {
        private readonly Dictionary<(double, double, double), uint> _index = new();

        public List<CaveVector3> Vertices { get; } = new();
        public List<CaveTriangle> Triangles { get; } = new();
        public int Skipped { get; private set; }

        public uint Add(CaveVector3 v)
        {
            var key = (v.X, v.Y, v.Z);
            if (_index.TryGetValue(key, out uint existing)) return existing;
            uint id = (uint)Vertices.Count;
            Vertices.Add(v);
            _index.Add(key, id);
            return id;
        }

        public void AddTriangle(uint a, uint b, uint c)
        {
            if (a == b || b == c || a == c) { Skipped++; return; }
            Triangles.Add(new CaveTriangle(a, b, c));
        }
    }
}
