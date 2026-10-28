using System.Globalization;

namespace Therion.Build.Automation;

/// <summary>The summary of a batch build: one aligned row per build, and the totals.</summary>
public static class BatchSummary
{
    public static IReadOnlyList<string> Lines(IReadOnlyList<BuildOutcome> outcomes, string? root = null)
    {
        string Name(BuildOutcome o)
        {
            var entry = root is null ? Path.GetFileName(o.Entry) : Path.GetRelativePath(root, o.Entry).Replace('\\', '/');
            return o.Label is { Length: > 0 } l ? $"{entry} ({l})" : entry;
        }
        string Result(BuildOutcome o) => o.Cancelled ? "cancelled" : o.Succeeded ? "ok" : $"FAILED ({o.ExitCode})";

        var rows = outcomes.Select(o => (Name: Name(o), Result: Result(o), o.Errors, o.Warnings,
            Time: o.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s", Outputs: o.Artifacts.Count)).ToList();
        int w = Math.Max(5, rows.Select(r => r.Name.Length).DefaultIfEmpty(0).Max());
        var lines = new List<string>
        {
            $"{"Entry".PadRight(w)}  {"Result",-12} {"Errors",6} {"Warnings",8} {"Time",9} {"Outputs",7}",
        };
        lines.AddRange(rows.Select(r => $"{r.Name.PadRight(w)}  {r.Result,-12} {r.Errors,6} {r.Warnings,8} {r.Time,9} {r.Outputs,7}"));
        int ok = outcomes.Count(o => o.Succeeded);
        lines.Add($"{ok} of {outcomes.Count} succeeded in {TimeSpan.FromTicks(outcomes.Sum(o => o.Elapsed.Ticks)).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s.");
        return lines;
    }
}
