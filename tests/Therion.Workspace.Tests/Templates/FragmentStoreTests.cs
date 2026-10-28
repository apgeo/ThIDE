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

    [Fact]
    public void Adding_a_new_trigger_keeps_the_files_comments()
    {
        var existing = "[\n  // club house style\n  { \"trigger\": \"fix\", \"template\": \"fix $1\" }\n]\n";

        var (json, error) = FragmentStore.Append(existing, "plan", "d", "x");
        var (snippets, errors) = SnippetCatalog.Load([], new SnippetLayer("p", json));

        Assert.Null(error);
        Assert.Contains("// club house style", json);
        Assert.Empty(errors);
        Assert.Equal(["fix", "plan"], snippets.Select(s => s.Trigger));
    }

    [Fact]
    public void A_non_string_trigger_is_not_a_crash()
    {
        var (json, error) = FragmentStore.Append("[ { \"trigger\": 5, \"template\": \"x\" } ]", "plan", "d", "x");

        Assert.Null(error);
        Assert.NotNull(json);
    }

    [Theory]
    [InlineData("[\n  { \"trigger\": \"fix\", \"template\": \"x\" } // club default\n]\n")]
    [InlineData("[\n  { \"trigger\": \"fix\", \"template\": \"x\" }\n]\n// see [1]\n")]
    public void A_comment_near_the_closing_bracket_does_not_break_the_file(string existing)
    {
        var (json, error) = FragmentStore.Append(existing, "plan", "d", "x");
        var (snippets, errors) = SnippetCatalog.Load([], new SnippetLayer("p", json));

        Assert.Null(error);
        Assert.Empty(errors);
        Assert.Equal(["fix", "plan"], snippets.Select(s => s.Trigger));
    }
}
