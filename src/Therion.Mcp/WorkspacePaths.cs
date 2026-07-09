// The path jail (02 §B.6 rule 2). Every path an MCP tool accepts from a model passes through here
// before it reaches the filesystem. A .th file authored by a stranger can carry prompt injection, so
// "the model asked for it" is never authorization to read or write outside the workspace root.

using System.Diagnostics.CodeAnalysis;

namespace Therion.Mcp;

public static class WorkspacePaths
{
    /// <summary>
    /// Case sensitivity of the comparison used to test containment. Assume case-sensitive everywhere
    /// but Windows (CLAUDE.md): on a case-insensitive volume this only ever over-rejects, never
    /// under-rejects, which is the safe direction for a jail.
    /// </summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Resolves <paramref name="path"/> — workspace-relative, or absolute inside the workspace —
    /// to a canonical absolute path under <paramref name="root"/>, with symlinks followed.
    /// </summary>
    /// <returns><c>true</c> when the path is inside the jail; otherwise <c>false</c> and
    /// <paramref name="error"/> explains why, in terms a model can act on.</returns>
    public static bool TryResolve(
        string root,
        string? path,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out string? error)
    {
        fullPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        // A NUL byte truncates the path inside some native filesystem calls; reject before canonicalizing.
        if (path.Contains('\0'))
        {
            error = "Path contains a NUL character.";
            return false;
        }

        string canonicalRoot;
        try
        {
            canonicalRoot = Canonicalize(root);
        }
        catch (Exception ex)
        {
            error = $"Workspace root is not a usable path: {ex.Message}";
            return false;
        }

        string candidate;
        try
        {
            // Path.Combine ignores `root` entirely when `path` is rooted, which is exactly the case the
            // containment check below must catch — so combine first, then test.
            candidate = Canonicalize(Path.Combine(canonicalRoot, path));
        }
        catch (Exception ex)
        {
            error = $"Not a usable path: {ex.Message}";
            return false;
        }

        if (!IsInside(canonicalRoot, candidate))
        {
            error = $"'{path}' resolves outside the workspace root.";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>The workspace-relative, forward-slashed form of <paramref name="fullPath"/> for the wire.</summary>
    public static string ToRelative(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="root"/> itself or lies beneath it.</summary>
    public static bool IsInside(string root, string candidate)
    {
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (candidate.Equals(root, PathComparison)) return true;
        if (!candidate.StartsWith(root, PathComparison)) return false;

        // Guard the "/work" vs "/workspace-of-someone-else" prefix collision.
        char boundary = candidate[root.Length];
        return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
    }

    /// <summary>
    /// Absolute path with <c>..</c> collapsed and every symlink resolved. Each component is resolved
    /// in turn, not just the leaf: a directory symlink halfway down the path is exactly how a
    /// jail that only inspects the final segment gets walked out of. Components that do not exist
    /// yet (an apply-step's target file) are appended unresolved, so the check still binds them to
    /// the real location of the directory that will hold them.
    /// </summary>
    private static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);

        var prefix = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(prefix)) return full; // not rooted — GetFullPath already gave up

        var segments = full[prefix.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var current = prefix;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (Path.Exists(current)) current = ResolveLinks(current);
        }

        return Path.GetFullPath(current);
    }

    /// <summary>Follows a symlink to its final target, tolerating cycles and broken links.</summary>
    private static string ResolveLinks(string existingPath)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(existingPath)
                ? new DirectoryInfo(existingPath)
                : new FileInfo(existingPath);

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? info.FullName;
        }
        catch (IOException)
        {
            // Cyclic or too-deep link chain: fall back to the un-followed path, which the caller then
            // jails as usual. Refusing here would be a denial of service on a merely odd tree.
            return Path.GetFullPath(existingPath);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(existingPath);
        }
    }
}
