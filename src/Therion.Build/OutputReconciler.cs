// Matches the outputs a thconfig asked for against the ones Therion's log says it wrote.
//
// The log's "writing <path> ... done" lines are the complete record of what a run produced: they
// name database exports, shapefile directories and everything else a directory scan by extension
// can miss, and they say whether each write finished. Therion echoes each path as the thconfig's
// `-o` gave it — but resolved against the configuration file's directory when Therion was given
// the configuration by a full path (TherionCompiler always does), so logged paths are made
// relative to that directory before they are compared.

namespace Therion.Build;

/// <summary>
/// The verdict per output: <see cref="Produced"/> and <see cref="Missing"/> partition the expected
/// paths (in the order given); <see cref="Unexpected"/> lists finished writes nobody asked for.
/// </summary>
public sealed record OutputReconciliation(
    IReadOnlyList<string> Produced,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unexpected)
{
    public bool AllProduced => Missing.Count == 0;
}

public static class OutputReconciler
{
    /// <summary>
    /// Reconciles <paramref name="expected"/> output paths (relative to the compile's working
    /// directory, as written after <c>-o</c>) with <paramref name="logged"/>, normally
    /// <see cref="TherionLogSummary.OutputFiles"/>. An output counts as produced only when its
    /// write completed; one the log started but never finished is missing. A logged full path under
    /// <paramref name="workingDirectory"/> (the configuration file's directory) is compared by its
    /// path relative to it, and is listed that way in <see cref="OutputReconciliation.Unexpected"/>.
    /// </summary>
    public static OutputReconciliation Reconcile(
        IEnumerable<string> expected,
        IEnumerable<TherionLogOutputFile> logged,
        string? workingDirectory = null)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var completedInOrder = new List<string>();
        foreach (var f in logged)
        {
            if (!f.Completed) continue;
            var path = RelativeTo(f.Path, workingDirectory);
            if (completed.Add(Normalize(path))) completedInOrder.Add(path);
        }

        var produced = new List<string>();
        var missing = new List<string>();
        var asked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in expected)
        {
            var key = Normalize(e);
            if (!asked.Add(key)) continue;
            (completed.Contains(key) ? produced : missing).Add(e);
        }

        var unexpected = completedInOrder.Where(p => !asked.Contains(Normalize(p))).ToList();
        return new OutputReconciliation(produced, missing, unexpected);
    }

    private static string RelativeTo(string path, string? workingDirectory)
    {
        if (workingDirectory is null || !Path.IsPathRooted(path)) return path;
        var relative = Path.GetRelativePath(workingDirectory, path);
        relative = relative.Replace('\\', '/');
        return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative;
    }

    // Separators as the log may print them on Windows, and a leading "./" a caller may add.
    private static string Normalize(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p;
    }
}
