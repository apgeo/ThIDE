// Native reader for the Survex ".3d" processed-survey format, versions 3-8 (BA-B2).
//
// Implemented from the OFFICIAL format specifications shipped with Survex —
// doc/3dformat.htm (v8) and doc/3dformat-old.htm (v3-v7) — with behavior
// cross-checked against Survex's own reader `img.c` (GPL-2.0-or-later, consulted as a
// normative format reference only; no code copied — this file is an original
// implementation, see D-21) and CaveView.js's independent MIT `svx3dLoader.js`.
//
// Format recap: text header (file id, "v<N>", title line — v8 smuggles NUL-separated
// coordinate-system + separator metadata after the title — and a datestamp line; v8
// adds a file-wide flags byte), then a byte-code item stream. Coordinates are s32
// little-endian centimetres. Station/survey labels are maintained in a stateful label
// buffer the items patch incrementally (v8: delete/add counts; v3-7: append with
// length-prefixed strings plus TRIM opcodes). The stream ends at a STOP item.
//
// Versions 0-2 (pre-1997: "v0.01"/"Bv0.01"/"v2") are rejected as unsupported — files
// that old can be rewritten as v8 by any modern Survex.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Therion.Blender.Parsing;

/// <summary>
/// Parses Survex <c>.3d</c> files (format versions 3–8) into a
/// <see cref="CaveModel"/>, extracting every field the format carries (D-19). Throws
/// <see cref="CaveFileFormatException"/> on malformed or unsupported input.
/// </summary>
public static class Survex3dReader
{
    private const string FileId = "Survex 3D Image File";
    private static readonly DateOnly Day1900 = new(1900, 1, 1);

    /// <summary>Reads and parses the <c>.3d</c> file at <paramref name="path"/>.</summary>
    public static CaveModel ReadFile(string path)
        => Read(File.ReadAllBytes(path), path);

    /// <summary>Parses a <c>.3d</c> file from a stream (fully buffered in memory).</summary>
    public static CaveModel Read(Stream stream, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray(), sourcePath);
    }

    /// <summary>Parses a <c>.3d</c> file from its raw bytes.</summary>
    public static CaveModel Read(byte[] data, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var cursor = new Cursor(data);

        // ----- header -----
        var magic = cursor.Line("file id");
        if (magic == FileId + "\r")
            throw new CaveFileFormatException(
                "Unsupported .3d format version \"v0.01\" with DOS line endings (pre-1997); convert the file with a modern Survex first.");
        if (magic != FileId)
            throw Corrupt("not a Survex 3D Image File (bad file id)");

        var versionLine = cursor.Line("format version");
        int version = versionLine switch
        {
            "v3" => 3, "v4" => 4, "v5" => 5, "v6" => 6, "v7" => 7, "v8" => 8,
            "v0.01" or "Bv0.01" or "bv0.01" or "v2" => throw new CaveFileFormatException(
                $"Unsupported .3d format version \"{versionLine}\" (pre-1997); convert the file with a modern Survex first."),
            _ => throw Corrupt($"unrecognized format version \"{versionLine}\""),
        };

        var titleLine = cursor.RawLine("title");
        string title;
        string? coordinateSystem = null;
        char separator = '.';
        bool isExtendedElevation = false;
        if (version == 8)
        {
            // v8 appends NUL-separated extra metadata after the title: coordinate
            // system, then the survey-hierarchy separator character. Split at the
            // byte level (UTF-8 never contains 0x00 inside a sequence).
            var fields = SplitOnNul(titleLine);
            title = Decode(fields[0]);
            if (fields.Count > 1 && fields[1].Length > 0)
                coordinateSystem = Decode(fields[1]);
            if (fields.Count > 2 && fields[2].Length > 0)
                separator = (char)fields[2][0];
        }
        else
        {
            title = Decode(titleLine);
            if (title.EndsWith(" (extended)", StringComparison.Ordinal))
            {
                title = title[..^" (extended)".Length];
                isExtendedElevation = true;
            }
        }

        var datestamp = Decode(cursor.RawLine("datestamp"));
        DateTimeOffset? timestamp = null;
        if (datestamp.StartsWith('@')
            && long.TryParse(datestamp.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds))
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        if (version >= 8 && (cursor.Byte("file-wide flags") & 0x80) != 0)
            isExtendedElevation = true;

        // ----- item stream -----
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        var passages = new List<CavePassage>();
        var traverseErrors = new List<CaveTraverseError>();

        var label = new LabelBuffer();
        var position = default(CaveVector3);
        int style = -1; // img_STYLE_UNKNOWN — a 0x00 item only stops once style is Normal
        DateOnly? dateFrom = null, dateTo = null;
        int traverseStart = 0;
        var openPassage = new List<CavePassageStation>();

        bool stopped = false;
        while (!stopped)
        {
            int opt = cursor.Byte("item code");
            if (version >= 8)
            {
                switch (opt)
                {
                    case 0x00 when style == 0:
                        stopped = true;
                        break;
                    case <= 0x04: // style change (0x00 first sets Normal)
                        style = opt;
                        break;
                    case 0x0f: // MOVE
                        position = cursor.Point();
                        traverseStart = shots.Count;
                        break;
                    case 0x10: // no date info
                        dateFrom = dateTo = null;
                        break;
                    case 0x11: // single date, days since 1900
                        dateFrom = dateTo = Day1900.AddDays(cursor.U16());
                        break;
                    case 0x12: // short date range
                        dateFrom = Day1900.AddDays(cursor.U16());
                        dateTo = dateFrom.Value.AddDays(cursor.Byte("date span") + 1);
                        break;
                    case 0x13: // long date range
                        dateFrom = Day1900.AddDays(cursor.U16());
                        dateTo = Day1900.AddDays(cursor.U16());
                        break;
                    case 0x1f: // traverse error info
                        traverseErrors.Add(ReadErrorInfo(ref cursor, shots.Count, traverseStart));
                        break;
                    case >= 0x30 and <= 0x33: // XSECT
                        label.ApplyV8Patch(ref cursor);
                        ReadCrossSection(ref cursor, label.Value, wide: opt >= 0x32,
                            endsPassage: (opt & 0x01) != 0, openPassage, passages);
                        break;
                    case >= 0x40 and <= 0x7f: // LINE (leg)
                        if ((opt & 0x20) == 0) label.ApplyV8Patch(ref cursor);
                        var lineEnd = cursor.Point();
                        shots.Add(MakeShot(position, lineEnd, (uint)(opt & 0x1f), label.Value, style, dateFrom, dateTo));
                        position = lineEnd;
                        break;
                    case >= 0x80: // LABEL (station)
                        label.ApplyV8Patch(ref cursor);
                        stations.Add(MakeStation((uint)stations.Count, label.Value, (uint)(opt & 0x7f), cursor.Point()));
                        break;
                    default: // 0x05-0x0e, 0x14-0x1e, 0x20-0x2f, 0x34-0x3f reserved
                        throw Corrupt($"reserved item code 0x{opt:x2}");
                }
            }
            else
            {
                switch (opt)
                {
                    case 0x00: // STOP, or "clear label" when it isn't empty
                        if (label.IsEmpty) stopped = true;
                        else label.Clear();
                        break;
                    case <= 0x0e: // trim 16 chars + N dot-levels
                        label.TrimLevels(opt);
                        break;
                    case 0x0f: // MOVE
                        position = cursor.Point();
                        traverseStart = shots.Count;
                        break;
                    case <= 0x1f: // trim N-15 chars
                        label.TrimChars(opt - 15);
                        break;
                    case 0x20: // single date (v4-6: time_t seconds; v7: days since 1900)
                        dateFrom = dateTo = version < 7 ? FromTimeT(cursor.U32()) : Day1900.AddDays(cursor.U16());
                        break;
                    case 0x21: // date range (v4-6: two time_t; v7: days + span byte)
                        if (version < 7)
                        {
                            dateFrom = FromTimeT(cursor.U32());
                            dateTo = FromTimeT(cursor.U32());
                        }
                        else
                        {
                            dateFrom = Day1900.AddDays(cursor.U16());
                            dateTo = dateFrom.Value.AddDays(cursor.Byte("date span") + 1);
                        }
                        break;
                    case 0x22 when version >= 6: // traverse error info
                        traverseErrors.Add(ReadErrorInfo(ref cursor, shots.Count, traverseStart));
                        break;
                    case 0x23 when version >= 7: // long date range
                        dateFrom = Day1900.AddDays(cursor.U16());
                        dateTo = Day1900.AddDays(cursor.U16());
                        break;
                    case 0x24 when version >= 7: // no date info
                        dateFrom = dateTo = null;
                        break;
                    case >= 0x30 and <= 0x33 when version >= 5: // XSECT
                        label.AppendV3Label(ref cursor);
                        ReadCrossSection(ref cursor, label.Value, wide: opt >= 0x32,
                            endsPassage: (opt & 0x01) != 0, openPassage, passages);
                        break;
                    case >= 0x40 and <= 0x7f: // LABEL (station)
                        label.AppendV3Label(ref cursor);
                        stations.Add(MakeStation((uint)stations.Count, label.Value, (uint)(opt & 0x3f), cursor.Point()));
                        break;
                    case >= 0x80 and <= 0xbf: // LINE (leg)
                        label.AppendV3Label(ref cursor);
                        var lineEnd = cursor.Point();
                        shots.Add(MakeShot(position, lineEnd, (uint)(opt & 0x3f), label.Value, style: -1, dateFrom, dateTo));
                        position = lineEnd;
                        break;
                    default:
                        throw Corrupt($"reserved item code 0x{opt:x2} for format version {version}");
                }
            }
        }

        if (openPassage.Count > 0)
            passages.Add(new CavePassage { Stations = openPassage });

        return new CaveModel
        {
            SourcePath = sourcePath,
            SourceFormat = CaveSourceFormat.Survex3d,
            FormatVersion = version,
            Title = title,
            CoordinateSystem = coordinateSystem,
            SeparatorChar = separator,
            Datestamp = datestamp,
            Timestamp = timestamp,
            IsExtendedElevation = isExtendedElevation,
            Stations = stations,
            Shots = shots,
            Passages = passages,
            TraverseErrors = traverseErrors,
        };
    }

    private static CaveStation MakeStation(uint id, string name, uint rawFlags, CaveVector3 position) => new()
    {
        Id = id,
        Name = name,
        Position = position,
        Flags = MapStationFlags(rawFlags),
        RawFlags = rawFlags,
    };

    private static CaveShot MakeShot(
        CaveVector3 from, CaveVector3 to, uint rawFlags, string surveyName,
        int style, DateOnly? dateFrom, DateOnly? dateTo) => new()
    {
        FromPosition = from,
        ToPosition = to,
        SurveyName = surveyName.Length == 0 ? null : surveyName,
        Flags = MapShotFlags(rawFlags),
        RawFlags = rawFlags,
        Style = style < 0 ? null : (SurveyStyle)style,
        DateFrom = dateFrom,
        DateTo = dateTo,
    };

    private static void ReadCrossSection(
        ref Cursor cursor, string stationName, bool wide, bool endsPassage,
        List<CavePassageStation> openPassage, List<CavePassage> passages)
    {
        double l, r, u, d;
        if (wide)
        {
            l = cursor.S32() / 100.0;
            r = cursor.S32() / 100.0;
            u = cursor.S32() / 100.0;
            d = cursor.S32() / 100.0;
        }
        else
        {
            l = cursor.S16() / 100.0;
            r = cursor.S16() / 100.0;
            u = cursor.S16() / 100.0;
            d = cursor.S16() / 100.0;
        }
        openPassage.Add(new CavePassageStation(stationName, l, r, u, d));
        if (endsPassage)
        {
            passages.Add(new CavePassage { Stations = openPassage.ToArray() });
            openPassage.Clear();
        }
    }

    private static CaveTraverseError ReadErrorInfo(ref Cursor cursor, int shotCount, int traverseStart) => new()
    {
        ShotStartIndex = traverseStart,
        ShotCount = shotCount - traverseStart,
        LegCount = cursor.S32(),
        Length = cursor.S32() / 100.0,
        Error = cursor.S32() / 100.0,
        HorizontalError = cursor.S32() / 100.0,
        VerticalError = cursor.S32() / 100.0,
    };

    /// <summary>Survey dates in v4–v6 files are Unix time_t values; 0 means unknown.</summary>
    private static DateOnly? FromTimeT(uint seconds)
        => seconds == 0 ? null : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);

    private static CaveStationFlags MapStationFlags(uint raw)
    {
        var flags = CaveStationFlags.None;
        if ((raw & 0x01) != 0) flags |= CaveStationFlags.Surface;
        if ((raw & 0x02) != 0) flags |= CaveStationFlags.Underground;
        if ((raw & 0x04) != 0) flags |= CaveStationFlags.Entrance;
        if ((raw & 0x08) != 0) flags |= CaveStationFlags.Exported;
        if ((raw & 0x10) != 0) flags |= CaveStationFlags.Fixed;
        if ((raw & 0x20) != 0) flags |= CaveStationFlags.Anonymous;
        if ((raw & 0x40) != 0) flags |= CaveStationFlags.Wall;
        return flags;
    }

    private static CaveShotFlags MapShotFlags(uint raw)
    {
        var flags = CaveShotFlags.None;
        if ((raw & 0x01) != 0) flags |= CaveShotFlags.Surface;
        if ((raw & 0x02) != 0) flags |= CaveShotFlags.Duplicate;
        if ((raw & 0x04) != 0) flags |= CaveShotFlags.Splay;
        return flags;
    }

    private static List<byte[]> SplitOnNul(byte[] line)
    {
        var fields = new List<byte[]>();
        int start = 0;
        while (start <= line.Length)
        {
            int nul = Array.IndexOf(line, (byte)0, start);
            int end = nul < 0 ? line.Length : nul;
            fields.Add(line[start..end]);
            if (nul < 0) break;
            start = nul + 1;
        }
        return fields;
    }

    private static string Decode(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    private static CaveFileFormatException Corrupt(string detail)
        => new($"Invalid .3d file: {detail}.");

    /// <summary>Bounds-checked sequential reader over the file bytes.</summary>
    private struct Cursor(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _pos = 0;

        public readonly int Remaining => _data.Length - _pos;

        public int Byte(string what)
        {
            if (_pos >= _data.Length) throw Corrupt($"unexpected end of file reading {what}");
            return _data[_pos++];
        }

        public ushort U16()
        {
            Need(2, "16-bit value");
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_pos));
            _pos += 2;
            return value;
        }

        public short S16()
        {
            Need(2, "16-bit value");
            var value = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_pos));
            _pos += 2;
            return value;
        }

        public uint U32()
        {
            Need(4, "32-bit value");
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_pos));
            _pos += 4;
            return value;
        }

        public int S32() => unchecked((int)U32());

        /// <summary>Reads a MOVE/LINE/LABEL coordinate triple (s32 centimetres each).</summary>
        public CaveVector3 Point()
            => new(S32() / 100.0, S32() / 100.0, S32() / 100.0);

        public byte[] Bytes(int count, string what)
        {
            Need(count, what);
            var result = _data.AsSpan(_pos, count).ToArray();
            _pos += count;
            return result;
        }

        /// <summary>Reads up to a linefeed, returning the raw bytes without it.</summary>
        public byte[] RawLine(string what)
        {
            int nl = Array.IndexOf(_data, (byte)'\n', _pos);
            if (nl < 0) throw Corrupt($"unexpected end of file reading the {what} line");
            var line = _data.AsSpan(_pos, nl - _pos).ToArray();
            _pos = nl + 1;
            return line;
        }

        public string Line(string what) => Decode(RawLine(what));

        private readonly void Need(int count, string what)
        {
            if (Remaining < count) throw Corrupt($"unexpected end of file reading {what}");
        }
    }

    /// <summary>
    /// The stateful label buffer the item stream patches. Operates on bytes (the
    /// formats' delete/add/trim counts are byte counts); decodes to UTF-8 lazily.
    /// </summary>
    private sealed class LabelBuffer
    {
        private byte[] _bytes = new byte[64];
        private int _length;
        private string _decoded = "";
        private bool _dirty;

        public bool IsEmpty => _length == 0;

        public string Value
        {
            get
            {
                if (_dirty)
                {
                    _decoded = Encoding.UTF8.GetString(_bytes, 0, _length);
                    _dirty = false;
                }
                return _decoded;
            }
        }

        public void Clear()
        {
            _length = 0;
            _dirty = true;
        }

        /// <summary>v8: delete D bytes from the end, append the next A file bytes
        /// (D/A packed in one byte's nibbles, or escaped as byte/u32 counts).</summary>
        public void ApplyV8Patch(ref Cursor cursor)
        {
            long del, add;
            int first = cursor.Byte("label patch");
            if (first != 0x00)
            {
                del = first >> 4;
                add = first & 0x0f;
            }
            else
            {
                int d = cursor.Byte("label delete count");
                del = d != 0xff ? d : cursor.U32();
                int a = cursor.Byte("label append count");
                add = a != 0xff ? a : cursor.U32();
            }
            if (del > _length)
                throw Corrupt($"label patch deletes {del} bytes but only {_length} are buffered");
            if (add > cursor.Remaining)
                throw Corrupt($"label patch appends {add} bytes but only {cursor.Remaining} remain in the file");
            _length -= (int)del;
            Append(ref cursor, (int)add);
        }

        /// <summary>v3–7: append a length-prefixed string (0xfe/0xff escape to u16/u32
        /// lengths).</summary>
        public void AppendV3Label(ref Cursor cursor)
        {
            long length = cursor.Byte("label length");
            if (length == 0xfe)
                length = 0xfe + cursor.U16();
            else if (length == 0xff)
            {
                length = cursor.U32();
                if (length < 0xfe + 0xffff)
                    throw Corrupt("over-long label length encoding used for a short label");
            }
            if (length > cursor.Remaining)
                throw Corrupt($"label appends {length} bytes but only {cursor.Remaining} remain in the file");
            Append(ref cursor, (int)length);
        }

        /// <summary>v3–7 TRIM 0x01–0x0e: drop the last 16 bytes, then cut back to (and
        /// keep) the dot closing the Nth level from there.</summary>
        public void TrimLevels(int levels)
        {
            // Mirrors the spec: it is an error to trim more label than there is.
            int index = _length - 18;
            if (index < 0) throw Corrupt("label level-trim on a too-short label");
            int remaining = levels;
            while (true)
            {
                if (_bytes[index] == (byte)'.' && --remaining <= 0) break;
                if (--index < 0) throw Corrupt("label level-trim consumed the whole label");
            }
            _length = index + 1;
            _dirty = true;
        }

        /// <summary>v3–7 TRIM 0x10–0x1f: drop the last N bytes (never all of them).</summary>
        public void TrimChars(int count)
        {
            if (_length <= count) throw Corrupt("label char-trim consumed the whole label");
            _length -= count;
            _dirty = true;
        }

        private void Append(ref Cursor cursor, int count)
        {
            if (count > 0)
            {
                if (_length + count > _bytes.Length)
                    Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, _length + count));
                cursor.Bytes(count, "label bytes").CopyTo(_bytes.AsSpan(_length));
                _length += count;
            }
            _dirty = true;
        }
    }
}
