using System.Linq;
using Therion.Semantics.Thbook;
using Xunit;

namespace Therion.Semantics.Tests;

public class ThbookIndexTests
{
    [Fact]
    public void The_embedded_index_loads_with_its_edition()
    {
        Assert.Equal("v6.4.0", ThbookIndex.Edition);
        Assert.NotEmpty(ThbookIndex.Pages);
        Assert.Contains("\"pages\"", ThbookIndex.DefaultJson);
    }

    [Theory]
    [InlineData("equate", 34)]
    [InlineData("cs", 50)]
    [InlineData("survey", 16)]
    public void PageFor_resolves_a_known_term_case_insensitively(string term, int page)
    {
        Assert.Equal(page, ThbookIndex.PageFor(term));
        Assert.Equal(page, ThbookIndex.PageFor(term.ToUpperInvariant()));
    }

    [Fact]
    public void An_unindexed_term_has_no_page()
    {
        Assert.Null(ThbookIndex.PageFor("fix"));       // real Therion command, just not in the index
        Assert.Null(ThbookIndex.Lookup("nonesuch"));
    }

    [Fact]
    public void Lookup_yields_a_ready_to_quote_citation()
    {
        var entry = ThbookIndex.Lookup("equate");
        Assert.NotNull(entry);
        Assert.Equal(34, entry!.Page);
        Assert.Equal("Therion Book v6.4.0, p.34", entry.Citation);
    }

    [Fact]
    public void Search_puts_an_exact_hit_first_and_finds_substrings()
    {
        var hits = ThbookIndex.Search("centre");   // substring of centreline
        Assert.Contains(hits, h => h.Term == "centreline");

        var exact = ThbookIndex.Search("cs");
        Assert.Equal("cs", exact.First().Term);    // exact match ranks first
    }

    [Fact]
    public void An_empty_or_unmatched_query_returns_nothing()
    {
        Assert.Empty(ThbookIndex.Search(""));
        Assert.Empty(ThbookIndex.Search("   "));
        Assert.Empty(ThbookIndex.Search("zzzznotacommand"));
    }
}
