// Rename Symbol's plan without a window: which occurrences the rename itself rewrites, what the preview
// offers on top, and whether the rename may be applied straight from the name prompt.

using System.Text;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Tests;

public class RenamePlannerTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static string Survey(string name, string extra = "") =>
        $"survey {name}\n" +
        "  centreline\n" +
        "    data normal from to length compass clino\n" +
        "    1 2 5 0 0\n" +
        "    2 3 5 90 0\n" +
        "  endcentreline\n" +
        extra +
        "endsurvey\n";

    private async Task<(Therion.Semantics.WorkspaceSemanticModel Model, FileDocumentViewModel Active)> LoadAsync(string activeFile)
    {
        var cfg = Path.Combine(_dir.Path, "thconfig");
        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);
        var model = ws.BuildSemanticModel();
        var path = Path.Combine(_dir.Path, activeFile);
        var active = new FileDocumentViewModel(path, File.ReadAllText(path), new MeasurementsViewModel());
        return (model, active);
    }

    /// <summary>What the window does before planning: resolve the clicked token to its declaration.</summary>
    private static RenamePlan Plan(Therion.Semantics.WorkspaceSemanticModel model, FileDocumentViewModel active, string raw, ReferenceKind kind)
    {
        // The document's own navigation once its background parse has landed (no dispatcher runs here).
        var nav = new Therion.Semantics.WorkspaceSymbolNavigationService(model, model.PerFile.GetValueOrDefault(active.FilePath), active.FilePath);
        var target = nav.GoToDefinition(raw, kind) ?? throw new InvalidOperationException($"'{raw}' does not resolve");
        var oldName = RenamePlanner.OldName(raw, kind)!;
        return RenamePlanner.Plan(model, active, nav, raw, kind, oldName, target);
    }

    private static string Apply(RenameFileChanges fc, string newName)
    {
        var sb = new StringBuilder(fc.FileText);
        foreach (var (start, length) in fc.Hits.OrderByDescending(h => h.Start))
            sb.Remove(start, length).Insert(start, newName);
        return sb.ToString();
    }

    [Theory]
    [InlineData("1@b.a", ReferenceKind.Station, "1")]
    [InlineData("1@b.a", ReferenceKind.Survey, "b")]
    [InlineData("x:mark", ReferenceKind.Station, "x")]
    [InlineData("cave", ReferenceKind.Survey, "cave")]
    [InlineData("st", ReferenceKind.Any, "st")]
    public void The_old_name_is_the_token_part_the_rename_replaces(string raw, ReferenceKind kind, string expected) =>
        Assert.Equal(expected, RenamePlanner.OldName(raw, kind));

    [Fact]
    public async Task A_station_used_only_in_its_own_file_is_a_simple_rename_of_exactly_its_occurrences()
    {
        _dir.Write("thconfig", "source cave.th\n");
        _dir.Write("cave.th", Survey("cave", "  # 2 is the pitch head\n"));
        var (model, active) = await LoadAsync("cave.th");

        var plan = Plan(model, active, "2", ReferenceKind.Station);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(active.FilePath, change.FilePath);
        // Both data-line occurrences, not the comment, the length "5" or anything else that reads "2".
        Assert.Equal(Survey("cave", "  # 2 is the pitch head\n").Replace("    1 2 5", "    1 9 5").Replace("    2 3 5", "    9 3 5"),
            Apply(change, "9"));
        Assert.False(plan.CrossFile);
        Assert.False(plan.NeedsPreview(alwaysPreview: false));
        Assert.True(plan.NeedsPreview(alwaysPreview: true));
        // The comment mention is offered, not applied.
        Assert.Single(Assert.Single(plan.CommentChanges).Hits);
    }

    [Fact]
    public async Task A_station_referenced_from_another_file_needs_the_preview_and_offers_its_equated_namesake()
    {
        _dir.Write("thconfig", "source main.th\n");
        _dir.Write("main.th", "survey main\n  input a.th\n  input b.th\n  equate 3@a 3@b\nendsurvey\n");
        _dir.Write("a.th", Survey("a"));
        _dir.Write("b.th", Survey("b"));
        var (model, active) = await LoadAsync("a.th");

        var plan = Plan(model, active, "3", ReferenceKind.Station);

        // The rename itself: a.th's own occurrence and the equate's reference to it, nothing in b.th.
        Assert.Equal(new[] { "a.th", "main.th" }, plan.Changes.Select(c => Path.GetFileName(c.FilePath)).Order(StringComparer.Ordinal));
        Assert.Equal("survey main\n  input a.th\n  input b.th\n  equate 9@a 3@b\nendsurvey\n",
            Apply(plan.Changes.Single(c => c.FilePath.EndsWith("main.th", StringComparison.Ordinal)), "9"));
        Assert.True(plan.CrossFile);
        Assert.True(plan.NeedsPreview(alwaysPreview: false));
        // b.3 is the same point under the same name: offered, so the equate is not broken by accident.
        Assert.Contains(plan.EquateLinkedChanges, c => c.FilePath.EndsWith("b.th", StringComparison.Ordinal));
        Assert.Contains(plan.SameNameChanges, c => c.FilePath.EndsWith("b.th", StringComparison.Ordinal));
        // An opt-in pass never repeats a span the rename itself already rewrites.
        foreach (var extra in plan.EquateLinkedChanges.Concat(plan.SameNameChanges))
            foreach (var own in plan.Changes.Where(c => c.FilePath == extra.FilePath))
                Assert.Empty(extra.Hits.Intersect(own.Hits));
    }

    [Fact]
    public async Task The_active_buffer_is_planned_from_its_unsaved_text()
    {
        _dir.Write("thconfig", "source cave.th\n");
        _dir.Write("cave.th", Survey("cave"));
        var (model, active) = await LoadAsync("cave.th");
        // An unsaved edit above the centreline shifts every offset; the plan must use the buffer.
        active.DocumentText = "# unsaved note\n" + active.DocumentText;

        var plan = Plan(model, active, "2", ReferenceKind.Station);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(active.DocumentText, change.FileText);
        Assert.Equal(2, change.Hits.Count);
        Assert.All(change.Hits, h => Assert.Equal("2", change.FileText.Substring(h.Start, h.Length)));
    }

    [Theory]
    [InlineData("new", null)]
    [InlineData("a b", ' ')]
    [InlineData("a#b", '#')]
    public void An_invalid_new_name_names_the_character_Therion_refuses(string name, char? bad) =>
        Assert.Equal(bad, RenamePlanner.InvalidName(name));
}
