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

    [Fact]
    public void Captures_full_thconfig_extension_not_just_dot_th()
    {
        // The ".thconfig" extension must be captured whole — the old regex matched only ".th",
        // producing a link to a non-existent "cave.th" (#1).
        var result = _parser.Classify("configuration file: cave.thconfig", isStderr: false);
        Assert.NotNull(result.Span);
        Assert.Equal("cave.thconfig", result.Span!.Value.FilePath);
    }

    [Fact]
    public void Captures_full_3dmf_extension_not_just_dot_3d()
    {
        var result = _parser.Classify("writing cave.3dmf ... done", isStderr: false);
        Assert.NotNull(result.Span);
        Assert.Equal("cave.3dmf", result.Span!.Value.FilePath);
    }

    [Theory]
    [InlineData("writing output/cave.dlp ... done", "output/cave.dlp")]
    [InlineData("writing cave.drml ... done", "cave.drml")]
    [InlineData("writing cave.kml ... done", "cave.kml")]
    [InlineData("writing outputs/model.lox ... done", "outputs/model.lox")]
    [InlineData(@"writing C:\caves\cave.dxf ... done", @"C:\caves\cave.dxf")]
    public void Detects_output_artifact_paths_of_various_extensions(string line, string expected)
    {
        var result = _parser.Classify(line, isStderr: false);
        Assert.NotNull(result.Span);
        Assert.Equal(expected, result.Span!.Value.FilePath);
    }

    [Fact]
    public void Captures_absolute_path_with_spaces_and_mixed_separators()
    {
        // "<label> file: <path>" lines print the path as the whole remainder — it must be captured
        // in full even with a space ("Program Files") and mixed \ and / separators (#1).
        const string line = @"initialization file: C:\Program Files\Therion/therion.ini";
        var result = _parser.Classify(line, isStderr: false);
        Assert.NotNull(result.Span);
        Assert.Equal(@"C:\Program Files\Therion/therion.ini", result.Span!.Value.FilePath);
    }

    [Fact]
    public void Captures_spaced_configuration_file_path()
    {
        const string line = @"configuration file: C:\My Caves\proj 1\cave.thconfig";
        var result = _parser.Classify(line, isStderr: false);
        Assert.NotNull(result.Span);
        Assert.Equal(@"C:\My Caves\proj 1\cave.thconfig", result.Span!.Value.FilePath);
    }

    [Fact]
    public void Does_not_hyperlink_a_dotted_survey_name()
    {
        // A fully-qualified survey/station name is not a file: no separator and an unknown extension.
        var result = _parser.Classify("removed 2 duplicate shots in main.upper", isStderr: false);
        Assert.Null(result.Span);
    }

    [Fact]
    public void Does_not_hyperlink_a_decimal_number()
    {
        var result = _parser.Classify("average loop error is 0.42 percent", isStderr: false);
        Assert.Null(result.Span);
    }
}
