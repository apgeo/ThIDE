using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class StationRenumberTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "renumber");
    private static string P(string n) => Path.GetFullPath(Path.Combine(Root, n));

    // Invented data: a trip survey linked into a cave by an equate in another file.
    private const string Trip = "survey trip\n  centreline\n    data normal from to length compass clino\n    1 2 3.0 10 0\n    2 3 4.0 20 0\n    3 - 1.0 0 0\n  endcentreline\nendsurvey\n";
    private const string Main = "survey cave\n  input trip.th\n  centreline\n    data normal from to length compass clino\n    A1 A2 5.0 0 0\n  endcentreline\n  equate 1@trip A2\nendsurvey\n";

    private static (WorkspaceSemanticModel, Dictionary<string, string>) Build(string trip = Trip, string main = Main)
    {
        var files = new Dictionary<string, string> { [P("main.th")] = main, [P("trip.th")] = trip };
        var parsed = files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value));
        return (WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>()), files);
    }

    private static string Applied(Dictionary<string, string> files, EditBatch batch, string file) =>
        batch.Edits.FirstOrDefault(g => g.FilePath == P(file)) is { } g ? TextSplicer.Splice(files[P(file)], g.Edits) : files[P(file)];

    [Theory]
    [InlineData("", "A", 0, "1", "A1")]
    [InlineData("A", "B", 0, "A7", "B7")]
    [InlineData("A", "", 0, "A7", "7")]
    [InlineData("", "", 100, "7", "107")]
    [InlineData("", "", 1, "A07", null)]   // no number right after the (empty) prefix
    [InlineData("A", "A", 1, "A07", "A08")] // zero padding kept
    [InlineData("B", "C", 0, "A7", null)]
    [InlineData("", "", -10, "5", null)]    // would go negative
    public void Rule(string oldPrefix, string newPrefix, int offset, string name, string? expected) =>
        Assert.Equal(expected, new RenumberRule(oldPrefix, newPrefix, offset).Apply(name));

    [Fact]
    public void Prefixes_every_occurrence_including_at_references_in_other_files()
    {
        var (ws, files) = Build();
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, files.GetValueOrDefault);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        Assert.Equal(3, plan.Renames.Count);
        Assert.Contains("T1 T2 3.0 10 0\n    T2 T3 4.0 20 0\n    T3 - 1.0", Applied(files, plan.Batch, "trip.th"));
        Assert.Contains("equate T1@trip A2", Applied(files, plan.Batch, "main.th"));
        Assert.Contains("A1 A2 5.0", Applied(files, plan.Batch, "main.th"));   // the other survey is untouched
    }

    [Fact]
    public void Shifting_numbers_that_overlap_is_fine_all_edits_apply_at_once()
    {
        var (ws, files) = Build();
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "", 1), false, files.GetValueOrDefault);

        Assert.True(plan.Ok);
        Assert.Contains("2 3 3.0 10 0\n    3 4 4.0 20 0", Applied(files, plan.Batch, "trip.th"));
    }

    [Fact]
    public void A_clash_with_a_station_that_stays_refuses_the_plan()
    {
        // 'A3' has no number after the empty prefix, so it stays — and 2 + 1 with prefix A lands on it.
        var (ws, files) = Build(trip: Trip.Replace("    3 - 1.0 0 0\n", "    3 - 1.0 0 0\n    3 A3 1.0 0 0\n"));
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "A", 1), false, files.GetValueOrDefault);
        Assert.False(plan.Ok);
        Assert.Contains(plan.Problems, p => p.Kind is RenumberProblemKind.Collides or RenumberProblemKind.Duplicate);
        Assert.True(plan.Batch.IsEmpty);
    }

    [Fact]
    public void A_file_that_changed_since_the_model_was_built_refuses_the_plan()
    {
        var (ws, files) = Build();
        files[P("trip.th")] = "# edited\n" + files[P("trip.th")];
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, files.GetValueOrDefault);

        Assert.Contains(plan.Problems, p => p.Kind == RenumberProblemKind.Stale);
        Assert.True(plan.Batch.IsEmpty);
    }

    [Fact]
    public void Invalid_names_and_unknown_surveys_are_reported()
    {
        var (ws, files) = Build();
        Assert.Contains(StationRenumber.Plan(ws, "trip", new RenumberRule("", "a@"), false, files.GetValueOrDefault).Problems, p => p.Kind == RenumberProblemKind.InvalidName);
        Assert.Equal(RenumberProblemKind.NoSurvey, StationRenumber.Plan(ws, "nope", new RenumberRule("", "a"), false, files.GetValueOrDefault).Problems.Single().Kind);
    }

    [Fact]
    public void Sub_surveys_only_when_asked()
    {
        // Survey names are file-local: a sub-survey is one nested in the same file.
        var nested = "survey cave\n  survey sub\n    centreline\n      data normal from to length compass clino\n      1 2 1 0 0\n    endcentreline\n  endsurvey\n"
                   + "  centreline\n    data normal from to length compass clino\n    A1 A2 5.0 0 0\n  endcentreline\n  equate 1@sub A2\nendsurvey\n";
        var (ws, files) = Build(main: nested);
        var only = StationRenumber.Plan(ws, "cave", new RenumberRule("", "Z"), false, files.GetValueOrDefault);
        Assert.Equal(2, only.Renames.Count);   // A1, A2 — not the sub-survey's
        var all = StationRenumber.Plan(ws, "cave", new RenumberRule("", "Z"), true, files.GetValueOrDefault);
        Assert.Equal(4, all.Renames.Count);
        Assert.Contains("equate Z1@sub ZA2", Applied(files, all.Batch, "main.th"));
    }
}
