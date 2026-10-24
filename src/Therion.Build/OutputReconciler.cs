// Matches the outputs a thconfig asked for against the ones Therion's log says it wrote.
//
// The log's "writing <path> ... done" lines are the complete record of what a run produced: they
// name database exports, shapefile directories and everything else a directory scan by extension
// can miss, and they say whether each write finished. Therion echoes each path as the thconfig's
// `-o` gave it, relative to the working directory, so a plain path comparison is enough.

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
    /// write completed; one the log started but never finished is missing.
    /// </summary>
    public static OutputReconciliation Reconcile(IEnumerable<string> expected, IEnumerable<TherionLogOutputFile> logged)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var completedInOrder = new List<string>();
        foreach (var f in logged)
            if (f.Completed && completed.Add(Normalize(f.Path))) completedInOrder.Add(f.Path);

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

    // Separators as the log may print them on Windows, and a leading "./" a caller may add.
    private static string Normalize(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p;
    }
}
