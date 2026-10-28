using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class SnippetTemplateTests
{
    private static RenderedSnippet Render(string template, Func<string, string?>? vars = null, string indent = "", string nl = "\n")
    {
        var (t, error) = SnippetTemplate.Parse(template);
        Assert.Null(error);
        return t!.Render(vars ?? (_ => null), indent, nl);
    }

    [Fact]
    public void Plain_text_and_final_caret()
    {
        var r = Render("survey $0\nendsurvey");

        Assert.Equal("survey \nendsurvey", r.Text);
        Assert.Equal(7, r.FinalCaret);
        Assert.Empty(r.Stops);
    }

    [Fact]
    public void Placeholders_choices_and_mirrors()
    {
        var r = Render("survey ${1:trip} -title \"$2\"\n  ${3|normal,diving|}\nendsurvey # $1");

        Assert.Equal("survey trip -title \"\"\n  normal\nendsurvey # trip", r.Text);
        Assert.Equal([(1, false), (1, true), (2, false), (3, false)], r.Stops.Select(s => (s.Index, s.IsMirror)));
        Assert.Equal(["normal", "diving"], r.Stops.Single(s => s.Index == 3).Choices);
        Assert.Equal(r.Text.Length, r.FinalCaret);
    }

    [Fact]
    public void Variables_resolve_or_fall_back_or_become_stops()
    {
        var r = Render("date $DATE\nteam \"${WHO:nobody}\"\n$MYSTERY", n => n == "DATE" ? "2026.10.28" : null);

        Assert.Equal("date 2026.10.28\nteam \"nobody\"\nMYSTERY", r.Text);
        var stop = Assert.Single(r.Stops);
        Assert.Equal("MYSTERY", r.Text.Substring(stop.Start, stop.Length));
    }

    [Fact]
    public void Escapes_and_lone_dollars_are_literal()
    {
        var r = Render(@"cost \$5 \} \\ and $ alone and $lower");

        Assert.Equal(@"cost $5 } \ and $ alone and $lower", r.Text);
    }

    [Fact]
    public void Nested_braces_in_a_default_are_kept()
    {
        Assert.Equal("scale [0 0 {1} 0]", Render("scale ${1:[0 0 {1} 0]}").Text);
    }

    [Fact]
    public void Continuation_lines_and_multiline_values_are_indented()
    {
        var r = Render("centreline\n  $TEAM\nendcentreline", n => n == "TEAM" ? "team \"A\"\nteam \"B\"" : null, indent: "    ", nl: "\r\n");

        Assert.Equal("centreline\r\n      team \"A\"\r\n    team \"B\"\r\n    endcentreline", r.Text);
    }

    [Theory]
    [InlineData("${1:unclosed", 0)]
    [InlineData("${1|a,b}", 0)]
    [InlineData("${x}", 0)]
    [InlineData("${1?}", 3)]
    public void Malformed_templates_are_errors_not_exceptions(string template, int position)
    {
        var (t, error) = SnippetTemplate.Parse(template);

        Assert.Null(t);
        Assert.Equal(position, error!.Position);
    }

    [Fact]
    public void Variable_names_are_listed_once()
    {
        var (t, _) = SnippetTemplate.Parse("$DATE ${DATE} $TIME $1");

        Assert.Equal(["DATE", "TIME"], t!.VariableNames);
    }
}
