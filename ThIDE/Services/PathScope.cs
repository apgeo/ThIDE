// "Is this path inside that directory?" — used by the status-bar breadcrumb (workspace vs. OS
// file manager) and by the project audit's exclusion filter. Kept in one place so the two never
// disagree about what "inside" means.

using System;
using System.IO;

namespace ThIDE.Services;

public static class PathScope
{
    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="directory"/> itself or lives beneath it.
    /// Comparison follows the host filesystem (case-insensitive on Windows/macOS), and a malformed
    /// path is simply "not inside" rather than an exception.
    /// </summary>
    public static bool IsUnder(string? path, string? directory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory)) return false;
        try
        {
            var rel = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
            // GetRelativePath returns ".." (or an absolute path, across volumes) when it escapes.
            return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
        }
        catch { return false; }
    }
}
