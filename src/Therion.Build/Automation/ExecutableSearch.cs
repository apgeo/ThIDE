// Windows starts a program named without a folder by looking in the starting program's own folder and
// in its current directory before the system folders and PATH. ThIDE's current directory is often a
// project folder (a double-clicked .thconfig), so a bare "python" or "git" in a task could run a
// same-named program the project ships. This resolves the name the way Windows would, minus those
// two places, so the start is by full path or not at all.

namespace Therion.Build.Automation;

public static class ExecutableSearch
{
    /// <summary>
    /// The full path to start for <paramref name="fileName"/> on this platform. On Windows: see
    /// <see cref="ResolveOnWindows"/>, with the real PATH and system folders; null when the program is
    /// not found there. Elsewhere the name is returned unchanged.
    /// </summary>
    public static string? Resolve(string fileName, string? workingDirectory)
    {
        if (!OperatingSystem.IsWindows()) return fileName;
        return ResolveOnWindows(
            fileName,
            workingDirectory,
            Environment.GetEnvironmentVariable("PATH"),
            [Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows)],
            File.Exists);
    }

    /// <summary>
    /// Resolves <paramref name="fileName"/> with Windows rules, whatever the host platform (so the rules
    /// can be tested anywhere):
    /// <list type="bullet">
    /// <item>a full path (<c>C:\…</c>, <c>\\server\…</c>) is used as given;</item>
    /// <item>a relative path with a folder in it (<c>tools\build.cmd</c>) is taken relative to
    /// <paramref name="workingDirectory"/>, the folder the program is started in;</item>
    /// <item>a bare name is looked up in <paramref name="systemDirectories"/>, then in each folder of
    /// <paramref name="pathVariable"/> that is a full path. The current directory and a relative PATH
    /// entry such as <c>.</c> are never searched.</item>
    /// </list>
    /// A name without an extension gets <c>.exe</c>, as Windows does. Returns null when nothing exists.
    /// </summary>
    public static string? ResolveOnWindows(
        string fileName,
        string? workingDirectory,
        string? pathVariable,
        IReadOnlyList<string> systemDirectories,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(systemDirectories);
        ArgumentNullException.ThrowIfNull(fileExists);
        var name = (fileName ?? "").Trim().Trim('"');
        if (name.Length == 0) return null;
        // Windows appends .exe to a name without an extension; one ending in a period is taken as written.
        var candidate = HasExtension(name) || name.EndsWith('.') ? name : name + ".exe";

        if (IsFullyQualified(candidate)) return fileExists(candidate) ? candidate : null;
        if (candidate.IndexOfAny(Separators) >= 0 || candidate.Contains(':'))
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !IsFullyQualified(workingDirectory)) return null;
            var relative = Join(workingDirectory, candidate);
            return fileExists(relative) ? relative : null;
        }

        foreach (var dir in systemDirectories.Concat(SplitPath(pathVariable)))
        {
            if (string.IsNullOrWhiteSpace(dir) || !IsFullyQualified(dir)) continue;
            var full = Join(dir, candidate);
            if (fileExists(full)) return full;
        }
        return null;
    }

    private static readonly char[] Separators = ['\\', '/'];

    private static IEnumerable<string> SplitPath(string? pathVariable) =>
        string.IsNullOrEmpty(pathVariable)
            ? []
            : pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim('"'));

    /// <summary><c>C:\x</c>, <c>C:/x</c> or a UNC path <c>\\server\share</c>; not <c>C:x</c> (relative to
    /// drive C's current directory) and not <c>\x</c> (relative to the current drive).</summary>
    private static bool IsFullyQualified(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        || (path.Length >= 2 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/'));

    private static bool HasExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 && dot > name.LastIndexOfAny(Separators);
    }

    private static string Join(string dir, string name) => dir.TrimEnd(Separators) + "\\" + name;
}
