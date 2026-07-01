// guard against accidentally loading a binary blob or a pathologically huge file into
// the text editor (which would show garbage or hang the UI). Pure + testable: returns a reason
// string when a file should NOT be opened as text, or null when it's fine. The caller offers an
// explicit "Open anyway" path.

using System;
using System.IO;

namespace TherionProc.Services;

public static class FileGuard
{
    /// <summary>Hard cap (bytes) for loading a file as text — above this we refuse by default.</summary>
    public const long MaxTextBytes = 50L * 1024 * 1024;   // 50 MB

    /// <summary>Returns a human reason when <paramref name="path"/> should not be opened as text, else null.</summary>
    public static string? ShouldBlockTextOpen(string path)
    {
        long length;
        try { length = new FileInfo(path).Length; }
        catch { return null; }   // can't stat → let the normal open path deal with it

        if (length > MaxTextBytes)
            return $"file is {length / (1024 * 1024)} MB (over the {MaxTextBytes / (1024 * 1024)} MB open limit)";

        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8192];
            int read = fs.Read(head);
            if (read <= 0) return null;
            if (LooksBinary(head[..read])) return "looks like a binary file";
        }
        catch { /* unreadable header → don't block on that alone */ }
        return null;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> head)
    {
        // Text encodings with BOMs (UTF-8/16/32) are fine even if they contain NUL bytes.
        if (HasTextBom(head)) return false;
        // A NUL byte in the first chunk is the classic binary signal for single-byte/UTF-8 text.
        foreach (var b in head)
            if (b == 0x00) return true;
        return false;
    }

    private static bool HasTextBom(ReadOnlySpan<byte> h) =>
        (h.Length >= 3 && h[0] == 0xEF && h[1] == 0xBB && h[2] == 0xBF) ||   // UTF-8
        (h.Length >= 2 && h[0] == 0xFF && h[1] == 0xFE) ||                    // UTF-16 LE / UTF-32 LE
        (h.Length >= 2 && h[0] == 0xFE && h[1] == 0xFF);                      // UTF-16 BE
}
