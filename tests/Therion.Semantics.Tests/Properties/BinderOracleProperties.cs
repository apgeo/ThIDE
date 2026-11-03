// Generated cave -> Therion text -> parse + bind -> the same cave. A valid survey must reach the
// model with every shot, flag, unit, equate, fix and note intact, however it is written: units and
// reading orders, back sights, groups with local state, several centrelines and files, @ references,
// continuations, comments and every line-break style. Seeds are fixed; THIDE_PROPERTY_RUNS raises
// the case count. A failure is shrunk to the smallest cave and feature set that still fails.

using Therion.Semantics.Tests.Properties.CaveGen;

namespace Therion.Semantics.Tests.Properties;

public class BinderOracleProperties
{
    private const int CaveSeed = 71_000;

    private static int PrintSeed(int caveSeed) => caveSeed * 7 + 3;

    private static void AssertRecovered(IEnumerable<int> seeds, PrintFeatures features)
    {
        foreach (var seed in seeds)
        {
            var cave = CaveGenerator.Generate(seed);
            var files = CavePrinter.Print(CaveShrinker.Clone(cave), features, PrintSeed(seed));
            var problems = BinderOracle.Check(cave, files);
            if (problems.Count == 0) continue;

            var reduced = CaveShrinker.Shrink(cave, features, PrintSeed(seed), BinderOracle.Check);
            Assert.Fail(
                $"cave seed {seed} (print seed {PrintSeed(seed)}) is not recovered.\n" +
                $"Reduced to features: {reduced.Features}\n  " +
                string.Join("\n  ", reduced.Problems.Take(8)) + "\n" +
                BinderOracle.Dump(reduced.Files));
        }
    }

    [Fact]
    public void Binder_recovers_the_generated_cave_with_every_printing_choice()
    {
        AssertRecovered(Enumerable.Range(CaveSeed, PropertyRuns.Count(200)), PrintFeatures.All);
    }

    public static IEnumerable<object[]> FeatureNames() =>
        PrintFeatures.Names.Select(n => new object[] { n }).Prepend(new object[] { "(plain)" });

    // One printing family at a time: when the combined property fails, the row that fails here
    // names the feature to look at.
    [Theory]
    [MemberData(nameof(FeatureNames))]
    public void Binder_recovers_the_generated_cave_with_one_printing_choice(string feature)
    {
        var features = feature == "(plain)" ? PrintFeatures.None : PrintFeatures.None.With(feature, true);
        int offset = 10_000 + 1_000 * (PrintFeatures.Names.ToList().IndexOf(feature) + 1);
        AssertRecovered(Enumerable.Range(CaveSeed + offset, PropertyRuns.Count(40)), features);
    }

    [Fact]
    public void Binder_applies_calibrate_zero_errors_and_scales()
    {
        // Metres and degrees only, so the calibration formula, (read - zero error) * scale, is unambiguous.
        AssertRecovered(Enumerable.Range(CaveSeed + 50_000, PropertyRuns.Count(40)), PrintFeatures.None with { Calibration = true });
    }

    [Fact]
    public void Printing_the_same_cave_twice_gives_the_same_text()
    {
        // The generator and printer are deterministic, so a reported seed reproduces the failure.
        for (int seed = CaveSeed; seed < CaveSeed + 20; seed++)
        {
            var a = CavePrinter.Print(CaveGenerator.Generate(seed), PrintFeatures.All, PrintSeed(seed));
            var b = CavePrinter.Print(CaveGenerator.Generate(seed), PrintFeatures.All, PrintSeed(seed));
            Assert.Equal(a.Select(f => (f.Path, f.Text)), b.Select(f => (f.Path, f.Text)));
        }
    }

    [Fact]
    public void The_oracle_notices_a_shot_that_is_not_in_the_text()
    {
        // Guard against an oracle that passes everything: drop one printed row and expect a complaint.
        var cave = CaveGenerator.Generate(CaveSeed);
        var files = CavePrinter.Print(CaveShrinker.Clone(cave), PrintFeatures.None, PrintSeed(CaveSeed));
        var main = files[0];
        var lines = main.Text.Split('\n').ToList();
        var keywords = new[] { "survey", "endsurvey", "centreline", "endcentreline", "data", "fix", "station", "equate", "input" };
        int row = lines.FindIndex(l => l.Trim().Split(' ') is { Length: >= 5 } words && !keywords.Contains(words[0]));
        Assert.True(row >= 0, "the printed cave has a data row");
        lines.RemoveAt(row);
        files[0] = main with { Text = string.Join('\n', lines) };
        Assert.NotEmpty(BinderOracle.Check(cave, files));
    }
}
