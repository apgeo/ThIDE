using Therion.Semantics;
using Therion.Syntax;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

// Invented tables and surveys.
public class AutomationDialogsViewModelTests
{
    [Fact]
    public void Paste_shots_follows_the_options()
    {
        var m = new PasteShotsViewModel("3.4 120 -5\n3.4 120.1 -5\n3.4 120.2 -5\n1 40 10\n", "A1", wrap: false);
        Assert.True(m.CanInsert);
        Assert.True(m.NeedsStations);
        Assert.Contains("A1 A2", m.Output);
        Assert.Contains("A2 -", m.Output);
        Assert.NotEmpty(m.Issues);   // the merge is reported

        m.MergeRepeats = 0;
        Assert.Contains("A3 A4", m.Output);
        m.Wrap = true;
        Assert.StartsWith("centreline\n", m.Output);
        m.Columns = "from to";
        Assert.Contains(m.Issues, i => i.Length > 0);
    }

    [Fact]
    public void Paste_shots_reports_a_short_leg_row_and_shows_where_it_inserts()
    {
        var m = new PasteShotsViewModel("from to length compass clino\n1 2 4.50 120 -10\n2 3 3.20 -5\n3 4 5.00 90 2\n", null, wrap: false,
            target: "Insert into: trip.th, centreline at line 3");
        Assert.Equal("Insert into: trip.th, centreline at line 3", m.Target);
        Assert.DoesNotContain("2 -", m.Output);
        Assert.Equal([PasteShotsViewModel.Describe(new Therion.Workspace.Import.ShotIssue(3, Therion.Workspace.Import.ShotIssueKind.TooFewValues))], m.Issues);
        Assert.Contains("3", m.Issues.Single());
    }

    [Fact]
    public void Every_shot_issue_has_a_message()
    {
        // A count issue has its singular/plural forms (ShotIssue_<kind>_One …) instead of one text.
        foreach (var kind in Enum.GetValues<Therion.Workspace.Import.ShotIssueKind>())
            Assert.True(ThIDE.Resources.Tr.HasPlural("ShotIssue_" + kind) || ThIDE.Resources.Tr.Find("ShotIssue_" + kind) is not null, kind.ToString());
    }

    [Fact]
    public void Paste_shots_with_nothing_usable_cannot_insert()
    {
        var m = new PasteShotsViewModel("just words", null, wrap: true);
        Assert.False(m.CanInsert);
        Assert.Equal("", m.Output);
    }

    [Fact]
    public void Renumber_plans_live_and_blocks_problems()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "renumber-vm", "a.th"));
        var text = "survey a\n  centreline\n    data normal from to length compass clino\n    1 2 3 0 0\n    2 X3 3 0 0\n  endcentreline\nendsurvey\n";
        var ws = WorkspaceSemanticModel.Build(new Dictionary<string, ParseResult<TherionFile>> { [path] = new ThParser().Parse(path, text) }, Array.Empty<XviFile>());
        var m = new RenumberStationsViewModel(ws, p => p == path ? text : null, "a");
        Assert.False(m.CanApply);   // nothing chosen yet

        m.NewPrefix = "B";
        Assert.True(m.CanApply);
        Assert.Contains("1  →  B1", m.Examples);

        m.NewPrefix = "X";
        m.Offset = 1;   // 2 → X3, which X3 keeps
        Assert.False(m.CanApply);
        Assert.NotEmpty(m.Problems);
    }
}
