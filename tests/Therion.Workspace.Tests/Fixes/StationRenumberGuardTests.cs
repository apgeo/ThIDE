using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

/// <summary>
/// The guards of a renumber plan: names Therion cannot read, numbers that would go below zero, a file
/// that changed since the model was built, and the shape of the batch (one group per file, edits in order).
/// </summary>
public class StationRenumberGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "renumber-guards");
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

    private static RenumberPlan Plan(RenumberRule rule, string survey = "trip", bool sub = false, string trip = Trip, string main = Main)
    {
        var (ws, files) = Build(trip, main);
        return StationRenumber.Plan(ws, survey, rule, sub, files.GetValueOrDefault);
    }

    // ---- new names Therion cannot read -------------------------------------

    [Theory]
    [InlineData("-")]     // a name may not start with '-'
    [InlineData("a ")]    // a space inside the name
    [InlineData("a\t")]   // a tab inside the name
    [InlineData(".")]     // a structural character first
    [InlineData("@")]
    [InlineData(":")]
    [InlineData("/")]
    [InlineData("a.")]    // and in the middle
    [InlineData("a@")]
    [InlineData("a:")]
    [InlineData("a/")]
    [InlineData("a#")]    // not a Therion identifier character
    public void A_new_prefix_that_makes_an_invalid_name_refuses_the_plan(string prefix)
    {
        var plan = Plan(new RenumberRule("", prefix));

        Assert.False(plan.Ok);
        Assert.True(plan.Batch.IsEmpty);
        Assert.All(plan.Problems, p => Assert.Equal(RenumberProblemKind.InvalidName, p.Kind));
        Assert.Equal(["trip.1", "trip.2", "trip.3"], plan.Problems.Select(p => p.Station));
        Assert.Equal([prefix + "1", prefix + "2", prefix + "3"], plan.Problems.Select(p => p.Detail));
    }

    [Fact]
    public void A_dash_later_in_the_name_is_allowed()
    {
        var plan = Plan(new RenumberRule("", "a-"));
        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
    }

    [Fact]
    public void A_rule_that_would_leave_an_empty_name_leaves_the_station_alone()
    {
        Assert.Null(new RenumberRule("1", "").Apply("1"));
        var plan = Plan(new RenumberRule("1", ""));
        Assert.Empty(plan.Renames);
        Assert.Equal(["trip.1", "trip.2", "trip.3"], plan.Unchanged.Select(s => s.ToString()));
        Assert.False(plan.Ok);   // nothing to do is not a plan to apply
    }

    [Fact]
    public void A_rule_that_gives_the_same_name_is_no_rename()
    {
        Assert.Null(new RenumberRule("A", "A").Apply("A7"));
        Assert.Null(new RenumberRule("", "", 0).Apply("7"));
        Assert.True(new RenumberRule("A", "A").IsNoOp);
        Assert.False(new RenumberRule("A", "A", 1).IsNoOp);
        Assert.False(new RenumberRule("A", "B").IsNoOp);
    }

    // ---- the zero boundary of an offset --------------------------------------

    [Theory]
    [InlineData("1", -1, "0")]
    [InlineData("0", -1, null)]
    [InlineData("01", -1, "00")]
    [InlineData("10", -10, "00")]
    [InlineData("10", -11, null)]
    [InlineData("99", 1, "100")]   // padding never truncates
    [InlineData("7a", 1, "8a")]    // the suffix after the number stays
    public void An_offset_stops_at_zero(string name, int offset, string? expected) =>
        Assert.Equal(expected, new RenumberRule("", "", offset).Apply(name));

    [Fact]
    public void Shifting_down_to_zero_renames_and_below_zero_leaves_alone()
    {
        var trip = Trip.Replace("    1 2 3.0 10 0\n", "    0 1 2.0 0 0\n    1 2 3.0 10 0\n");
        var (ws, files) = Build(trip);

        // 1 -> 0 would collide with the 0 that stays (0 - 1 is below zero).
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "", -1), false, files.GetValueOrDefault);
        Assert.Equal(["trip.0"], plan.Unchanged.Select(s => s.ToString()));
        Assert.Contains(plan.Problems, p => p is { Kind: RenumberProblemKind.Collides, Detail: "0", Station: "trip.1" });
        Assert.True(plan.Batch.IsEmpty);

        var only = Plan(new RenumberRule("", "", -1));
        Assert.True(only.Ok, string.Join(", ", only.Problems));
        Assert.Equal([("trip.1", "0"), ("trip.2", "1"), ("trip.3", "2")], only.Renames.Select(r => (r.Station.ToString(), r.NewName)));
    }

    // ---- refusals ---------------------------------------------------------

    [Fact]
    public void Two_stations_that_would_get_the_same_name_are_a_duplicate()
    {
        // 9 + 1 and 09 + 1 are both "10": two stations would become one.
        var trip = Trip.Replace("    3 - 1.0 0 0\n", "    3 9 1.0 0 0\n    9 09 1.0 0 0\n");
        var plan = Plan(new RenumberRule("", "", 1), trip: trip);

        var problem = Assert.Single(plan.Problems);
        Assert.Equal(RenumberProblemKind.Duplicate, problem.Kind);
        Assert.Equal("10", problem.Detail);
        Assert.True(plan.Batch.IsEmpty);
    }

    [Fact]
    public void A_name_that_stays_in_another_survey_is_no_collision()
    {
        // The cave's A1 and A2 stay; trip's 1 and 2 become A1 and A2 in trip's own namespace.
        var plan = Plan(new RenumberRule("", "A"));
        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
    }

    [Fact]
    public void A_plan_with_no_renames_is_not_ok_even_without_problems()
    {
        var plan = Plan(new RenumberRule("Z", "Y"));
        Assert.Empty(plan.Problems);
        Assert.Empty(plan.Renames);
        Assert.Equal(3, plan.Unchanged.Count);
        Assert.False(plan.Ok);
    }

    [Fact]
    public void Renames_come_in_natural_order_and_unchanged_stations_are_listed()
    {
        var trip = Trip.Replace("    3 - 1.0 0 0\n", "    3 10 1.0 0 0\n    10 9 1.0 0 0\n    9 x 1.0 0 0\n");
        var plan = Plan(new RenumberRule("", "T", 1), trip: trip);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        Assert.Equal(["1", "2", "3", "9", "10"], plan.Renames.Select(r => r.Station.Last));
        Assert.Equal(["T2", "T3", "T4", "T10", "T11"], plan.Renames.Select(r => r.NewName));
        Assert.Equal(["trip.x"], plan.Unchanged.Select(s => s.ToString()));
    }

    // ---- stale files ------------------------------------------------------

    [Fact]
    public void An_unreadable_file_is_stale_and_nothing_is_edited()
    {
        var (ws, files) = Build();
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, f => f == P("main.th") ? null : files[f]);

        Assert.Equal([(RenumberProblemKind.Stale, P("main.th"))], plan.Problems.Select(p => (p.Kind, p.Detail)));
        Assert.True(plan.Batch.IsEmpty);   // not even trip.th, whose text is current
    }

    [Fact]
    public void A_file_cut_short_before_an_occurrence_is_stale()
    {
        var (ws, files) = Build();
        var cut = files[P("trip.th")][..files[P("trip.th")].IndexOf("    3 -", StringComparison.Ordinal)];
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, f => f == P("trip.th") ? cut : files[f]);

        Assert.Contains(plan.Problems, p => p.Kind == RenumberProblemKind.Stale && p.Detail == P("trip.th"));
        Assert.True(plan.Batch.IsEmpty);
    }

    [Fact]
    public void A_file_that_ends_right_after_an_occurrence_is_not_stale()
    {
        // The last occurrence of a trip station in trip.th is the 3 of "3 - 1.0"; text that stops
        // just after it still holds every occurrence where the model has it.
        var (ws, files) = Build();
        var text = files[P("trip.th")];
        var cut = text[..(text.IndexOf("    3 -", StringComparison.Ordinal) + "    3".Length)];
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, f => f == P("trip.th") ? cut : files[f]);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        var group = plan.Batch.Edits.Single(g => g.FilePath == P("trip.th"));
        Assert.EndsWith("    T3", TextSplicer.Splice(cut, group.Edits));
    }

    [Fact]
    public void Different_text_at_an_occurrence_is_stale()
    {
        // Same length, same offsets, but another name where the model saw "2".
        var (ws, files) = Build();
        var changed = files[P("trip.th")].Replace("    2 3 4.0", "    7 3 4.0");
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, f => f == P("trip.th") ? changed : files[f]);

        var stale = Assert.Single(plan.Problems);
        Assert.Equal((RenumberProblemKind.Stale, P("trip.th"), "trip.2"), (stale.Kind, stale.Detail, stale.Station));
        Assert.True(plan.Batch.IsEmpty);
    }

    // ---- the batch's shape -------------------------------------------------

    [Fact]
    public void The_batch_has_one_group_per_file_in_ordinal_order_with_edits_sorted_by_start()
    {
        var (ws, files) = Build();
        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "T"), false, files.GetValueOrDefault);

        Assert.True(plan.Ok);
        Assert.Equal([P("main.th"), P("trip.th")], plan.Batch.Edits.Select(g => g.FilePath));
        foreach (var group in plan.Batch.Edits)
        {
            var starts = group.Edits.Select(e => e.Start).ToList();
            Assert.Equal(starts.Order(), starts);
            Assert.Equal(starts.Distinct().Count(), starts.Count);   // an occurrence is edited once
        }
        Assert.Equal(5, plan.Batch.Edits.Single(g => g.FilePath == P("trip.th")).Edits.Count);   // 1, 2, 2, 3, 3
    }

    [Fact]
    public void Sub_surveys_of_another_survey_are_never_in_scope()
    {
        // `other.x` is as deep as `cave.sub` but is not under `cave`.
        const string text =
            "survey cave\n  survey sub\n    centreline\n      1 2 1 0 0\n    endcentreline\n  endsurvey\n" +
            "  centreline\n    1 2 1 0 0\n  endcentreline\nendsurvey\n" +
            "survey other\n  survey x\n    centreline\n      1 2 1 0 0\n    endcentreline\n  endsurvey\nendsurvey\n";
        var (ws, files) = Build(main: text, trip: "");
        var plan = StationRenumber.Plan(ws, "cave", new RenumberRule("", "Z"), true, files.GetValueOrDefault);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        Assert.Equal(["cave.1", "cave.2", "cave.sub.1", "cave.sub.2"], plan.Renames.Select(r => r.Station.ToString()).Order(StringComparer.Ordinal));
        var after = TextSplicer.Splice(text, plan.Batch.Edits.Single().Edits)!;
        Assert.EndsWith("survey other\n  survey x\n    centreline\n      1 2 1 0 0\n    endcentreline\n  endsurvey\nendsurvey\n", after);
    }
}

public class StationRenumberAtReferenceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "renumber-at");
    private static readonly string Main = Path.GetFullPath(Path.Combine(Root, "main.th"));

    // Invented data: a connecting shot written in the parent survey into its sub-survey.
    private const string Text =
        "survey cave\n  survey sub\n    centreline\n      1 2 1 0 0\n    endcentreline\n  endsurvey\n" +
        "  centreline\n    A1 A2 5 0 0\n    A2 2@sub 3 0 0\n  endcentreline\nendsurvey\n";

    private static RenumberPlan Plan(RenumberRule rule, bool sub)
    {
        var parsed = new[] { Main }.ToDictionary(p => p, p => new ThParser().Parse(p, Text));
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        return StationRenumber.Plan(ws, "cave", rule, sub, _ => Text);
    }

    [Fact]
    public void A_shot_into_a_sub_survey_is_not_a_station_of_the_parent()
    {
        var plan = Plan(new RenumberRule("", "T"), sub: false);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        Assert.Equal(["cave.A1", "cave.A2"], plan.Renames.Select(r => r.Station.ToString()));
        Assert.Empty(plan.Unchanged);
        Assert.EndsWith("    TA1 TA2 5 0 0\n    TA2 2@sub 3 0 0\n  endcentreline\nendsurvey\n", TextSplicer.Splice(Text, plan.Batch.Edits.Single().Edits));
    }

    [Fact]
    public void With_sub_surveys_the_reference_from_the_parent_is_renamed_too()
    {
        var plan = Plan(new RenumberRule("", "T"), sub: true);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        Assert.Equal("survey cave\n  survey sub\n    centreline\n      T1 T2 1 0 0\n    endcentreline\n  endsurvey\n" +
                     "  centreline\n    TA1 TA2 5 0 0\n    TA2 T2@sub 3 0 0\n  endcentreline\nendsurvey\n",
            TextSplicer.Splice(Text, plan.Batch.Edits.Single().Edits));
    }
}
