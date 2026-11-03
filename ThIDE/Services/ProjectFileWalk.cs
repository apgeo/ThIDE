// Recursive file walks over a project folder that stay inside it. EnumerationOptions'
// RecurseSubdirectories follows directory symlinks (and Windows junctions), so a link in a shared
// project — `extra -> ../..`, `all -> /` — would let Replace in Files rewrite files elsewhere and make
// Go to File walk the whole disk. Skipping every ReparsePoint is not the answer: OneDrive and other cloud
// placeholders carry that attribute on Windows without being links.

using System;
using System.Collections.Generic;
using System.IO;

namespace ThIDE.Services;

public static class ProjectFileWalk
{
    private static readonly EnumerationOptions OneLevel = new() { IgnoreInaccessible = true };

    /// <summary>
    /// The files under <paramref name="root"/> that match <paramref name="pattern"/> (the same pattern
    /// syntax and hidden/system skipping as <see cref="Directory.EnumerateFiles(string, string, EnumerationOptions)"/>),
    /// without descending into a subdirectory that is a symlink or junction. Unreadable folders are
    /// skipped; the walk never throws once it has started.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Safe(() => Directory.EnumerateFiles(dir, pattern, OneLevel)))
                yield return file;
            foreach (var sub in Safe(() => Directory.EnumerateDirectories(dir, "*", OneLevel)))
                if (!IsLink(sub)) pending.Push(sub);
        }
    }

    private static bool IsLink(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null;
        }
        catch { return true; }   // cannot tell: do not follow it
    }

    // Materialized so a folder that vanishes or turns unreadable mid-walk costs its entries, not the walk.
    private static List<string> Safe(Func<IEnumerable<string>> list)
    {
        try { return new List<string>(list()); }
        catch { return []; }
    }
}
