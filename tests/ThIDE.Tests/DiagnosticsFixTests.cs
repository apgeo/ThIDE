using Therion.Core;
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
}
