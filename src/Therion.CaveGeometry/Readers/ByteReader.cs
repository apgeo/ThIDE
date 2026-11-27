// A small buffered forward reader over a seekable stream, shared by the STL and PLY readers:
// bytes, exact-size reads, ASCII lines and whitespace-separated tokens, and how many bytes remain
// (so a count in a header can be checked against the data before anything is allocated).

using System.Text;
using Therion.Cave3D.Parsing;

namespace Therion.CaveGeometry;

internal sealed class ByteReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[1 << 16];
    private int _pos;
    private int _len;

    public ByteReader(Stream stream)
    {
        _stream = stream;
        Offset = 0;
    }

    /// <summary>Bytes consumed so far.</summary>
    public long Offset { get; private set; }

    /// <summary>Bytes not yet consumed.</summary>
    public long Remaining => _stream.Length - _stream.Position + (_len - _pos);

    private bool Fill()
    {
        _len = _stream.Read(_buffer, 0, _buffer.Length);
        _pos = 0;
        return _len > 0;
    }

    public int ReadByte()
    {
        if (_pos >= _len && !Fill()) return -1;
        Offset++;
        return _buffer[_pos++];
    }

    public int PeekByte()
    {
        if (_pos >= _len && !Fill()) return -1;
        return _buffer[_pos];
    }

    /// <summary>Fills <paramref name="destination"/> or throws <see cref="EndOfStreamException"/>.</summary>
    public void ReadExactly(Span<byte> destination)
    {
        int done = 0;
        while (done < destination.Length)
        {
            if (_pos >= _len && !Fill()) throw new EndOfStreamException();
            int n = Math.Min(destination.Length - done, _len - _pos);
            _buffer.AsSpan(_pos, n).CopyTo(destination[done..]);
            _pos += n;
            done += n;
            Offset += n;
        }
    }

    /// <summary>Skips <paramref name="count"/> bytes or throws <see cref="EndOfStreamException"/>.</summary>
    public void Skip(long count)
    {
        if (count > Remaining) throw new EndOfStreamException();
        while (count > 0)
        {
            if (_pos >= _len && !Fill()) throw new EndOfStreamException();
            int n = (int)Math.Min(count, _len - _pos);
            _pos += n;
            count -= n;
            Offset += n;
        }
    }

    /// <summary>Reads up to the next LF (dropping a CR before it); null at the end. Throws when a
    /// line is longer than <paramref name="maxLength"/>.</summary>
    public string? ReadLine(int maxLength = 1 << 16)
    {
        var sb = new StringBuilder();
        int b = ReadByte();
        if (b < 0) return null;
        while (b >= 0 && b != '\n')
        {
            if (sb.Length >= maxLength) throw new CaveFileFormatException("line or token too long (not a text model file?)");
            sb.Append((char)b);
            b = ReadByte();
        }
        if (sb.Length > 0 && sb[^1] == '\r') sb.Length--;
        return sb.ToString();
    }

    /// <summary>Reads the next whitespace-delimited token; null at the end.</summary>
    public string? ReadToken()
    {
        int b;
        do { b = ReadByte(); } while (b >= 0 && b <= ' ');
        if (b < 0) return null;
        var sb = new StringBuilder();
        while (b > ' ')
        {
            if (sb.Length > 256) throw new CaveFileFormatException("line or token too long (not a text model file?)");
            sb.Append((char)b);
            b = ReadByte();
        }
        return sb.ToString();
    }
}
