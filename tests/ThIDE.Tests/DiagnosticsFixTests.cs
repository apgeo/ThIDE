using AvaloniaEdit.Document;
using Therion.Core;
using ThIDE.Editor;
using ThIDE.Resources;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class DiagnosticsFixTests
{
    private static Diagnostic D(string code, int line)
    {
        var at = new SourceLocation(line, 1);
        return Diagnostic.Create(code, DiagnosticSeverity.Error, "x", new SourceSpan("/c/a.th", at, at, 0, 0));
    }

    [Fact]
    public void Fix_all_is_offered_only_for_codes_with_a_batch_fix_and_names_the_code()
    {
        var vm = new DiagnosticsViewModel();
        vm.Load([D("TH0020", 3), D("TH0010", 4)]);
        string? requested = null;
        vm.FixAllRequested += (_, code) => requested = code;

        vm.Selected = vm.Rows.Single(r => r.Code == "TH0010");
        Assert.False(vm.CanFixAllSelected);

        vm.Selected = vm.Rows.Single(r => r.Code == "TH0020");
        Assert.True(vm.CanFixAllSelected);
        Assert.Contains("TH0020", vm.FixAllHeader);
        vm.FixAllSelectedCodeCommand.Execute(null);
        Assert.Equal("TH0020", requested);
    }

    [Fact]
    public void Fix_hands_over_the_original_diagnostic()
    {
        var vm = new DiagnosticsViewModel();
        var d = D("TH0020", 3);
        vm.Load([d]);
        Diagnostic? got = null;
        vm.FixRequested += (_, x) => got = x;

        vm.Selected = vm.Rows.Single();
        vm.FixSelectedCommand.Execute(null);

        Assert.Same(d, got);
    }

    [Fact]
    public void Editor_quick_fix_on_a_zero_length_leg_offers_the_equate_first()
    {
        // Invented survey: B is reached only through the zero-length leg A B.
        var doc = new TextDocument("survey s\n  centreline\n    A B 0 0 0\n    B - 1.2 10 0\n    A C 3 10 0\n  endcentreline\nendsurvey\n");
        int caret = doc.GetLineByNumber(3).Offset + 6;

        // The editor holds only the file's own diagnostics: the project-level TH_SEM_012 is not among them.
        var fixes = TherionCodeActions.Build(doc, [], caret, "/c/a.th");

        Assert.Equal([string.Format(Tr.Get("Fix_Equate"), "A", "B"), string.Format(Tr.Get("Fix_CommentOutDisconnects"), "B")],
            fixes.Select(f => f.Title));
        fixes[0].Apply();
        Assert.Contains("\n    equate A B\n    B - 1.2", doc.Text);
    }
}
