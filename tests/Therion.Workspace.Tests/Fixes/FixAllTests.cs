using Therion.Core;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class FixAllTests
{
    private static Diagnostic D(string file, int line, string message, string hint)
    {
        var at = new SourceLocation(line, 1);
        return Diagnostic.Create(CodeFixes.UnresolvedStation, DiagnosticSeverity.Error, message, new SourceSpan(file, at, at, 0, 0), hint);
    }

    private static readonly Dictionary<string, string> Files = new()
    {
        ["/c/a.th"] = "survey a\n  equate 1@bb 2@c\n  equate 3@bb 4@c\nendsurvey\n",
        ["/c/b.th"] = "survey b\n  equate x y\nendsurvey\n",
    };

    [Fact]
    public void Plans_safe_fixes_per_file_and_counts_the_rest()
    {
        var diags = new[]
        {
            D("/c/a.th", 2, "Station '1@bb' not found", "did you mean '1@b'?"),
            D("/c/a.th", 3, "Station '3@bb' not found", "did you mean '3@b'?"),
            D("/c/b.th", 2, "Station 'x' not found", "did you mean 'p'? did you mean 'q'?"),
        };

        var (batch, fixedCount, skipped) = FixAll.Plan(CodeFixes.UnresolvedStation, diags, Files.GetValueOrDefault);

        Assert.Equal(2, fixedCount);
        Assert.Equal(1, skipped);
        var group = Assert.Single(batch.Edits);
        Assert.Equal("survey a\n  equate 1@b 2@c\n  equate 3@b 4@c\nendsurvey\n", TextSplicer.Splice(Files["/c/a.th"], group.Edits));
    }

    [Fact]
    public void Preview_shows_each_line_before_and_after_and_keep_filters()
    {
        var diags = new[]
        {
            D("/c/a.th", 2, "Station '1@bb' not found", "did you mean '1@b'?"),
            D("/c/a.th", 3, "Station '3@bb' not found", "did you mean '3@b'?"),
        };
        var (batch, _, _) = FixAll.Plan(CodeFixes.UnresolvedStation, diags, Files.GetValueOrDefault);

        var rows = FixAll.Preview(batch, Files.GetValueOrDefault);

        Assert.Equal([2, 3], rows.Select(r => r.Line));
        Assert.Equal("  equate 1@bb 2@c", rows[0].Before);
        Assert.Equal("  equate 1@b 2@c", rows[0].After);

        var kept = FixAll.Keep(batch, [rows[1]]);
        Assert.Equal("survey a\n  equate 1@bb 2@c\n  equate 3@b 4@c\nendsurvey\n", TextSplicer.Splice(Files["/c/a.th"], kept.Edits.Single().Edits));
    }
}
