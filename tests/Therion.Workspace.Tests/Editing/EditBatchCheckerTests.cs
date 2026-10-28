using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

public class EditBatchCheckerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "edit-batch");
    private static string P(string name) => Path.Combine(Root, name);

    private static EditBatchCheck Check(EditBatch batch, Dictionary<string, string> files) =>
        EditBatchChecker.Check(batch, p => files.GetValueOrDefault(p), p => files.ContainsKey(p) || p.EndsWith("dir"), files.ContainsKey);

    [Fact]
    public void Clean_batch_reports_the_rewritten_text()
    {
        var files = new Dictionary<string, string> { [P("a.th")] = "survey a\nendsurvey\n" };
        var batch = new EditBatch([new FileTextEdits(P("a.th"), [TextEdit.Insert(9, "  input b.th\n")])], [], []);

        var check = Check(batch, files);

        Assert.True(check.Ok);
        Assert.Equal("survey a\n  input b.th\nendsurvey\n", check.NewTexts[P("a.th")]);
    }

    [Fact]
    public void Stale_edit_is_reported()
    {
        var files = new Dictionary<string, string> { [P("a.th")] = "survey a\nendsurvey\n" };
        var batch = new EditBatch([new FileTextEdits(P("a.th"), [new TextEdit(0, 6, "centre", "x")])], [], []);

        Assert.Equal([new EditProblem(P("a.th"), EditProblemKind.Stale)], Check(batch, files).Problems);
    }

    [Fact]
    public void Missing_file_and_missing_copy_source_are_reported()
    {
        var batch = new EditBatch(
            [new FileTextEdits(P("gone.th"), [TextEdit.Insert(0, "x")])],
            [],
            [new FileCopy(P("src.th"), P("dst.th"))]);

        var kinds = Check(batch, []).Problems.Select(p => (p.FilePath, p.Kind));

        Assert.Equal([(P("gone.th"), EditProblemKind.Missing), (P("src.th"), EditProblemKind.Missing)], kinds);
    }

    [Fact]
    public void Existing_create_or_copy_target_is_reported()
    {
        var files = new Dictionary<string, string> { [P("a.th")] = "", [P("b.th")] = "", [P("src.th")] = "" };
        var batch = new EditBatch([], [new FileCreate(P("a.th"), "x")], [new FileCopy(P("src.th"), P("b.th"))]);

        var kinds = Check(batch, files).Problems.Select(p => (p.FilePath, p.Kind));

        Assert.Equal([(P("a.th"), EditProblemKind.Exists), (P("b.th"), EditProblemKind.Exists)], kinds);
    }

    [Fact]
    public void Same_target_twice_is_reported()
    {
        var files = new Dictionary<string, string> { [P("src.th")] = "" };
        var batch = new EditBatch([], [new FileCreate(P("n.th"), "x")], [new FileCopy(P("src.th"), P("n.th"))]);

        Assert.Equal([new EditProblem(P("n.th"), EditProblemKind.Duplicate)], Check(batch, files).Problems);
    }

    [Fact]
    public void Directory_as_copy_source_is_reported()
    {
        var batch = new EditBatch([], [], [new FileCopy(P("dir"), P("n.th"))]);

        Assert.Equal([new EditProblem(P("dir"), EditProblemKind.NotAFile)], Check(batch, []).Problems);
    }
}
