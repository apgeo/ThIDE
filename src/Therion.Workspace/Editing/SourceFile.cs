// Reading and writing a Therion source file without changing anything about it except the text.
//
// A .th file may be UTF-8, UTF-16, or Latin-1 declared by an `encoding` directive, and Therion reads
// it back with that same rule. Rewriting one as UTF-8 leaves the directive lying about the bytes,
// which corrupts every accented survey name in the file. So: decode with EncodingResolver, re-encode
// with whatever it resolved, and put the original byte-order mark back exactly as it was.

using System.Security.Cryptography;
using System.Text;
using Therion.Syntax;

namespace Therion.Workspace.Editing;

/// <summary>
/// The new text holds a character the file's declared encoding cannot represent. Writing anyway would
/// substitute <c>?</c> for it, silently and irreversibly.
/// </summary>
public sealed class UnrepresentableCharacterException(string path, string encodingName, char character)
    : Exception($"'{character}' (U+{(int)character:X4}) cannot be written in {encodingName}, "
              + $"which '{path}' declares. Writing it would replace the character with '?'.")
{
    public char Character { get; } = character;
    public string EncodingName { get; } = encodingName;
}

/// <param name="Sha256">Lowercase hex digest of the file's bytes, for optimistic concurrency.</param>
public sealed record SourceFile(string Path, string Text, string Sha256, Encoding Encoding, byte[] Bom);

/// <summary>How a text file was stored: its encoding and its byte-order mark (empty when none).</summary>
public sealed record TextFileFormat(Encoding Encoding, byte[] Bom)
{
    /// <summary>UTF-8 without a BOM — Therion's default, and what a new file is written as.</summary>
    public static TextFileFormat Default { get; } = new(EncodingResolver.Default, []);
}

public static class SourceFileIo
{
    /// <summary>The format a file was read in.</summary>
    public static TextFileFormat FormatOf(SourceFile file) => new(file.Encoding, file.Bom);

    /// <summary>
    /// The format to save <paramref name="text"/> in — whatever <see cref="Read"/> (and Therion) will
    /// resolve the saved bytes as: the encoding the text's own <c>encoding</c> line declares, else the
    /// one its BOM implies, else UTF-8. So editing or deleting that line takes effect on save. The
    /// original BOM is kept when it still matches the encoding.
    /// </summary>
    /// <remarks>
    /// In a UTF-16 file the directive is invisible to the byte-level scan, so the file keeps its format.
    /// A directive naming an encoding the scan could not find again in the saved bytes (UTF-16, UTF-32)
    /// is ignored for the same reason.
    /// </remarks>
    public static TextFileFormat FormatForSave(string text, TextFileFormat? readAs)
    {
        readAs ??= TextFileFormat.Default;
        EncodingResolver.BomLength(readAs.Bom, out var bomEncoding);
        if (bomEncoding is not null && !IsAsciiCompatible(bomEncoding)) return readAs;

        var probe = Encoding.UTF8.GetBytes(text.Length > EncodingResolver.ProbeBytes ? text[..EncodingResolver.ProbeBytes] : text);
        var declared = EncodingResolver.DetectEncoding(probe);
        if (declared is not null && !IsAsciiCompatible(declared)) declared = null;

        var encoding = declared ?? bomEncoding ?? EncodingResolver.Default;
        if (encoding.CodePage == readAs.Encoding.CodePage) return readAs;
        return new TextFileFormat(encoding, bomEncoding?.CodePage == encoding.CodePage ? readAs.Bom : []);
    }

    /// <summary>
    /// Saves <paramref name="text"/> over <paramref name="path"/> (atomically, see <see cref="WriteOver"/>)
    /// in the format the file has on disk, or <paramref name="readAs"/> — the format an open editor read
    /// it with. Returns the format used.
    /// </summary>
    /// <exception cref="UnrepresentableCharacterException">The text holds a character the encoding cannot express.</exception>
    public static TextFileFormat SaveText(string path, string text, TextFileFormat? readAs = null)
    {
        var (bytes, format) = EncodeForSave(path, text, readAs);
        WriteOver(path, bytes);
        return format;
    }

    /// <summary>
    /// The bytes to save <paramref name="text"/> as (see <see cref="FormatForSave"/>). A null
    /// <paramref name="readAs"/> means the format the file has on disk now (UTF-8 for a new file).
    /// </summary>
    /// <exception cref="UnrepresentableCharacterException">The text holds a character the encoding cannot express.</exception>
    public static (byte[] Bytes, TextFileFormat Format) EncodeForSave(string path, string text, TextFileFormat? readAs)
    {
        var format = FormatForSave(text, readAs ?? FormatOnDisk(path));
        return (WithBom(format.Bom, Encode(path, format.Encoding, text)), format);
    }

    /// <summary>The format <paramref name="path"/> is stored in, or <see cref="TextFileFormat.Default"/> when it is not there.</summary>
    public static TextFileFormat FormatOnDisk(string path)
    {
        if (!File.Exists(path)) return TextFileFormat.Default;
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength) = Resolve(bytes);
        return new TextFileFormat(encoding, bytes[..bomLength]);
    }

    /// <summary>Reads a file, resolving its encoding the way the parser does.</summary>
    public static SourceFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength) = Resolve(bytes);

        return new SourceFile(
            Path: path,
            Text: encoding.GetString(bytes, bomLength, bytes.Length - bomLength),
            Sha256: Digest(bytes),
            Encoding: encoding,
            Bom: bytes[..bomLength]);
    }

    /// <summary>
    /// Replaces <paramref name="file"/>'s contents with <paramref name="text"/>, re-encoded as it was.
    /// The write goes to a sibling temporary file and is then moved over the target, so a crash or a
    /// full disk leaves the original intact rather than truncated.
    /// </summary>
    /// <exception cref="UnrepresentableCharacterException">
    /// <paramref name="text"/> holds a character the file's encoding cannot express. The default
    /// encoder fallback would write <c>?</c> instead — a rename to <c>Peștera</c> inside a Latin-1
    /// survey must fail, not quietly become <c>Pe?tera</c>.
    /// </exception>
    public static void Write(SourceFile file, string text)
    {
        WriteOver(file.Path, WithBom(file.Bom, Encode(file.Path, file.Encoding, text)));
    }

    /// <summary>
    /// Creates a new file as UTF-8 without a BOM — the encoding Therion assumes by default. Fails if
    /// anything is already there: <see cref="FileMode.CreateNew"/> makes that check and the creation one
    /// indivisible operation, which <c>File.Exists</c> followed by a write is not.
    /// </summary>
    /// <exception cref="IOException">The path already exists.</exception>
    public static void Create(string path, string content)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = EncodingResolver.Default.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// True when <paramref name="text"/> can be written in <paramref name="file"/>'s encoding, so a
    /// batch can refuse up front instead of failing part-way through.
    /// </summary>
    public static bool CanEncode(SourceFile file, string text)
    {
        try { Encode(file.Path, file.Encoding, text); return true; }
        catch (UnrepresentableCharacterException) { return false; }
    }

    /// <summary>Writes bytes over an existing file, atomically. Used for the write and for rolling it back.</summary>
    public static void WriteOver(string path, byte[] bytes)
    {
        // Same directory as the target: File.Move across volumes is a copy, which is not atomic.
        var temporary = path + ".therion-write-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }

    public static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Digest of the file on disk, or null when it is not there.</summary>
    public static string? DigestOf(string path)
    {
        try { return File.Exists(path) ? Digest(File.ReadAllBytes(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Encodes with an exception fallback rather than the default best-fit one, so a character the
    /// encoding cannot hold stops the write instead of becoming <c>?</c>.
    /// </summary>
    private static byte[] Encode(string path, Encoding encoding, string text)
    {
        var strict = Encoding.GetEncoding(
            encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

        try
        {
            return strict.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            throw new UnrepresentableCharacterException(path, encoding.WebName, ex.CharUnknown);
        }
    }

    // The parser's rule: an `encoding` line in the ASCII prefix, else the BOM, else UTF-8.
    private static (Encoding Encoding, int BomLength) Resolve(byte[] bytes)
    {
        int bomLength = EncodingResolver.BomLength(bytes, out var bomEncoding);
        return (EncodingResolver.DetectEncoding(bytes, bomLength) ?? bomEncoding ?? EncodingResolver.Default, bomLength);
    }

    // GetBytes never emits a preamble, so the original BOM (or its absence) is what carries through.
    private static byte[] WithBom(byte[] bom, byte[] payload)
    {
        var bytes = new byte[bom.Length + payload.Length];
        bom.CopyTo(bytes, 0);
        payload.CopyTo(bytes, bom.Length);
        return bytes;
    }

    // True when the encoding writes `encoding <name>` as plain ASCII bytes, so the directive scan can find it.
    private static bool IsAsciiCompatible(Encoding encoding) =>
        encoding.GetBytes("encoding").AsSpan().SequenceEqual("encoding"u8);
}
