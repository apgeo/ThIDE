// Format-sniffing entry point over the two native parsers (BA-B2): callers that hold
// "some model artifact" (BA-B4 source acquisition, the future UI) don't need to care
// which format it is. Detection is content-first (magic bytes), extension-assisted.

using System.Text;

namespace Therion.Cave3D.Parsing;

/// <summary>
/// Reads a cave model file of either supported format, deciding between
/// <see cref="LoxReader"/> and <see cref="Survex3dReader"/> by content (magic bytes),
/// falling back to the file extension for edge cases.
/// </summary>
public static class CaveModelReader
{
    private static readonly byte[] SurvexMagic = Encoding.ASCII.GetBytes("Survex 3D Image File");

    /// <summary>Reads and parses the model file at <paramref name="path"/>.</summary>
    public static CaveModel ReadFile(string path)
        => Read(File.ReadAllBytes(path), path);

    /// <summary>Parses a model file from its raw bytes.</summary>
    public static CaveModel Read(byte[] data, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Detect(data, sourcePath) switch
        {
            CaveSourceFormat.Survex3d => Survex3dReader.Read(data, sourcePath),
            CaveSourceFormat.Lox => LoxReader.Read(data, sourcePath),
            _ => throw new CaveFileFormatException(
                $"Unrecognized cave model format{(sourcePath is null ? "" : $" for \"{sourcePath}\"")}: " +
                "expected a Therion .lox or Survex .3d file."),
        };
    }

    /// <summary>Detects the model format from content (and, when content is not
    /// conclusive, the file extension). Returns Unknown when neither matches.</summary>
    public static CaveSourceFormat Detect(ReadOnlySpan<byte> data, string? sourcePath = null)
    {
        if (data.Length >= SurvexMagic.Length && data[..SurvexMagic.Length].SequenceEqual(SurvexMagic))
            return CaveSourceFormat.Survex3d;

        // A .lox file starts straight with a chunk header whose sizes fit the file. The
        // type is not limited to the six known ones: a newer Therion may write others,
        // which the reader skips. (An empty file is a valid, empty .lox model.)
        if (data.Length == 0)
            return CaveSourceFormat.Lox;
        if (IsPlausibleLoxChunkHeader(data))
            return CaveSourceFormat.Lox;

        var extension = sourcePath is null ? null : Path.GetExtension(sourcePath).ToLowerInvariant();
        return extension switch
        {
            ".lox" => CaveSourceFormat.Lox,
            ".3d" => CaveSourceFormat.Survex3d,
            _ => CaveSourceFormat.Unknown,
        };
    }

    // A known chunk type (1-6) is taken as .lox as before, so a damaged file is still
    // reported as a corrupt .lox. Any other type must look like a real chunk: non-zero,
    // a small number, and a record + data size that fit the file.
    private static bool IsPlausibleLoxChunkHeader(ReadOnlySpan<byte> data)
    {
        const int headerSize = 16;
        if (data.Length < headerSize) return false;
        uint type = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (type is >= 1 and <= 6) return true;
        if (type is 0 or > 0xFFFF) return false;
        ulong recSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        ulong dataSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        return headerSize + recSize + dataSize <= (ulong)data.Length;
    }
}
