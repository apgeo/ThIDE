using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Workspace.Tests;

// The workspace parses with the options it was given, so a strict-mode host gets strict diagnostics.
public class WorkspaceParserOptionsTests
{
    private const string Unterminated = "survey a\n  centerline\n  endcenterline\n";

    [Fact]
    public void ParseText_defaults_to_lenient()
    {
        var parsed = TherionWorkspace.ParseText("x.th", Unterminated);
        var d = Assert.Single(parsed.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnterminatedBlock);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void ParseText_honours_strict_options()
    {
        var parsed = TherionWorkspace.ParseText("x.th", Unterminated, new ParserOptions(ParserMode.Strict));
        var d = Assert.Single(parsed.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnterminatedBlock);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public async System.Threading.Tasks.Task Buffer_overlay_uses_the_workspace_parser_options()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thide-strict-overlay.th"));
        await using var ws = new TherionWorkspace(options: new WorkspaceOptions { ParserOptions = new ParserOptions(ParserMode.Strict) });
        ws.UpdateFileFromText(path, Unterminated);
        var parsed = ws.TryGetFile(path);
        Assert.NotNull(parsed);
        Assert.Contains(parsed!.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnterminatedBlock && d.Severity == DiagnosticSeverity.Error);
    }
}
