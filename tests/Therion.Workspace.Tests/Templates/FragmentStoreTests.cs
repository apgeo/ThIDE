using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class FragmentStoreTests
{
    [Fact]
    public void Creates_the_file_and_round_trips_through_the_catalog()
    {
        var block = "export map -projection plan -layout l1 -output plan.pdf\n# costs $5";

        var (json, error) = FragmentStore.Append(null, "plan", "our plan export", FragmentStore.TemplateFromText(block));
        var (snippets, errors) = SnippetCatalog.Load([], new SnippetLayer("p", json));

        Assert.Null(error);
        Assert.Empty(errors);
        var s = Assert.Single(snippets);
        Assert.Equal(SnippetScope.Thconfig, s.Scope);
        Assert.Equal(block, SnippetTemplate.Parse(s.Template).Template!.Render(_ => null).Text);
    }

    [Fact]
    public void Replaces_the_same_trigger_and_keeps_other_entries()
    {
        var existing = """
            [
              // ours
              { "trigger": "fix", "template": "fix $1" },
              { "trigger": "PLAN", "template": "old" },
            ]
            """;

        var (json, _) = FragmentStore.Append(existing, "plan", "d", "new");
        var (snippets, _) = SnippetCatalog.Load([], new SnippetLayer("p", json));

        Assert.Equal(["fix", "plan"], snippets.Select(s => s.Trigger));
        Assert.Equal("new", snippets.Single(s => s.Trigger == "plan").Template);
    }

    [Fact]
    public void Leaves_a_file_that_is_not_a_list_alone()
    {
        var (json, error) = FragmentStore.Append("{ \"trigger\": \"x\" }", "plan", "d", "t");

        Assert.Null(json);
        Assert.NotNull(error);
    }
}
