// Implementation Plan �3 � encoding directive handling.
// A two-phase decoder: scan the ASCII prefix for an `encoding <name>` line,
// then re-decode the entire byte buffer with that encoding. Falls back to UTF-8.

using System;
using System.IO;
using System.Text;

namespace Therion.Syntax;

/// <summary>
/// Decodes a Therion source file honoring an optional <c>encoding &lt;name&gt;</c>
/// directive at the top of the file.
/// </summary>
public static class EncodingResolver
{
    /// <summary>How many bytes to scan looking for the encoding directive.</summary>
    public const int ProbeBytes = 4 * 1024;

    /// <summary>Default fallback encoding when no directive is present.</summary>
    public static Encoding Default { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Read <paramref name="filePath"/> from disk and decode honoring an embedded directive.</summary>
    public static string ReadAllText(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return Decode(bytes);
    }

    /// <summary>
    /// Decode <paramref name="bytes"/> honoring an embedded directive. A leading byte-order mark
    /// (UTF-8 / UTF-16) is detected and stripped so it never leaks into the first token (LANG-11).
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        int skip = BomLength(bytes, out var bomEncoding);
        var enc = DetectEncoding(bytes, skip) ?? bomEncoding ?? Default;
        return enc.GetString(bytes, skip, bytes.Length - skip);
    }

    /// <summary>Length (0/2/3) of a leading BOM, and the encoding it implies (if any).</summary>
    public static int BomLength(byte[] bytes, out Encoding? encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { encoding = Default; return 3; }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { encoding = Encoding.Unicode; return 2; }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { encoding = Encoding.BigEndianUnicode; return 2; }
        encoding = null;
        return 0;
    }

    /// <summary>Detect the encoding declared by an <c>encoding &lt;name&gt;</c> directive, if any.</summary>
    public static Encoding? DetectEncoding(byte[] bytes) => DetectEncoding(bytes, 0);

    /// <summary>As <see cref="DetectEncoding(byte[])"/> but starting after a stripped BOM.</summary>
    public static Encoding? DetectEncoding(byte[] bytes, int startOffset)
    {
        // ASCII-decode just the probe prefix (after any BOM) to find an `encoding` directive.
        int n = Math.Min(bytes.Length, startOffset + ProbeBytes) - startOffset;
        if (n <= 0) return null;
        string ascii = Encoding.ASCII.GetString(bytes, startOffset, n);

        int lineStart = 0;
        while (lineStart < ascii.Length)
        {
            int lineEnd = ascii.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = ascii.Length;

            var line = ascii.AsSpan(lineStart, lineEnd - lineStart).Trim();
            if (line.Length > 0 && line[0] != '#')
            {
                // Need an "encoding <name>" line � match case-insensitively.
                if (line.Length >= 9 && line[..8].ToString().Equals("encoding", StringComparison.OrdinalIgnoreCase)
                    && char.IsWhiteSpace(line[8]))
                {
                    var name = line[8..].Trim().ToString();
                    return TryGetEncoding(name);
                }
                // First non-comment, non-encoding line: stop looking.
                return null;
            }

            lineStart = lineEnd + 1;
        }
        return null;
    }

    private static Encoding? TryGetEncoding(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            // Therion historically uses names like "utf-8", "iso-8859-1", "cp1250".
            // Register the code-pages provider once so legacy names resolve on .NET.
            EnsureCodePagesRegistered();
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return null;
        }
    }

    private static int _codePagesRegistered;
    private static void EnsureCodePagesRegistered()
    {
        if (System.Threading.Interlocked.Exchange(ref _codePagesRegistered, 1) == 0)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
