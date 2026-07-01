using System.Linq;
using Therion.Lsp;

namespace TherionProc.Tests;

// the LSP server's diagnostic provider maps Therion diagnostics to the LSP shape.
public class LspDiagnosticProviderTests
{
    [Fact]
    public void Clean_file_has_no_diagnostics()
    {
        const string src = """
            survey cave
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
              endcentreline
            endsurvey
            """;
        Assert.Empty(DiagnosticProvider.Compute("cave.th", src));
    }

    [Fact]
    public void Diagnostics_are_mapped_to_the_lsp_shape()
    {
        // Missing endsurvey → at least one parser/semantic diagnostic.
        const string src = "survey cave\n  centreline\n  endcentreline\n";
        var diags = DiagnosticProvider.Compute("cave.th", src);

        Assert.NotEmpty(diags);
        Assert.All(diags, d =>
        {
            Assert.InRange(d.Severity, 1, 4);              // 1=Error … 4=Hint
            Assert.True(d.Range.Start.Line >= 0);          // 0-based
            Assert.True(d.Range.End.Character > d.Range.Start.Character || d.Range.End.Line > d.Range.Start.Line);
            Assert.False(string.IsNullOrEmpty(d.Message));
            Assert.Equal("therion", d.Source);
        });
    }
}
