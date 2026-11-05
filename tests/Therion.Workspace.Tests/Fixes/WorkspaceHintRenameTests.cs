// In a project, TH_SEM_001 comes from the workspace, not the standalone fallback. It carries the
// binder's "did you mean" hint so the rename is offered for the one diagnostic, but the hint is only
// the nearest name in the file: fix all must not rename every unresolved reference by it.

using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class WorkspaceHintRenameTests
{
    private const string Path = "/p/c.th";

    private const string Text = """
        survey s
          centreline
            data normal from to length compass clino
            a1 b7 10 0 0
            c3 d9 10 0 0
            equate b7 a2
            equate d9 c4
          endcentreline
        endsurvey
        """;

    private static IReadOnlyList<Therion.Core.Diagnostic> WorkspaceDiagnostics(string text)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { [Path] = new ThParser().Parse(Path, text) };
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => false);
        return ws.ValidateEquateReferences(ws.PerFile[Path]);
    }

    [Fact]
    public void Workspace_diagnostic_carries_the_hint()
    {
        var diags = WorkspaceDiagnostics(Text);
        Assert.Equal(["did you mean 'a1'?", "did you mean 'c3'?"], diags.Select(d => d.Hint));
        Assert.All(diags, d => Assert.True(d.HintNeedsReview));
    }

    [Fact]
    public void Ctrl_dot_offers_the_rename_for_one_diagnostic()
    {
        var d = WorkspaceDiagnostics(Text)[0];

        var fix = Assert.Single(CodeFixes.ForLine(Text, d.Span.Start.Line, [d]), f => f.TitleKey == "Fix_Rename");
        Assert.Equal(["a2", "a1"], fix.TitleArgs);
        Assert.Equal(CodeFixes.UnresolvedStation, fix.DiagnosticCode);
        Assert.False(fix.SafeForBatch);
        Assert.Contains("equate b7 a1", TextSplicer.Splice(Text, fix.Edits)!);
    }

    [Fact]
    public void Fix_all_does_not_apply_the_rename()
    {
        var diags = WorkspaceDiagnostics(Text);

        Assert.All(diags, d => Assert.False(CodeFixes.CanBatchFix(d)));
        Assert.All(diags, d => Assert.Null(CodeFixes.ForDiagnostic(Text, d)));
        var (batch, fixedCount, skipped) = FixAll.Plan(CodeFixes.UnresolvedStation, diags, _ => Text);
        Assert.Equal((0, 2), (fixedCount, skipped));
        Assert.Empty(batch.Edits);
    }
}
