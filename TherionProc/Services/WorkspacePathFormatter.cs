// Formats workspace file paths for display in the config dropdown, the workspace
// header and tab tooltips (#3, #8): relative to the root when inside it, full path
// otherwise, with middle-ellipsis truncation that always keeps the filename visible.

using System;
using System.IO;

namespace TherionProc.Services;

public static class WorkspacePathFormatter
{
    /// <summary>Default maximum display length before middle-ellipsis truncation kicks in.</summary>
    public const int DefaultMaxLength = 48;

    /// <summary>
    /// Display string for <paramref name="path"/>: relative to <paramref name="root"/> when the
    /// file lives inside the root, otherwise the absolute path. The result is truncated with a
    /// middle ellipsis (filename always preserved) once it exceeds <paramref name="maxLength"/>.
    /// </summary>
    public static string Display(string? root, string path, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;

        var text = Relativize(root, path);
        return Truncate(text, maxLength);
    }

    /// <summary>Relative path when inside <paramref name="root"/>, else the full path.</summary>
    public static string Relativize(string? root, string path)
    {
        if (string.IsNullOrEmpty(root)) return path;
        try
        {
            var full = Path.GetFullPath(path);
            var rootFull = Path.GetFullPath(root);
            var rel = Path.GetRelativePath(rootFull, full);
            // GetRelativePath returns "..\..." when outside the root — show the full path then.
            return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? full : rel;
        }
        catch { return path; }
    }

    /// <summary>
    /// Middle-ellipsis truncation that always keeps the final path segment (filename) visible:
    /// <c>"start\of\path…\filename.thconfig"</c>.
    /// </summary>
    public static string Truncate(string text, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;

        var fileName = LastSegment(text);
        const string ellipsis = "…";

        // If even the filename (plus ellipsis) doesn't fit, show a tail of the filename.
        if (fileName.Length + ellipsis.Length >= maxLength)
            return ellipsis + fileName[Math.Max(0, fileName.Length - (maxLength - ellipsis.Length))..];

        int headBudget = maxLength - fileName.Length - ellipsis.Length;
        var head = text[..headBudget];
        return head + ellipsis + fileName;
    }

    private static string LastSegment(string text)
    {
        int slash = text.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 && slash + 1 < text.Length ? text[(slash + 1)..] : text;
    }
}
