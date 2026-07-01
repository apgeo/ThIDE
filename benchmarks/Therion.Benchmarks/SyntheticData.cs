// Shared synthetic-survey generators for the performance baselines.
// Mirrors the shapes used by the existing perf smoke tests (PerformanceSmokeTests,
// SemanticPerfTests) so benchmark numbers line up with the in-repo budgets.

using System.Text;

namespace Therion.Benchmarks;

internal static class SyntheticData
{
    /// <summary>
    /// A single-survey centreline with <paramref name="legCount"/> shots
    /// (s0→s1→…→s{legCount}). Compass/clino stay in range so no spurious diagnostics fire.
    /// </summary>
    public static string Centreline(int legCount, string surveyName = "big")
    {
        var sb = new StringBuilder(legCount * 32);
        sb.Append("survey ").Append(surveyName).Append('\n');
        sb.Append("  centreline\n");
        sb.Append("    data normal from to length compass clino\n");
        for (int i = 0; i < legCount; i++)
        {
            sb.Append("      s").Append(i).Append(" s").Append(i + 1)
              .Append(" 10.0 ").Append(i % 360)
              .Append(' ').Append((i % 179) - 89)
              .Append('\n');
        }
        sb.Append("  endcentreline\n");
        sb.Append("endsurvey\n");
        return sb.ToString();
    }

    /// <summary>
    /// A multi-file project: <paramref name="fileCount"/> independent survey files of
    /// <paramref name="legsPerFile"/> shots each. Returns (path, sourceText) pairs.
    /// </summary>
    public static (string Path, string Text)[] Project(int fileCount, int legsPerFile)
    {
        var files = new (string, string)[fileCount];
        for (int f = 0; f < fileCount; f++)
            files[f] = ($"survey{f}.th", Centreline(legsPerFile, $"survey{f}"));
        return files;
    }
}
