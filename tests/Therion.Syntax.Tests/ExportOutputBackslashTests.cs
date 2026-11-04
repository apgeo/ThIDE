// `export … -output rez\a.lox` written on Windows: off Windows Therion writes a file literally named
// `rez\a.lox` into the working folder. The parser warns; '/' spellings and rooted paths are left alone.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ExportOutputBackslashTests
{
    private static Diagnostic[] Diags(string text) =>
        new ThconfigParser().Parse("x.thconfig", text).Diagnostics
            .Where(d => d.Code.Value == DiagnosticCodes.BackslashOutputPath).ToArray();

    [Fact]
    public void A_backslash_output_path_is_reported_with_the_portable_spelling()
    {
        const string text = "source main.th\nexport model -fmt loch -output rez\\cave.lox\n";
        var d = Assert.Single(Diags(text));

        Assert.Equal(OperatingSystem.IsWindows() ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, d.Severity);
        Assert.StartsWith("Output path 'rez\\cave.lox'", d.Message);
        Assert.Contains("'rez/cave.lox'", d.Hint);
        Assert.Equal(2, d.Span.Start.Line);
        Assert.Equal("rez\\cave.lox", text.Substring(d.Span.StartOffset, d.Span.Length));
    }

    [Fact]
    public void A_quoted_path_and_the_short_option_are_reported_too()
    {
        Assert.Single(Diags("export map -o \"out dir\\plan.pdf\"\n"));
        Assert.Single(Diags("export map -output out\\plan.pdf -layout l1\n"));
    }

    [Theory]
    [InlineData("export model -output rez/cave.lox")]
    [InlineData("export model -output C:\\caves\\cave.lox")]
    [InlineData("export model -output \\\\server\\cave.lox")]
    [InlineData("export model -fmt loch")]
    public void Portable_rooted_or_missing_paths_are_not_reported(string line) =>
        Assert.Empty(Diags(line + "\n"));
}
