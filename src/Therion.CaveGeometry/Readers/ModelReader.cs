// Front door for reading 3-D model files: finds the format (by content first, by extension as a
// fallback) and hands the file to the reader for it. Coordinates come back as stored.

using System.Buffers.Binary;
using System.Text;
using Therion.Cave3D.Parsing;

namespace Therion.CaveGeometry;

/// <summary>Reads STL, PLY, OBJ, glb and XYZ model files.</summary>
public static class ModelReader
{
    private const int SniffBytes = 4096;

    /// <summary>Reads a model file, detecting its format from content and extension.</summary>
    /// <exception cref="CaveFileFormatException">The format is unknown or the file is malformed.</exception>
    public static ModelFile Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!TryDetectFormat(path, out var format))
            throw new CaveFileFormatException($"Cannot tell what kind of 3-D model file '{Path.GetFileName(path)}' is (known: STL, PLY, OBJ, GLB, XYZ/PTS/CSV/TXT).");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Read(stream, format);
    }

    /// <summary>Reads a model of the given format from the stream's current position.</summary>
    /// <exception cref="CaveFileFormatException">The data is malformed.</exception>
    public static ModelFile Read(Stream stream, ModelFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            stream = copy;
        }
        return format switch
        {
            ModelFileFormat.Stl => StlReader.Read(stream),
            ModelFileFormat.Ply => PlyReader.Read(stream),
            ModelFileFormat.Obj => ObjReader.Read(stream),
            ModelFileFormat.Glb => GlbReader.Read(stream),
            ModelFileFormat.Xyz => XyzReader.Read(stream),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>Works out a file's format: by its content when that is telling (binary STL by its
    /// exact size, "solid" plus "facet", a "ply" line, the "glTF" magic, OBJ vertex lines), else
    /// by extension (.stl .ply .obj .glb .xyz .pts .txt .csv, any case).</summary>
    public static bool TryDetectFormat(string path, out ModelFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(path);
        format = default;
        byte[] head;
        long length;
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
            length = f.Length;
            head = new byte[(int)Math.Min(length, SniffBytes)];
            f.ReadExactly(head);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (head.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(head) == 0x46546C67)
            return Found(ModelFileFormat.Glb, out format);
        if (IsPlyHead(head))
            return Found(ModelFileFormat.Ply, out format);
        if (head.Length >= 84 && StlReader.IsBinarySize(length, BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(80))))
            return Found(ModelFileFormat.Stl, out format);

        string text = Encoding.UTF8.GetString(head);
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("solid", StringComparison.Ordinal) && text.Contains("facet", StringComparison.Ordinal))
            return Found(ModelFileFormat.Stl, out format);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".obj" || LooksLikeObj(text))
            return Found(ModelFileFormat.Obj, out format);

        switch (ext)
        {
            case ".stl": return Found(ModelFileFormat.Stl, out format);
            case ".ply": return Found(ModelFileFormat.Ply, out format);
            case ".glb": return Found(ModelFileFormat.Glb, out format);
            case ".xyz":
            case ".pts":
                return Found(ModelFileFormat.Xyz, out format);
            case ".txt":
            case ".csv":
                if (HasNumericLine(text)) return Found(ModelFileFormat.Xyz, out format);
                break;
        }
        return false;
    }

    private static bool Found(ModelFileFormat value, out ModelFileFormat format)
    {
        format = value;
        return true;
    }

    private static bool IsPlyHead(byte[] head)
        => head.Length >= 4 && head[0] == 'p' && head[1] == 'l' && head[2] == 'y' && (head[3] == '\n' || head[3] == '\r' || head[3] == ' ');

    private static bool LooksLikeObj(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal) && XyzReader.IsPointLine(line[2..])) return true;
        }
        return false;
    }

    private static bool HasNumericLine(string text)
    {
        foreach (var raw in text.Split('\n'))
            if (XyzReader.IsPointLine(raw.Trim())) return true;
        return false;
    }
}
