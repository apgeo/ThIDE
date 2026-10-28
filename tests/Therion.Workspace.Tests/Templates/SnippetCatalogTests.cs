using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class SnippetCatalogTests
{
    [Fact]
    public void Built_ins_all_parse()
    {
        foreach (var s in SnippetCatalog.BuiltIns)
            Assert.Null(SnippetTemplate.Parse(s.Template).Error);
    }

    [Fact]
    public void Later_layers_override_by_trigger_case_insensitively()
    {
        var user = new SnippetLayer("user", """[ { "trigger": "SURVB", "template": "survey $1", "description": "mine" } ]""");
        var project = new SnippetLayer("project", """
            [
              // project house style
              { "trigger": "fix", "template": "fix ${1:st} $2 $3 $4", "scope": "th", },
            ]
            """);

        var (snippets, errors) = SnippetCatalog.Load(SnippetCatalog.BuiltIns, user, project);

        Assert.Empty(errors);
        Assert.Equal("mine", snippets.Single(s => s.Trigger.Equals("survb", StringComparison.OrdinalIgnoreCase)).Description);
        Assert.Equal(SnippetScope.Th, snippets.Single(s => s.Trigger == "fix").Scope);
    }

    [Fact]
    public void Broken_file_or_entry_is_reported_and_the_rest_kept()
    {
        var broken = new SnippetLayer("user", "[ { \"trigger\": ");
        var badEntry = new SnippetLayer("project", """[ { "trigger": "x", "template": "${1:open" }, { "trigger": "ok", "template": "fine" } ]""");

        var (snippets, errors) = SnippetCatalog.Load(SnippetCatalog.BuiltIns, broken, badEntry);

        Assert.Equal(2, errors.Count);
        Assert.Equal("user", errors[0].Source);
        Assert.Equal("project (x)", errors[1].Source);
        Assert.Contains(snippets, s => s.Trigger == "ok");
        Assert.DoesNotContain(snippets, s => s.Trigger == "x");
    }

    [Theory]
    [InlineData(SnippetScope.Th, "cave.th", true)]
    [InlineData(SnippetScope.Th, "plan.th2", false)]
    [InlineData(SnippetScope.Th2, "plan.th2", true)]
    [InlineData(SnippetScope.Thconfig, "thconfig", true)]
    [InlineData(SnippetScope.Thconfig, "cave.thconfig", true)]
    [InlineData(SnippetScope.Thconfig, "cave.th", false)]
    [InlineData(SnippetScope.Any, "x.txt", true)]
    public void Scope_filters_by_file_kind(SnippetScope scope, string file, bool expected)
    {
        Assert.Equal(expected, new SnippetDefinition("t", "d", "x", scope).AppliesTo(file));
    }
}
