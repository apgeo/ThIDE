// Test-side .3d byte-stream builder: composes spec-exact v3-v8 files so the reader
// can be exercised without binary fixtures in the repo. Deliberately low-level — each
// method writes exactly the wire bytes the spec describes, so tests can also compose
// malformed streams. Label state is NOT tracked; callers pass explicit patch/append
// values (that's the point: exercising the label machinery is the tests' job).

using System.Buffers.Binary;
using System.Text;

namespace Therion.Blender.Tests;

internal sealed class Survex3dFixtureBuilder
{
    private readonly MemoryStream _stream = new();

    public static Survex3dFixtureBuilder V8(
        string title, string? coordinateSystem = null, char? separator = null,
        string datestamp = "@1371300355", byte fileFlags = 0)
    {
        var builder = new Survex3dFixtureBuilder();
        builder.Text("Survex 3D Image File\nv8\n");
        builder.Text(title);
        if (coordinateSystem is not null || separator is not null)
        {
            builder.Text("\0" + (coordinateSystem ?? ""));
            if (separator is not null) builder.Text("\0" + separator);
        }
        builder.Text("\n" + datestamp + "\n");
        builder._stream.WriteByte(fileFlags);
        return builder;
    }

    public static Survex3dFixtureBuilder Old(
        int version, string title, string datestamp = "Sun,2002.03.17 14:01:07 GMT")
    {
        var builder = new Survex3dFixtureBuilder();
        builder.Text($"Survex 3D Image File\nv{version}\n{title}\n{datestamp}\n");
        return builder;
    }

    public byte[] Build() => _stream.ToArray();

    // ----- generic wire primitives -----

    public Survex3dFixtureBuilder Op(int opcode)
    {
        _stream.WriteByte((byte)opcode);
        return this;
    }

    public Survex3dFixtureBuilder Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
        return this;
    }

    public Survex3dFixtureBuilder U16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _stream.Write(bytes);
        return this;
    }

    public Survex3dFixtureBuilder S16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        _stream.Write(bytes);
        return this;
    }

    public Survex3dFixtureBuilder S32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _stream.Write(bytes);
        return this;
    }

    /// <summary>Writes a coordinate triple in metres as s32 centimetres.</summary>
    public Survex3dFixtureBuilder Point(double x, double y, double z)
        => S32(Cm(x)).S32(Cm(y)).S32(Cm(z));

    private static int Cm(double metres) => checked((int)Math.Round(metres * 100.0));

    // ----- v8 items -----

    /// <summary>The v8 label patch: delete <paramref name="delete"/> bytes, append
    /// <paramref name="append"/> — packed nibbles when possible, escaped otherwise.</summary>
    public Survex3dFixtureBuilder V8LabelPatch(int delete, string append, bool forceEscaped = false)
    {
        var bytes = Encoding.UTF8.GetBytes(append);
        if (!forceEscaped && (delete | bytes.Length) != 0 && delete <= 15 && bytes.Length <= 15)
        {
            _stream.WriteByte((byte)((delete << 4) | bytes.Length));
        }
        else
        {
            _stream.WriteByte(0x00);
            WriteEscapedCount(delete);
            WriteEscapedCount(bytes.Length);
        }
        _stream.Write(bytes, 0, bytes.Length);
        return this;

        void WriteEscapedCount(int count)
        {
            if (count < 0xff) _stream.WriteByte((byte)count);
            else
            {
                _stream.WriteByte(0xff);
                Span<byte> wide = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(wide, (uint)count);
                _stream.Write(wide);
            }
        }
    }

    public Survex3dFixtureBuilder V8Style(int style) => Op(style);

    public Survex3dFixtureBuilder V8Move(double x, double y, double z) => Op(0x0f).Point(x, y, z);

    public Survex3dFixtureBuilder V8Label(int flags, int delete, string append, double x, double y, double z)
        => Op(0x80 | flags).V8LabelPatch(delete, append).Point(x, y, z);

    /// <summary>A LINE item; pass a null patch to set flag 0x20 (label unchanged).</summary>
    public Survex3dFixtureBuilder V8Line(int flags, (int Delete, string Append)? patch, double x, double y, double z)
    {
        Op(0x40 | flags | (patch is null ? 0x20 : 0));
        if (patch is { } p) V8LabelPatch(p.Delete, p.Append);
        return Point(x, y, z);
    }

    public Survex3dFixtureBuilder V8XSect(int delete, string append, double l, double r, double u, double d, bool last = false, bool wide = false)
    {
        Op((wide ? 0x32 : 0x30) | (last ? 0x01 : 0)).V8LabelPatch(delete, append);
        return wide
            ? S32(Cm(l)).S32(Cm(r)).S32(Cm(u)).S32(Cm(d))
            : S16((short)Cm(l)).S16((short)Cm(r)).S16((short)Cm(u)).S16((short)Cm(d));
    }

    public Survex3dFixtureBuilder V8SingleDate(int daysSince1900) => Op(0x11).U16((ushort)daysSince1900);

    public Survex3dFixtureBuilder V8ErrorInfo(int legs, double lengthMetres, double e, double h, double v)
        => Op(0x1f).S32(legs).S32(Cm(lengthMetres)).S32(Cm(e)).S32(Cm(h)).S32(Cm(v));

    /// <summary>End-of-data as the Survex writer emits it: 0x00 to return the style to
    /// Normal if needed, then the 0x00 STOP.</summary>
    public Survex3dFixtureBuilder V8Stop(bool styleAlreadyNormal = true)
    {
        if (!styleAlreadyNormal) Op(0x00);
        return Op(0x00);
    }

    // ----- v3-v7 items -----

    /// <summary>The v3-7 length-prefixed label append (0xfe/0xff escapes).</summary>
    public Survex3dFixtureBuilder V3LabelBytes(string append)
    {
        var bytes = Encoding.UTF8.GetBytes(append);
        if (bytes.Length < 0xfe)
            _stream.WriteByte((byte)bytes.Length);
        else if (bytes.Length <= 0xfe + 0xffff)
        {
            _stream.WriteByte(0xfe);
            U16((ushort)(bytes.Length - 0xfe));
        }
        else
        {
            _stream.WriteByte(0xff);
            S32(bytes.Length);
        }
        _stream.Write(bytes, 0, bytes.Length);
        return this;
    }

    public Survex3dFixtureBuilder OldMove(double x, double y, double z) => Op(0x0f).Point(x, y, z);

    public Survex3dFixtureBuilder OldLabel(int flags, string append, double x, double y, double z)
        => Op(0x40 | flags).V3LabelBytes(append).Point(x, y, z);

    public Survex3dFixtureBuilder OldLine(int flags, string append, double x, double y, double z)
        => Op(0x80 | flags).V3LabelBytes(append).Point(x, y, z);

    /// <summary>TRIM 0x01-0x0e: cut 16 chars then N dot-levels.</summary>
    public Survex3dFixtureBuilder OldTrimLevels(int levels) => Op(levels);

    /// <summary>TRIM 0x10-0x1f: cut N (1-16) chars.</summary>
    public Survex3dFixtureBuilder OldTrimChars(int count) => Op(0x0f + count);

    public Survex3dFixtureBuilder OldClearLabel() => Op(0x00);

    public Survex3dFixtureBuilder OldXSect(string append, double l, double r, double u, double d, bool last = false)
        => Op(0x30 | (last ? 0x01 : 0)).V3LabelBytes(append)
            .S16((short)Cm(l)).S16((short)Cm(r)).S16((short)Cm(u)).S16((short)Cm(d));

    /// <summary>v4-6 single date as Unix seconds (0 = unknown).</summary>
    public Survex3dFixtureBuilder OldTimeTDate(uint seconds) => Op(0x20).S32(unchecked((int)seconds));

    /// <summary>End-of-data for v3-7: STOP is only a STOP once the label is empty.</summary>
    public Survex3dFixtureBuilder OldStop(bool labelEmpty = false)
    {
        if (!labelEmpty) Op(0x00);
        return Op(0x00);
    }
}
