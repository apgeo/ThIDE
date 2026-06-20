// Implementation Plan §3 — encoding directive handling.
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

    /// <summary>Decode <paramref name="bytes"/> honoring an embedded directive.</summary>
    public static string Decode(byte[] bytes)
    {
        var enc = DetectEncoding(bytes) ?? Default;
        return enc.GetString(bytes);
    }

    /// <summary>Detect the encoding declared by an <c>encoding &lt;name&gt;</c> directive, if any.</summary>
    public static Encoding? DetectEncoding(byte[] bytes)
    {
        // ASCII-decode just the probe prefix to find an `encoding` directive.
        int n = Math.Min(bytes.Length, ProbeBytes);
        string ascii = Encoding.ASCII.GetString(bytes, 0, n);

        int lineStart = 0;
        while (lineStart < ascii.Length)
        {
            int lineEnd = ascii.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = ascii.Length;

            var line = ascii.AsSpan(lineStart, lineEnd - lineStart).Trim();
            if (line.Length > 0 && line[0] != '#')
            {
                // Need an "encoding <name>" line — match case-insensitively.
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
