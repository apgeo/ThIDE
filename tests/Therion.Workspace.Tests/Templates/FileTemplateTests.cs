using Therion.Workspace.Editing;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class FileTemplateTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "file-templates");
    private static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));

    private static FileTemplate T(string id) => FileTemplateCatalog.BuiltIns.Single(t => t.Id == id);

    [Fact]
    public void Built_ins_all_parse()
    {
        foreach (var t in FileTemplateCatalog.BuiltIns)
            Assert.Null(SnippetTemplate.Parse(t.Body).Error);
    }

    [Fact]
    public void Trip_template_takes_its_survey_name_from_the_file_and_defaults_the_rest()
    {
        var plan = NewFilePlanner.Plan(T("th-trip"), P("2026 10 21.th"), v => v == "DATE" ? "2026.10.21" : null);

        Assert.StartsWith("encoding utf-8\nsurvey 2026_10_21 -title \"2026_10_21\"\n  centreline\n    date 2026.10.21\n", plan.Content);
        Assert.Contains("    units length meters\n    data normal from to length compass clino\n", plan.Content);
        Assert.Equal(plan.Content.IndexOf("\n  endcentreline"), plan.Caret); // on the empty line for the first leg
        Assert.Single(plan.Batch.Creates);
        Assert.False(plan.Batch.RequiresWorkspaceReload);
    }

    [Fact]
    public void Input_goes_after_the_last_input_with_its_indentation()
    {
        var parent = "survey cave\n  input a.th\n  input b.th\nendsurvey\n";

        var plan = NewFilePlanner.Plan(T("blank"), P("trips/c.th"), _ => null, P("main.th"), _ => parent);

        Assert.Equal("input trips/c.th", plan.InputLine);
        var edited = TextSplicer.Splice(parent, plan.Batch.Edits.Single().Edits);
        Assert.Equal("survey cave\n  input a.th\n  input b.th\n  input trips/c.th\nendsurvey\n", edited);
        Assert.True(plan.Batch.RequiresWorkspaceReload);
    }

    [Fact]
    public void Input_without_other_inputs_goes_inside_the_first_survey_or_after_encoding()
    {
        var inSurvey = NewFilePlanner.Plan(T("blank"), P("c.th"), _ => null, P("main.th"), _ => "survey cave\nendsurvey\n");
        var atTop = NewFilePlanner.Plan(T("blank"), P("c.th"), _ => null, P("main.th"), _ => "encoding utf-8\nequate 1@a 1@b\n");

        Assert.Equal("survey cave\n  input c.th\nendsurvey\n", TextSplicer.Splice("survey cave\nendsurvey\n", inSurvey.Batch.Edits[0].Edits));
        Assert.Equal("encoding utf-8\ninput c.th\nequate 1@a 1@b\n", TextSplicer.Splice("encoding utf-8\nequate 1@a 1@b\n", atTop.Batch.Edits[0].Edits));
    }

    [Fact]
    public void An_existing_input_is_not_written_twice()
    {
        var plan = NewFilePlanner.Plan(T("blank"), P("c.th"), _ => null, P("main.th"), _ => "survey cave\n  input c.th\nendsurvey\n");

        Assert.True(plan.InputExists);
        Assert.Empty(plan.Batch.Edits);
    }

    [Fact]
    public void Free_name_counts_up_past_taken_names()
    {
        var taken = new HashSet<string> { Path.Combine(Root, "trip.th"), Path.Combine(Root, "trip-2.th") };

        Assert.Equal("trip-3.th", NewFilePlanner.FreeName(Root, "trip", ".th", taken.Contains));
    }

    [Fact]
    public void Template_files_add_or_replace_built_ins()
    {
        var (templates, errors) = FileTemplateCatalog.Load(FileTemplateCatalog.BuiltIns,
        [
            new SnippetLayer("club.json", """{ "id": "club-trip", "title": "Club trip", "body": "survey $SURVEY_NAME\nendsurvey\n" }"""),
            new SnippetLayer("blank.json", """{ "id": "blank", "title": "Mine", "extension": "th", "body": "# blank\n" }"""),
            new SnippetLayer("bad.json", """{ "id": "x" }"""),
        ]);

        Assert.Single(errors);
        Assert.Equal("Mine", templates.Single(t => t.Id == "blank").Title);
        Assert.Equal(".th", templates.Single(t => t.Id == "club-trip").Extension);
        Assert.Equal("club-trip", templates[^1].Id);
    }
}
