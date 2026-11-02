using Therion.Core;
using Therion.Syntax;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>
/// View ▸ Strict parser mode must change how the app parses documents: an unterminated block is a
/// warning in lenient mode and an error in strict mode (the parsers already implement the split).
/// </summary>
[Collection(ParserOptionsHostCollection.Name)]
public class StrictParserModeTests
{
    [Fact]
    public void Strict_mode_toggle_escalates_parser_warnings_in_document_parsing()
    {
        var before = ParserOptionsHost.Current;
        try
        {
            var vm = new MainWindowViewModel();
            vm.StrictParserMode = true;

            var parsed = DocumentParser.ParseByExtension("x.th", "survey a\n  centerline\n  endcenterline\n");
            var unterminated = Assert.Single(parsed.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnterminatedBlock);

            Assert.True(unterminated.Severity == DiagnosticSeverity.Error,
                $"with strict parser mode on, the unterminated-block diagnostic should be an Error but is {unterminated.Severity}");
        }
        finally
        {
            ParserOptionsHost.Current = before;
        }
    }
}

/// <summary>
/// Tests that flip the process-wide <see cref="ParserOptionsHost.Current"/> run alone: every
/// document parse reads it, so a parallel test class would otherwise see strict-mode diagnostics.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ParserOptionsHostCollection
{
    public const string Name = "ParserOptionsHost";
}
