using Therion.Core;
using Therion.Semantics;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests;

/// <summary>
/// On a case-sensitive file system (Linux) <c>Cave.th</c> and <c>cave.th</c> are two files. Both must
/// reach the model, each with its own surveys and stations, and edits and lookups must keep them apart.
/// Invented files in a temp folder; the parse cache is in memory only.
/// </summary>
public sealed class CaseVariantWorkspaceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // Both declare their survey and first station at the same offsets, so a lookup that compares the
    // paths without case finds the wrong one.
    private const string Upper = "survey upper\n  centreline\n    1 2 3 0 0\n  endcentreline\nendsurvey\n";
    private const string Lower = "survey lower\n  centreline\n    1 2 4 0 0\n  endcentreline\nendsurvey\n";

    private async Task<(TherionWorkspace Ws, string Upper, string Lower)> LoadAsync()
    {
        var upper = Path.Combine(_dir.Path, "Cave.th");
        var lower = Path.Combine(_dir.Path, "cave.th");
        File.WriteAllText(upper, Upper);
        File.WriteAllText(lower, Lower);
        var config = Path.Combine(_dir.Path, "thconfig");
        File.WriteAllText(config, "source Cave.th\nsource cave.th\n");
        var ws = new TherionWorkspace(new InMemoryParseCache(), new WorkspaceOptions { DisableDiskCache = true });
        await ws.LoadAsync(config);
        return (ws, upper, lower);
    }

    [Fact]
    public async Task Both_files_are_loaded_and_both_surveys_are_in_the_model()
    {
        if (!OperatingSystem.IsLinux()) return;
        var (ws, upper, lower) = await LoadAsync();
        await using var _ = ws;

        Assert.Contains(upper, ws.LoadedFiles);
        Assert.Contains(lower, ws.LoadedFiles);
        var model = ws.BuildSemanticModel();
        Assert.True(model.PerFile.ContainsKey(upper) && model.PerFile.ContainsKey(lower));
        Assert.Equal(["lower", "upper"], model.SurveysByFullName.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Upper, File.ReadAllText(upper));
        Assert.NotSame(ws.TryGetFile(upper), ws.TryGetFile(lower));
    }

    [Fact]
    public async Task A_lookup_by_declaration_finds_the_symbol_of_that_file()
    {
        if (!OperatingSystem.IsLinux()) return;
        var (ws, upper, lower) = await LoadAsync();
        await using var _ = ws;
        var model = ws.BuildSemanticModel();

        foreach (var (path, name) in new[] { (upper, "upper"), (lower, "lower") })
        {
            var survey = model.PerFile[path].Surveys.Values.Single();
            Assert.Equal(name, model.FindSurveyByDeclaration(survey.DeclarationSpan)?.Name.ToString());
            var station = model.PerFile[path].Stations.Values.First(s => s.Name.Last == "1");
            Assert.Equal(name + ".1", model.FindStationByDeclaration(station.DeclarationSpan)?.Name.ToString());
        }
    }

    [Fact]
    public async Task A_renumber_edits_only_the_file_of_the_survey()
    {
        if (!OperatingSystem.IsLinux()) return;
        var (ws, upper, lower) = await LoadAsync();
        await using var _ = ws;
        var model = ws.BuildSemanticModel();

        var plan = StationRenumber.Plan(model, "lower", new RenumberRule("", "L"), false, File.ReadAllText);

        Assert.True(plan.Ok, string.Join(", ", plan.Problems));
        var group = Assert.Single(plan.Batch.Edits);
        Assert.Equal(lower, group.FilePath);
        Assert.Equal(Lower.Replace("1 2 4", "L1 L2 4"), TextSplicer.Splice(Lower, group.Edits));
    }

    [Fact]
    public async Task An_edit_to_one_file_reparses_only_that_file()
    {
        if (!OperatingSystem.IsLinux()) return;
        var (ws, upper, lower) = await LoadAsync();
        await using var _ = ws;

        ws.UpdateFileFromText(lower, Lower.Replace("survey lower", "survey lower2"));
        var model = ws.BuildSemanticModel();

        Assert.Equal(["lower2", "upper"], model.SurveysByFullName.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Fix_all_groups_diagnostics_of_the_two_files_apart()
    {
        if (!OperatingSystem.IsLinux()) return;
        var texts = new Dictionary<string, string> { ["/c/Cave.th"] = "survey a\n  equate 1 2\n", ["/c/cave.th"] = "survey b\n  equate 3 4\n" };
        var diagnostics = texts.Keys.Select(p => Diagnostic.Create(CodeFixes.UnterminatedBlock, DiagnosticSeverity.Warning, "unterminated",
            new SourceSpan(p, new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0)));

        var (batch, fixedCount, _) = FixAll.Plan(CodeFixes.UnterminatedBlock, diagnostics, texts.GetValueOrDefault);

        Assert.Equal(2, fixedCount);
        Assert.Equal(["/c/Cave.th", "/c/cave.th"], batch.Edits.Select(g => g.FilePath).Order(StringComparer.Ordinal));
        Assert.True(FilePaths.Same("/c/Cave.th", "/c/Cave.th") && !FilePaths.Same("/c/Cave.th", "/c/cave.th"));
    }
}
