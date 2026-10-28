namespace Therion.Workspace.Editing;

/// <summary>
/// Comparing file paths the way the file system does: case-sensitive on Linux, case-insensitive on
/// Windows and macOS (their default file systems). Paths are made absolute first.
/// </summary>
public static class FilePaths
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool Same(string a, string b) => Comparer.Equals(Full(a), Full(b));

    public static string Full(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
