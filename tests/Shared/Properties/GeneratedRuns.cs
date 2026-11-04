// Fixed-seed runner for the hand-rolled generated-input tests. A run is deterministic: the seeds are
// baseSeed, baseSeed + 1, ... A failure names its seed, so it can be replayed on its own with
// THIDE_PROPERTY_SEED=<seed> THIDE_PROPERTY_RUNS=1. THIDE_PROPERTY_RUNS alone raises the case count.

using Xunit.Sdk;

namespace Therion.TestSupport;

internal static class GeneratedRuns
{
    /// <summary>How many cases a property runs: the fixed default, or THIDE_PROPERTY_RUNS when set.</summary>
    public static int Count(int defaultRuns) =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_RUNS"), out var n) && n > 0 ? n : defaultRuns;

    /// <summary>The first seed: THIDE_PROPERTY_SEED when set, else <paramref name="defaultSeed"/>.</summary>
    public static int FirstSeed(int defaultSeed) =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : defaultSeed;

    /// <summary>Runs <paramref name="check"/> once per seed; a failure is rethrown with its seed.</summary>
    public static void ForEachSeed(int defaultSeed, int defaultRuns, Action<int> check)
    {
        int first = FirstSeed(defaultSeed), runs = Count(defaultRuns);
        for (int i = 0; i < runs; i++)
        {
            int seed = first + i;
            try { check(seed); }
            catch (Exception ex)
            {
                throw new XunitException(
                    $"seed {seed} (replay: THIDE_PROPERTY_SEED={seed} THIDE_PROPERTY_RUNS=1): {ex.Message}");
            }
        }
    }

    /// <summary>A standard-normal sample (Box-Muller).</summary>
    public static double Gaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
