using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

public class TherionOutputParserTests
{
    private readonly HeuristicTherionOutputParser _parser = new();

    [Fact]
    public void Parses_therion_native_error_with_file_line_and_symbol()
    {
        const string line =
            @"C:\Program Files\Therion\therion.exe: error -- PS-5nord/20161203_ovi/20161203_ps5.th2 [168] -- station does not exist -- E65a";

        var result = _parser.Classify(line, isStderr: true);

        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.Span);
        Assert.Equal("PS-5nord/20161203_ovi/20161203_ps5.th2", result.Span!.Value.FilePath);
        Assert.Equal(168, result.Span!.Value.Start.Line);
        Assert.Equal("E65a", result.Symbol);
    }

    [Fact]
    public void Parses_file_colon_line_format()
    {
        var result = _parser.Classify("foo/bar.th:42: warning -- something", isStderr: false);
        Assert.NotNull(result.Span);
        Assert.Equal("foo/bar.th", result.Span!.Value.FilePath);
        Assert.Equal(42, result.Span!.Value.Start.Line);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public void Detects_a_bare_path_with_no_line_info()
    {
        var result = _parser.Classify("can't open file for input -- Cerna_lox.th", isStderr: true);
        Assert.NotNull(result.Span);
        Assert.Equal("Cerna_lox.th", result.Span!.Value.FilePath);
        Assert.Equal(1, result.Span!.Value.Start.Line); // no line info → top of file
    }

    [Fact]
    public void Plain_text_line_has_no_span()
    {
        var result = _parser.Classify("Initialization done.", isStderr: false);
        Assert.Null(result.Span);
        Assert.Null(result.Symbol);
    }
}
