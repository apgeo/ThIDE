// The app's own state files (settings, bookmarks, keyboard shortcuts) fall back to defaults when they
// cannot be read, and the next save then writes over them. This keeps the unreadable bytes first.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ThIDE.Services;

internal static class UnreadableStateFile
{
    // The services load before the main window exists; it reports what was kept once it is up.
    private static readonly ConcurrentQueue<string> Kept = new();

    /// <summary>
    /// Copies <paramref name="path"/> to <c>&lt;path&gt;.unreadable-yyyyMMdd-HHmmss</c> so the user's data
    /// survives the save that follows a failed load. Returns the copy's path, or null when there was
    /// nothing to copy or the copy failed.
    /// </summary>
    public static string? KeepAside(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var copy = path + ".unreadable-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Copy(path, copy, overwrite: true);
            Kept.Enqueue(copy);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The copies made since the last call, oldest first (each is reported once).</summary>
    public static IReadOnlyList<string> TakeKept()
    {
        var list = new List<string>();
        while (Kept.TryDequeue(out var copy)) list.Add(copy);
        return list;
    }
}
