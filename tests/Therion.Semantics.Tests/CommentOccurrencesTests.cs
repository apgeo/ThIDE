// P2 — the opt-in "rename in comments" pass: only whole-word matches inside # comments, never code.
using System.Collections.Generic;
using System.Linq;
using Therion.Semantics;

namespace Therion.Semantics.Tests;

public class CommentOccurrencesTests
{
    [Fact]
    public void Finds_whole_word_name_in_comments_only_not_in_code()
    {
        const string text = "survey s1 -title \"s1 main\"  # rename s1 here\ns1x 2 # not s1x\nendsurvey\n";
        var hits = CommentOccurrences.Find(text, "s1");

        Assert.NotEmpty(hits);
        // Every hit is the whole word "s1" and lands inside a comment.
        Assert.All(hits, h => Assert.Equal("s1", text.Substring(h.Start, h.Length)));
        Assert.Contains(hits, h => h.Start == text.IndexOf("s1 here", System.StringComparison.Ordinal));
        // The code occurrences (`survey s1`, the `-title` string) are NOT included.
        Assert.DoesNotContain(hits, h => h.Start == text.IndexOf("s1 -title", System.StringComparison.Ordinal));
        // "s1x" in a comment must not match (whole-word).
        Assert.DoesNotContain(hits, h => text.Substring(h.Start, 3) == "s1x");
    }

    [Fact]
    public void Excludes_already_handled_spans()
    {
        const string text = "# a and a\n";
        int first = text.IndexOf("a", System.StringComparison.Ordinal);
        var hits = CommentOccurrences.Find(text, "a", new HashSet<int> { first });
        Assert.DoesNotContain(hits, h => h.Start == first);   // excluded
        Assert.NotEmpty(hits);                                // the second "a" remains
    }
}
