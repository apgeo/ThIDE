using Therion.Lsp;
using Therion.Workspace.Templates;

namespace ThIDE.Tests;

public class LspCompletionTests
{
    private static readonly DateTime Now = new(2026, 10, 28);

    [Fact]
    public void First_word_offers_scoped_snippets_with_variables_filled_and_keywords()
    {
        var items = CompletionProvider.Complete("/c/cave.th", "survey a\n  cl\nendsurvey\n", 1, 4, SnippetCatalog.BuiltIns, Now);

        var clt = items.Single(i => i.Label == "clt");
        Assert.True(clt.IsSnippet);
        Assert.StartsWith("centreline\n  date 2026.10.28\n", clt.InsertText);
        Assert.Contains(items, i => i.Label == "centreline" && !i.IsSnippet);
        Assert.DoesNotContain(items, i => i.Label == "thconf"); // thconfig-only
    }

    [Fact]
    public void Nothing_after_the_first_word()
    {
        Assert.Empty(CompletionProvider.Complete("/c/cave.th", "survey a -ti", 0, 12, SnippetCatalog.BuiltIns, Now));
    }

    [Fact]
    public void Rendered_snippet_becomes_lsp_syntax_with_escapes()
    {
        var (t, _) = SnippetTemplate.Parse("scrap ${1:s1} -note \"\\$x}\"\n  $0\nendscrap");

        Assert.Equal("scrap ${1:s1} -note \"\\$x\\}\"\n  $0\nendscrap", CompletionProvider.ToLspSnippet(t!.Render(_ => null)));
    }
}
