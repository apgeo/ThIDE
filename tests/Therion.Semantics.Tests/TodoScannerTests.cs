using System.Linq;

namespace Therion.Semantics.Tests;

// NOTE-01 — comment tag aggregator.
public class TodoScannerTests
{
    [Fact]
    public void Finds_tags_in_comments_with_correct_lines()
    {
        const string src =
            "survey cave\n" +
            "  # TODO resurvey the entrance\n" +
            "  centreline\n" +
            "    1 2 10 0 0  # FIXME transposed digits?\n" +
            "  endcentreline   # QM 3 going up\n" +
            "endsurvey\n";

        var items = TodoScanner.Scan("cave.th", src);

        Assert.Equal(3, items.Length);
        Assert.Contains(items, i => i.Tag == "TODO" && i.Span.Start.Line == 2);
        Assert.Contains(items, i => i.Tag == "FIXME" && i.Span.Start.Line == 4);
        Assert.Contains(items, i => i.Tag == "QM" && i.Span.Start.Line == 5);
        Assert.Contains(items, i => i.Text.Contains("resurvey"));
    }

    [Fact]
    public void Lowercase_tags_are_recognised_and_normalised()
    {
        var items = TodoScanner.Scan("a.th", "x 1 2 # todo check\n");
        Assert.Single(items);
        Assert.Equal("TODO", items[0].Tag);
    }

    [Fact]
    public void Plain_comments_and_code_are_ignored()
    {
        // No tag → no items; "todo" must be a whole word, not part of another token.
        var items = TodoScanner.Scan("a.th", "survey todos\n  # just a note\n  1 2 10 0 0\n");
        Assert.Empty(items);
    }
}
