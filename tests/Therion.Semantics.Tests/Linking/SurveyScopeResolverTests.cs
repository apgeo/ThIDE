using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;

namespace Therion.Semantics.Tests.Linking;

public class SurveyScopeResolverTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "scope-resolver");
    private static string P(string name) => Path.Combine(Root, name);

    private static (WorkspaceSemanticModel Ws, FileScopes Scopes) Build(params (string Name, string Text)[] files)
    {
        var parsed = files.ToDictionary(f => P(f.Name), f => new ThParser().Parse(P(f.Name), f.Text), StringComparer.OrdinalIgnoreCase);
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        return (ws, SurveyScopeResolver.Build(ws, p => parsed.TryGetValue(p, out var r) ? r.Value : null));
    }

    private static ImmutableArray<string> S(params string[] parts) => [.. parts];

    [Fact]
    public void File_input_inside_nested_surveys_sits_in_their_path()
    {
        var (_, scopes) = Build(
            ("main.th", "survey cave\n  survey north\n    input a.th\n  endsurvey\nendsurvey\n"),
            ("a.th", "survey a\n  input b.th\nendsurvey\n"),
            ("b.th", "survey b\nendsurvey\n"));

        Assert.Equal(S(), scopes.PrefixOf(P("main.th")));
        Assert.Equal(S("cave", "north"), scopes.PrefixOf(P("a.th")));
        Assert.Equal(S("cave", "north", "a"), scopes.PrefixOf(P("b.th")));
        Assert.Equal(P("a.th"), scopes.EnclosingFileOf(P("b.th")));
        Assert.Empty(scopes.Warnings);
    }

    [Fact]
    public void Effective_name_prefixes_the_file_local_name()
    {
        var (ws, scopes) = Build(
            ("main.th", "survey cave\n  input a.th\nendsurvey\n"),
            ("a.th", "survey a\n  centreline\n    data normal from to length compass clino\n    1 2 10 0 0\n  endcentreline\nendsurvey\n"));

        var local = ws.PerFile[P("a.th")].Stations.Keys.Single(k => k.Last == "2");

        Assert.Equal("cave.a.2", scopes.Effective(P("a.th"), local).ToString());
    }

    [Fact]
    public void Bare_equate_file_input_from_a_survey_takes_that_survey_as_scope()
    {
        var (_, scopes) = Build(
            ("main.th", "survey cave\n  input a.th\n  input links.th\nendsurvey\n"),
            ("a.th", "survey a\nendsurvey\n"),
            ("links.th", "equate 1@a 2@a\n"));

        Assert.Equal(S("cave"), scopes.PrefixOf(P("links.th")));
    }

    [Fact]
    public void Input_after_a_survey_block_is_back_at_the_root()
    {
        var (_, scopes) = Build(
            ("main.th", "survey cave\nendsurvey\ninput a.th\n"),
            ("a.th", "survey a\nendsurvey\n"));

        Assert.Equal(S(), scopes.PrefixOf(P("a.th")));
    }

    [Fact]
    public void Two_parents_with_different_scopes_warn_and_keep_the_first()
    {
        var (_, scopes) = Build(
            ("main.th", "survey one\n  input a.th\nendsurvey\nsurvey two\n  input a.th\nendsurvey\n"),
            ("a.th", "survey a\nendsurvey\n"));

        Assert.Equal(S("one"), scopes.PrefixOf(P("a.th")));
        Assert.Equal(new ScopeWarning(ScopeWarningKind.MultipleParents, P("a.th")), Assert.Single(scopes.Warnings));
    }

    [Fact]
    public void Include_cycle_warns_and_terminates()
    {
        var (_, scopes) = Build(
            ("a.th", "survey a\n  input b.th\nendsurvey\n"),
            ("b.th", "survey b\n  input a.th\nendsurvey\n"));

        Assert.Contains(scopes.Warnings, w => w.Kind == ScopeWarningKind.Cycle);
    }

    [Fact]
    public void Survey_path_at_an_offset_walks_nested_blocks()
    {
        const string text = "survey cave\n  survey a\n    centreline\n    endcentreline\n\n  endsurvey\n  # here\nendsurvey\ninput x.th\n";
        var ast = new ThParser().Parse("x.th", text).Value;

        Assert.Equal(S("cave", "a"), SurveyScopeResolver.SurveyPathAt(ast, text.IndexOf("centreline")));
        Assert.Equal(S("cave"), SurveyScopeResolver.SurveyPathAt(ast, text.IndexOf("# here")));
        Assert.Equal(S("cave"), SurveyScopeResolver.SurveyPathAt(ast, 0));
        Assert.Equal(S(), SurveyScopeResolver.SurveyPathAt(ast, text.IndexOf("input")));
    }

    [Fact]
    public void With_the_text_a_block_reaches_its_terminator()
    {
        const string text = "survey cave\n  survey a\n    centreline\n    endcentreline\n\n  endsurvey\nendsurvey\n";
        var ast = new ThParser().Parse("x.th", text).Value;
        int blankLine = text.IndexOf("\n\n") + 1;

        Assert.Equal("a", SurveyScopeResolver.SurveyBlockAt(ast, text.IndexOf("endcentreline"), text)!.Name);
        Assert.Equal("a", SurveyScopeResolver.SurveyBlockAt(ast, blankLine, text)!.Name);
        Assert.Equal("cave", SurveyScopeResolver.SurveyBlockAt(ast, text.IndexOf("endsurvey\nendsurvey") + 10, text)!.Name);
        Assert.Null(SurveyScopeResolver.SurveyBlockAt(ast, text.Length, text));
    }

    [Fact]
    public void Terminator_line_start_finds_the_end_line()
    {
        const string text = "survey a\n  input b.th\n\n  endsurvey\n";
        var block = (SurveyCommand)new ThParser().Parse("x.th", text).Value!.Children.OfType<SurveyCommand>().Single();

        Assert.Equal(text.IndexOf("  endsurvey"), SurveyScopeResolver.TerminatorLineStart(block, text));
        Assert.Equal(text.Length, SurveyScopeResolver.ExtentEnd(block, text));
    }

    [Fact]
    public void Unterminated_block_has_no_terminator()
    {
        const string text = "survey a\n  input b.th\n";
        var block = new ThParser().Parse("x.th", text).Value!.Children.OfType<SurveyCommand>().Single();

        Assert.Null(SurveyScopeResolver.TerminatorLineStart(block, text));
    }
}
