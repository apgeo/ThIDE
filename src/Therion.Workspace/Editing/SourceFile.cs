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
public class UnrepresentableCharacterException : Exception
{
    public UnrepresentableCharacterException(string path, string encodingName, char character)
        : this(encodingName, character, $"'{character}' (U+{(int)character:X4}) cannot be written in {encodingName}, "
             + $"which '{path}' declares. Writing it would replace the character with '?'.") { }

    protected UnrepresentableCharacterException(string encodingName, char character, string message) : base(message)
    {
        Character = character;
        EncodingName = encodingName;
    }

    public char Character { get; }
    public string EncodingName { get; }
}

/// <summary>
/// The file held bytes its encoding could not decode, read as U+FFFD, and the new text still holds
/// one. Writing it would put U+FFFD where those bytes were, so the original would be gone for good.
/// </summary>
public sealed class UndecodableBytesException(string path, string encodingName)
    : UnrepresentableCharacterException(encodingName, '\uFFFD',
        $"'{path}' holds bytes that are not valid {encodingName} (read as '\uFFFD'). Writing it would "
      + "replace them for good. Declare the file's real encoding with an `encoding` line, or retype the replaced characters.");

/// <param name="Sha256">Lowercase hex digest of the file's bytes, for optimistic concurrency.</param>
public sealed record SourceFile(string Path, string Text, string Sha256, Encoding Encoding, byte[] Bom)
{
    /// <summary>
    /// Some bytes were not valid in <see cref="Encoding"/> and <see cref="Text"/> holds U+FFFD in their
    /// place. A write of text that still holds U+FFFD is refused (<see cref="UndecodableBytesException"/>).
    /// </summary>
    public bool Lossy { get; init; }
}

/// <summary>How a text file was stored: its encoding and its byte-order mark (empty when none).</summary>
public sealed record TextFileFormat(Encoding Encoding, byte[] Bom)
{
    /// <summary>As <see cref="SourceFile.Lossy"/>: the text read in this format stands in for bytes it could not decode.</summary>
    public bool Lossy { get; init; }

    /// <summary>UTF-8 without a BOM — Therion's default, and what a new file is written as.</summary>
    public static TextFileFormat Default { get; } = new(EncodingResolver.Default, []);
}

public static class SourceFileIo
{
    /// <summary>The format a file was read in.</summary>
    public static TextFileFormat FormatOf(SourceFile file) => new(file.Encoding, file.Bom) { Lossy = file.Lossy };

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
        return new TextFileFormat(encoding, bomEncoding?.CodePage == encoding.CodePage ? readAs.Bom : []) { Lossy = readAs.Lossy };
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
    /// <remarks>The format returned is not <see cref="TextFileFormat.Lossy"/>: the bytes it goes with decode cleanly.</remarks>
    /// <exception cref="UnrepresentableCharacterException">The text holds a character the encoding cannot express.</exception>
    /// <exception cref="UndecodableBytesException">The text was read lossily and still holds U+FFFD.</exception>
    public static (byte[] Bytes, TextFileFormat Format) EncodeForSave(string path, string text, TextFileFormat? readAs)
    {
        var format = FormatForSave(text, readAs ?? FormatOnDisk(path));
        var bytes = WithBom(format.Bom, Encode(path, format.Encoding, text, format.Lossy));
        return (bytes, format.Lossy ? format with { Lossy = false } : format);
    }

    /// <summary>The format <paramref name="path"/> is stored in, or <see cref="TextFileFormat.Default"/> when it is not there.</summary>
    public static TextFileFormat FormatOnDisk(string path)
    {
        if (!File.Exists(path)) return TextFileFormat.Default;
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength) = Resolve(bytes);
        return new TextFileFormat(encoding, bytes[..bomLength]) { Lossy = Decode(encoding, bytes, bomLength).Lossy };
    }

    /// <summary>Reads a file, resolving its encoding the way the parser does.</summary>
    public static SourceFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength) = Resolve(bytes);
        var (text, lossy) = Decode(encoding, bytes, bomLength);

        return new SourceFile(
            Path: path,
            Text: text,
            Sha256: Digest(bytes),
            Encoding: encoding,
            Bom: bytes[..bomLength]) { Lossy = lossy };
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
    /// <exception cref="UndecodableBytesException">The file was read lossily and the text still holds U+FFFD.</exception>
    public static void Write(SourceFile file, string text)
    {
        WriteOver(file.Path, WithBom(file.Bom, Encode(file.Path, file.Encoding, text, file.Lossy)));
    }

    /// <summary>
    /// Creates a new file in the encoding its own <c>encoding</c> line declares, else as UTF-8 without a
    /// BOM — the encoding Therion assumes by default — so it reads back as the text it was given. Fails if
    /// anything is already there: <see cref="FileMode.CreateNew"/> makes that check and the creation one
    /// indivisible operation, which <c>File.Exists</c> followed by a write is not.
    /// </summary>
    /// <exception cref="IOException">The path already exists, or the write failed (the file is then removed).</exception>
    /// <exception cref="UnrepresentableCharacterException">The content holds a character its declared encoding cannot express; nothing is created.</exception>
    public static void Create(string path, string content)
    {
        var bytes = EncodeForSave(path, content, TextFileFormat.Default).Bytes;

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (stream) stream.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            // The file is ours (CreateNew made it): a failed write must not leave it half-written.
            try { File.Delete(path); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }

    /// <summary>
    /// True when <paramref name="text"/> can be written in <paramref name="file"/>'s encoding, so a
    /// batch can refuse up front instead of failing part-way through.
    /// </summary>
    public static bool CanEncode(SourceFile file, string text)
    {
        try { Encode(file.Path, file.Encoding, text, file.Lossy); return true; }
        catch (UnrepresentableCharacterException) { return false; }
    }

    /// <summary>
    /// Writes bytes over a file, atomically: they go to a sibling temporary file that is then moved over
    /// the target, so a crash or a full disk leaves the original whole rather than truncated. Used for the
    /// write and for rolling it back. The file is replaced as an in-place write would change it: a
    /// symbolic link is written through (its target changes, the link stays), the file keeps its
    /// permissions (Unix mode, Windows attributes), and a read-only file refuses the write. A folder that
    /// refuses the temporary file gets the in-place write instead. A file that is not there is created.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The file is read-only, or its folder refuses a new file.</exception>
    public static void WriteOver(string path, byte[] bytes)
    {
        path = LinkTargetOf(path);
        if (!File.Exists(path)) { ReplaceThroughTemporary(path, bytes, mode: null); return; }

        // The move would replace a read-only file regardless of its mode: check the file is writable first.
        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
        UnixFileMode? mode = null;
        FileAttributes? attributes = null;
        if (OperatingSystem.IsWindows()) attributes = File.GetAttributes(path);
        else mode = File.GetUnixFileMode(path);
        try { ReplaceThroughTemporary(path, bytes, mode); }
        catch (UnauthorizedAccessException)
        {
            File.WriteAllBytes(path, bytes);   // the folder refused the temporary file; the file itself is writable
            return;
        }
        if (attributes is { } a) File.SetAttributes(path, a);
    }

    /// <summary>The file a symbolic link at <paramref name="path"/> finally points to, or <paramref name="path"/> itself.</summary>
    public static string LinkTargetOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget is null ? path : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException) { return path; }
    }

    private static void ReplaceThroughTemporary(string path, byte[] bytes, UnixFileMode? mode)
    {
        // Same directory as the target: File.Move across volumes is a copy, which is not atomic.
        var temporary = path + ".therion-write-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            // The file's own mode before it takes the file's place, so the text is never readable more widely.
            if (mode is { } m && !OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, m);
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
    /// encoding cannot hold stops the write instead of becoming <c>?</c>. After a lossy read, a U+FFFD
    /// left in the text stands for bytes that were never decoded, so it stops the write too.
    /// </summary>
    private static byte[] Encode(string path, Encoding encoding, string text, bool lossy)
    {
        if (lossy && text.Contains('\uFFFD')) throw new UndecodableBytesException(path, encoding.WebName);

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

    // Decodes strictly first, so bytes the encoding cannot read are noticed rather than quietly
    // becoming U+FFFD; the text is then a replacement decode. Its replacement is always U+FFFD, not the
    // encoding's own ('?' for ASCII), or Encode could not tell the lost bytes from a real '?'.
    private static (string Text, bool Lossy) Decode(Encoding encoding, byte[] bytes, int offset)
    {
        var strict = Encoding.GetEncoding(
            encoding.CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ExceptionFallback);
        try { return (strict.GetString(bytes, offset, bytes.Length - offset), false); }
        catch (DecoderFallbackException)
        {
            var replacing = Encoding.GetEncoding(
                encoding.CodePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("\uFFFD"));
            return (replacing.GetString(bytes, offset, bytes.Length - offset), true);
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
