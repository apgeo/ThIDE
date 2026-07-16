// Reading and writing a Therion source file without changing anything about it except the text.
//
// A .th file may be UTF-8, UTF-16, or Latin-1 declared by an `encoding` directive, and Therion reads
// it back with that same rule. Rewriting one as UTF-8 leaves the directive lying about the bytes,
// which corrupts every accented survey name in the file. So: decode with EncodingResolver, re-encode
// with whatever it resolved, and put the original byte-order mark back exactly as it was.

using System.Security.Cryptography;
using System.Text;
using Therion.Syntax;

namespace Therion.Mcp.Mutations;

/// <summary>
/// The new text holds a character the file's declared encoding cannot represent. Writing anyway would
/// substitute <c>?</c> for it, silently and irreversibly.
/// </summary>
public sealed class UnrepresentableCharacterException(string path, string encodingName, char character)
    : Exception($"'{character}' (U+{(int)character:X4}) cannot be written in {encodingName}, "
              + $"which '{path}' declares. Writing it would replace the character with '?'.")
{
    public char Character { get; } = character;
}

/// <param name="Sha256">Lowercase hex digest of the file's bytes, for optimistic concurrency.</param>
public sealed record SourceFile(string Path, string Text, string Sha256, Encoding Encoding, byte[] Bom);

public static class SourceFileIo
{
    /// <summary>Reads a file, resolving its encoding the way the parser does.</summary>
    public static SourceFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);

        int bomLength = EncodingResolver.BomLength(bytes, out var bomEncoding);
        var encoding = EncodingResolver.DetectEncoding(bytes, bomLength) ?? bomEncoding ?? EncodingResolver.Default;

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
        var payload = Encode(file, text);

        // GetBytes never emits a preamble, so the original BOM (or its absence) is what carries through.
        var bytes = new byte[file.Bom.Length + payload.Length];
        file.Bom.CopyTo(bytes, 0);
        payload.CopyTo(bytes, file.Bom.Length);

        WriteOver(file.Path, bytes);
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

    /// <summary>Writes bytes over an existing file, atomically. Used for the write and for rolling it back.</summary>
    public static void WriteOver(string path, byte[] bytes)
    {
        // Same directory as the target: File.Move across volumes is a copy, which is not atomic.
        var temporary = path + ".therion-mcp-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
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
    private static byte[] Encode(SourceFile file, string text)
    {
        var strict = Encoding.GetEncoding(
            file.Encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

        try
        {
            return strict.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            throw new UnrepresentableCharacterException(file.Path, file.Encoding.WebName, ex.CharUnknown);
        }
    }
}
