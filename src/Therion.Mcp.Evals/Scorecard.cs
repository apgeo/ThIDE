using System.Globalization;

namespace Therion.Mcp.Evals;

/// <summary>
/// The deterministic metrics of a run (MODEL-EVALS §Metrics). Pure: it turns a list of graded
/// <see cref="CaseRun"/>s into the numbers, and renders the MODEL-EVALS table row — no model, no I/O, so
/// the arithmetic is unit-tested (Program --self-test) rather than trusted.
/// </summary>
/// <param name="CallValidity">% of tool calls that were schema-valid against a real tool.</param>
/// <param name="TaskSuccess">% of cases whose end state passed its check.</param>
/// <param name="RepairAt3">% of Repair cases brought to lint-clean within ≤3 turns.</param>
/// <param name="QaExact">% of Qa cases answered with the library-computed value.</param>
/// <param name="MedianTokens">Median total tokens per case (endpoints that report usage).</param>
/// <param name="MedianWallMs">Median wall-clock per case, milliseconds.</param>
public sealed record Scorecard(
    double? CallValidity,
    double TaskSuccess,
    double? RepairAt3,
    double? QaExact,
    int MedianTokens,
    long MedianWallMs)
{
    public static Scorecard Compute(IReadOnlyList<CaseRun> runs)
    {
        var calls = runs.SelectMany(r => r.Calls).ToList();
        var repair = runs.Where(r => r.Case.Category == Category.Repair).ToList();
        var qa = runs.Where(r => r.Case.Category == Category.Qa).ToList();

        return new Scorecard(
            CallValidity: Ratio(calls.Count(c => c.SchemaValid), calls.Count),
            TaskSuccess: Ratio(runs.Count(r => r.Passed), runs.Count) ?? 0,
            // A repair only counts if it actually reached lint-clean AND stayed within the turn budget.
            RepairAt3: Ratio(repair.Count(r => r.Passed && r.Turns <= 3), repair.Count),
            QaExact: Ratio(qa.Count(r => r.Passed), qa.Count),
            MedianTokens: (int)Median(runs.Where(r => r.Tokens > 0).Select(r => (double)r.Tokens)),
            MedianWallMs: (long)Median(runs.Select(r => r.Wall.TotalMilliseconds)));
    }

    /// <summary>A MODEL-EVALS host-① table row (pipe-delimited).</summary>
    public string ToMarkdownRow(string runId, string model, string notes) =>
        $"| {runId} | {model} | {Pct(CallValidity)} | {Pct(TaskSuccess)} | {Pct(RepairAt3)} | "
        + $"{Pct(QaExact)} | {(MedianTokens > 0 ? MedianTokens.ToString(CultureInfo.InvariantCulture) : "n/a")} | "
        + $"{FormatWall(MedianWallMs)} | {notes} |";

    public string ToConsole() =>
        $"  call_validity   {Pct(CallValidity)}\n"
        + $"  task_success    {Pct(TaskSuccess)}\n"
        + $"  repair@3        {Pct(RepairAt3)}\n"
        + $"  qa_exact        {Pct(QaExact)}\n"
        + $"  tokens/task     {(MedianTokens > 0 ? MedianTokens.ToString(CultureInfo.InvariantCulture) : "n/a")}\n"
        + $"  wall/task       {FormatWall(MedianWallMs)}";

    /// <summary>Fraction, or null when the denominator is zero (rendered "n/a").</summary>
    private static double? Ratio(int num, int denom) => denom == 0 ? null : (double)num / denom;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static string Pct(double? ratio) =>
        ratio is { } r ? ((int)Math.Round(r * 100)).ToString(CultureInfo.InvariantCulture) + "%" : "n/a";

    private static string FormatWall(long ms) =>
        ms <= 0 ? "n/a" : ms >= 1000 ? (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s"
                                     : ms.ToString(CultureInfo.InvariantCulture) + "ms";
}
