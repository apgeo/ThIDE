// Renumbering a generated cave: a random rule over a random survey either is refused exactly when two
// stations of one survey would share a name (or a name is invalid), or, once applied, gives back the
// same cave with the stations renamed: every shot, equate class, fix, note and @ reference, checked by
// the binder oracle of the Semantics tests. A rule whose inverse is exact restores the bytes.
// Seeds are fixed; THIDE_PROPERTY_RUNS raises the case count. A failure names its seed.

using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Tests.Properties.CaveGen;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class StationRenumberTopologyProperties
{
    private const int FirstSeed = 18_000;

    private static readonly string[] OldPrefixes = ["", "", "", "p", "a", "0", "e", "end", "x"];
    private static readonly string[] NewPrefixes = ["", "", "T", "x_", "p", "Q-", "e", "0"];
    private static readonly int[] Offsets = [0, 0, 1, -1, 2, 1000, -1000, 7, -3];

    [Fact]
    public void Renumbering_is_refused_exactly_when_names_clash_and_otherwise_keeps_the_cave()
    {
        int applied = 0, refused = 0, inverted = 0;
        foreach (var seed in Enumerable.Range(FirstSeed, PropertyRuns.Count(200)))
        {
            var outcome = Check(seed);
            if (outcome.Problem is { } problem) Assert.Fail($"[seed {seed}] {outcome.Rule}: {problem}\n{outcome.Dump}");
            if (outcome.Applied) applied++;
            if (outcome.Refused) refused++;
            if (outcome.Inverted) inverted++;
        }
        // The generator must reach both branches and the inverse often enough for the property to mean something.
        Assert.True(applied >= 40 && refused >= 10 && inverted >= 20, $"applied {applied}, refused {refused}, inverted {inverted}");
    }

    [Fact]
    public void Zero_padded_names_that_meet_after_a_shift_are_refused()
    {
        // 9 and 09 both become 10 under +1 in one survey: two stations would be merged.
        var trip = "survey trip\n  centreline\n    data normal from to length compass clino\n    9 09 1 0 0\n    09 1 2 0 0\n  endcentreline\nendsurvey\n";
        var path = Path.Combine(CavePrinter.Folder, "pad.th");
        var parsed = new[] { path }.ToDictionary(p => p, p => new ThParser().Parse(p, trip));
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());

        var plan = StationRenumber.Plan(ws, "trip", new RenumberRule("", "", 1), false, _ => trip);

        Assert.Contains(plan.Problems, p => p is { Kind: RenumberProblemKind.Duplicate, Detail: "10" });
        Assert.True(plan.Batch.IsEmpty);
    }

    private sealed record Outcome(string Rule, string? Problem = null, string Dump = "", bool Applied = false, bool Refused = false, bool Inverted = false);

    private static Outcome Check(int seed)
    {
        var rnd = new SeededRandom(seed * 31 + 7);
        var cave = CaveGenerator.Generate(seed);
        var files = CavePrinter.Print(CaveShrinker.Clone(cave), PrintFeatures.All, seed);
        var before = BinderOracle.Check(cave, files);
        if (before.Count > 0) return new Outcome("(none)", "the printed cave is not recovered before any renumber: " + before[0], BinderOracle.Dump(files));

        var ws = BinderOracle.Build(files);
        var target = rnd.Pick(cave.Surveys.ToList());
        var relName = RelativeSurveyName(ws, files, target.Path);
        if (relName is null) return new Outcome("(none)", $"survey {string.Join('.', target.Path)} is not in the model");

        var rule = new RenumberRule(rnd.Pick(OldPrefixes), rnd.Pick(NewPrefixes), rnd.Pick(Offsets));
        // A quarter of the rules aim one station at another's name, so refusals are common too.
        if (target.Stations.Count > 1 && rnd.Chance(0.25))
            rule = new RenumberRule(rnd.Pick(target.Stations), rnd.Pick(target.Stations));
        if (rule.IsNoOp) rule = rule with { NewPrefix = "T" };
        bool sub = rnd.Chance(0.5);
        var ruleText = $"rule '{rule.OldPrefix}' -> '{rule.NewPrefix}' {rule.Offset:+0;-0;0} on {relName}{(sub ? " and sub-surveys" : "")}";
        var texts = files.ToDictionary(f => f.Path, f => f.Text);
        var dump = BinderOracle.Dump(files);

        var plan = StationRenumber.Plan(ws, relName, rule, sub, texts.GetValueOrDefault);
        var absOf = AbsoluteStationNames(ws, files);
        string Abs(QualifiedName q) => absOf.TryGetValue(q.ToString(), out var a) ? a : "?" + q;

        // ---- the plan covers the survey, and each new name is the rule's ----
        var covered = plan.Renames.Select(r => Abs(r.Station)).Concat(plan.Unchanged.Select(Abs)).ToHashSet();
        var own = target.Stations.Select(s => target.Ref(s).Key).ToHashSet();
        var below = target.SelfAndDescendants().SelectMany(s => s.Stations.Select(st => s.Ref(st).Key)).ToHashSet();
        if (!own.IsSubsetOf(covered)) return new Outcome(ruleText, $"stations of the survey are not in the plan: {string.Join(", ", own.Except(covered))}", dump);
        if (!covered.IsSubsetOf(sub ? below : own)) return new Outcome(ruleText, $"the plan reaches outside the scope: {string.Join(", ", covered.Except(sub ? below : own))}", dump);
        foreach (var (station, newName) in plan.Renames)
            if (rule.Apply(station.Last) != newName) return new Outcome(ruleText, $"{station} -> {newName}, the rule says {rule.Apply(station.Last)}", dump);
        foreach (var station in plan.Unchanged)
            if (rule.Apply(station.Last) is not null) return new Outcome(ruleText, $"{station} is left alone, the rule renames it", dump);

        // ---- refused exactly when a survey would hold one name twice, or a name is invalid ----
        var mapping = plan.Renames.ToDictionary(r => Abs(r.Station), r => r.NewName);
        string? clash = Clash(cave, mapping);
        bool refusedByPlan = plan.Problems.Any(p => p.Kind is RenumberProblemKind.Duplicate or RenumberProblemKind.Collides or RenumberProblemKind.InvalidName);
        if (plan.Problems.Any(p => p.Kind is RenumberProblemKind.Stale or RenumberProblemKind.NoSurvey))
            return new Outcome(ruleText, "unexpected problem: " + string.Join(", ", plan.Problems), dump);
        if (refusedByPlan != (clash is not null))
            return new Outcome(ruleText, refusedByPlan ? $"refused ({string.Join(", ", plan.Problems)}) but the mapping is injective" : $"accepted but {clash}", dump);
        if (refusedByPlan) return plan.Batch.IsEmpty ? new Outcome(ruleText, Refused: true) : new Outcome(ruleText, "a refused plan carries edits", dump);
        if (plan.Renames.Count == 0) return new Outcome(ruleText);

        // ---- applied: the same cave with the stations renamed ----
        var edited = Apply(files, plan.Batch);
        var renamed = Rename(CaveShrinker.Clone(cave), mapping);
        var after = BinderOracle.Check(renamed, edited);
        if (after.Count > 0)
            return new Outcome(ruleText, "after the renumber the model differs:\n  " + string.Join("\n  ", after.Take(8)), BinderOracle.Dump(edited));

        // ---- an exact inverse restores every byte ----
        var inverse = new RenumberRule(rule.NewPrefix, rule.OldPrefix, -rule.Offset);
        bool exact = plan.Renames.All(r => inverse.Apply(r.NewName) == r.Station.Last)
                     && plan.Unchanged.All(u => inverse.Apply(u.Last) is null);
        if (!exact) return new Outcome(ruleText, Applied: true);
        var ws2 = BinderOracle.Build(edited);
        var texts2 = edited.ToDictionary(f => f.Path, f => f.Text);
        var back = StationRenumber.Plan(ws2, relName, inverse, sub, texts2.GetValueOrDefault);
        if (!back.Ok) return new Outcome(ruleText, "the inverse plan is refused: " + string.Join(", ", back.Problems), BinderOracle.Dump(edited));
        var restored = Apply(edited, back.Batch);
        foreach (var (f, r) in files.Zip(restored))
            if (f.Text != r.Text) return new Outcome(ruleText, $"the inverse does not restore {Path.GetFileName(f.Path)}", BinderOracle.Dump(restored));
        return new Outcome(ruleText, Applied: true, Inverted: true);
    }

    // The model's (file-local) name of the survey at an absolute path.
    private static string? RelativeSurveyName(WorkspaceSemanticModel ws, IReadOnlyList<PrintedFile> files, ImmutableArray<string> path)
    {
        foreach (var f in files)
            if (ws.PerFile.TryGetValue(f.Path, out var m))
                foreach (var qn in m.Surveys.Keys)
                    if (f.Prefix.AddRange(qn.Parts).SequenceEqual(path) && ws.SurveysByFullName.ContainsKey(qn.ToString()))
                        return qn.ToString();
        return null;
    }

    private static Dictionary<string, string> AbsoluteStationNames(WorkspaceSemanticModel ws, IReadOnlyList<PrintedFile> files)
    {
        var result = new Dictionary<string, string>();
        foreach (var f in files)
            if (ws.PerFile.TryGetValue(f.Path, out var m))
                foreach (var qn in m.Stations.Keys.Where(q => !q.Last.Contains('@')))
                    result.TryAdd(qn.ToString(), BinderOracle.Canon(f.Prefix, qn));
        return result;
    }

    // Two stations of one survey under one name, or a name Therion cannot read; null when neither.
    private static string? Clash(GCave cave, IReadOnlyDictionary<string, string> mapping)
    {
        foreach (var s in cave.Surveys)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var st in s.Stations)
            {
                var key = s.Ref(st).Key;
                var name = mapping.GetValueOrDefault(key, st);
                if (mapping.ContainsKey(key) && (name.StartsWith('-') || name.IndexOfAny(['.', '@', ':', '/']) >= 0 || !TherionIdentifiers.IsValid(name)))
                    return $"{key} would be named '{name}'";
                if (!names.TryAdd(name, key)) return $"{names[name]} and {key} would both be '{name}'";
            }
        }
        return null;
    }

    private static List<PrintedFile> Apply(IReadOnlyList<PrintedFile> files, EditBatch batch) =>
        files.Select(f => batch.Edits.FirstOrDefault(g => g.FilePath == f.Path) is { } g
            ? f with { Text = TextSplicer.Splice(f.Text, g.Edits) ?? throw new InvalidOperationException("edits do not apply to " + f.Path) }
            : f).ToList();

    private static GCave Rename(GCave cave, IReadOnlyDictionary<string, string> mapping)
    {
        GRef R(GRef r) => mapping.TryGetValue(r.Key, out var n) ? r with { Station = n } : r;
        foreach (var s in cave.Surveys)
        {
            for (int i = 0; i < s.Stations.Count; i++)
                if (mapping.TryGetValue(s.Ref(s.Stations[i]).Key, out var n)) s.Stations[i] = n;
            for (int i = 0; i < s.Shots.Count; i++)
                s.Shots[i] = s.Shots[i] with { From = R(s.Shots[i].From), To = s.Shots[i].To is { } to ? R(to) : null };
            for (int i = 0; i < s.Equates.Count; i++)
                s.Equates[i] = new GEquate(s.Equates[i].Members.Select(R).ToImmutableArray());
            for (int i = 0; i < s.Fixes.Count; i++)
                s.Fixes[i] = s.Fixes[i] with { Station = R(s.Fixes[i].Station) };
            for (int i = 0; i < s.Notes.Count; i++)
                s.Notes[i] = s.Notes[i] with { Station = R(s.Notes[i].Station) };
        }
        return cave;
    }
}
