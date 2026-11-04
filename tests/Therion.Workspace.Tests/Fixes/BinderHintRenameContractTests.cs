// The rename quick fix reads the station name and the suggestion out of the diagnostic text. The other
// CodeFixes tests build that text by hand, so here the diagnostic comes from the real binder: a change
// to the binder's wording must not silently switch the fix off.

using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class BinderHintRenameContractTests
{
    private const string Text = """
        survey s
          centreline
            data normal from to length compass clino
            a1 b7 10 0 0
            equate b7 a2
          endcentreline
        endsurvey
        """;

    private static SemanticModel Bind(string text) =>
        new SemanticBinder().Bind(new ThParser().Parse("/p/c.th", text).Value);

    [Fact]
    public void Binder_suggestion_drives_the_batch_safe_rename_fix()
    {
        var d = Assert.Single(Bind(Text).UnresolvedEquateDiagnostics());
        Assert.Equal(CodeFixes.UnresolvedStation, d.Code.Value);

        var fix = CodeFixes.ForDiagnostic(Text, d);

        Assert.NotNull(fix);
        Assert.Equal("Fix_Rename", fix!.TitleKey);
        Assert.Equal(["a2", "a1"], fix.TitleArgs);
        Assert.True(fix.SafeForBatch);

        var renamed = TextSplicer.Splice(Text, fix.Edits)!;
        Assert.Contains("equate b7 a1", renamed);
        Assert.Empty(Bind(renamed).UnresolvedEquateDiagnostics());
    }

    [Fact]
    public void Binder_suggestion_is_offered_on_its_line_and_planned_by_fix_all()
    {
        var d = Assert.Single(Bind(Text).UnresolvedEquateDiagnostics());
        Assert.Contains(CodeFixes.ForLine(Text, d.Span.Start.Line, [d]), f => f.TitleKey == "Fix_Rename");

        var (_, fixedCount, skipped) = FixAll.Plan(CodeFixes.UnresolvedStation, [d], _ => Text);
        Assert.Equal((1, 0), (fixedCount, skipped));
    }
}
