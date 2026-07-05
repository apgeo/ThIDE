using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ThParserSmokeTests
{
    [Fact]
    public void Parses_minimal_survey_with_centreline_and_data()
    {
        const string text = """
            # cave file
            survey upper -title "Upper passages"
              centreline
                date 2024.01.15
                team "Alice" instruments
                data normal from to length compass clino
                  1 2 12.5 0 -5
                  2 3 8.0 90 0
              endcentreline
            endsurvey
            """;

        var parser = new ThParser();
        var r = parser.Parse("cave.th", text);

        Assert.NotNull(r.Value);
        Assert.False(r.HasErrors);

        var survey = r.Value!.Children.OfType<SurveyCommand>().Single();
        Assert.Equal("upper", survey.Name);

        var cl = survey.Children.OfType<CentrelineCommand>().Single();
        Assert.Contains(cl.Children, c => c is DataCommand);
        Assert.Contains(cl.Children, c => c is DateCommand);
        Assert.Contains(cl.Children, c => c is TeamCommand);
    }
    
    [Fact]
    public void Parses_semicomplex_survey_with_centreline_and_data_1()
    {
        // Resolve the committed corpus file relative to the test assembly so this runs on any
        // machine/CI checkout (the whole synthetic corpus is also swept by SyntheticCorpusTests).
        var filePath = LocateSyntheticCorpusFile(Path.Combine("project", "av_cerbul_de_aur.th"));
        Assert.True(filePath is not null, "Could not locate tests/Corpus/Synthetic/project/av_cerbul_de_aur.th");
        string text = EncodingResolver.ReadAllText(filePath!);

        var parser = new ThParser();
        var r = parser.Parse(filePath!, text);

        Assert.NotNull(r.Value);
        Assert.False(r.HasErrors);

        var survey = r.Value!.Children.OfType<SurveyCommand>().Single();
        Assert.Equal("av_cerbul_de_aur_2025", survey.Name);

        var cl = survey.Children.OfType<CentrelineCommand>().Single();
        Assert.Contains(cl.Children, c => c is DataCommand);
        Assert.Contains(cl.Children, c => c is DateCommand);
    }

    // Walk up from the test assembly to find a file under tests/Corpus/Synthetic (mirrors
    // SyntheticCorpusTests.LocateCorpusRoot) so tests don't depend on an absolute checkout path.
    private static string? LocateSyntheticCorpusFile(string relativeToSynthetic)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Corpus", "Synthetic", relativeToSynthetic);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Fix_command_extracts_coordinates()
    {
        var parser = new ThParser();
        var r = parser.Parse("x.th", "fix S1 100.5 200.0 -3.25");

        var fix = r.Value!.Children.OfType<StationFix>().Single();
        Assert.Equal("S1", fix.Station);
        Assert.Equal(100.5, fix.X);
        Assert.Equal(200.0, fix.Y);
        Assert.Equal(-3.25, fix.Z);
    }

    [Fact]
    public void Equate_collects_all_station_names()
    {
        var parser = new ThParser();
        var r = parser.Parse("x.th", "equate a.1 b.2 c.3");

        var eq = r.Value!.Children.OfType<EquateCommand>().Single();
        Assert.Equal(new[] { "a.1", "b.2", "c.3" }, eq.Stations);
    }

    [Fact]
    public void Unterminated_survey_is_warning_in_lenient_mode()
    {
        const string text = "survey forgotten\n  date 2024.01.01\n";
        var parser = new ThParser();
        var r = parser.Parse("x.th", text);

        Assert.False(r.HasErrors);
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnterminatedBlock);
    }

    [Fact]
    public void Unterminated_survey_is_error_in_strict_mode()
    {
        const string text = "survey forgotten\n  date 2024.01.01\n";
        var parser = new ThParser();
        var r = parser.Parse("x.th", text, new ParserOptions(ParserMode.Strict));
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Unknown_command_becomes_UnknownCommand_in_lenient_mode()
    {
        var parser = new ThParser();
        var r = parser.Parse("x.th", "frobnicate one two");

        Assert.False(r.HasErrors);
        Assert.Single(r.Value!.Children.OfType<UnknownCommand>());
    }

    [Fact]
    public void Input_command_unquotes_path()
    {
        var parser = new ThParser();
        var r = parser.Parse("x.th", "input \"sub/file.th\"");

        var inp = r.Value!.Children.OfType<InputCommand>().Single();
        Assert.Equal("sub/file.th", inp.Path);
    }
}

public class RustcFormatterTests
{
    [Fact]
    public void Formats_diagnostic_with_code_message_and_location()
    {
        var span = new SourceSpan("x.th", new SourceLocation(2, 3),
            new SourceLocation(2, 8), StartOffset: 10, Length: 5);
        var d = Diagnostic.Create("TH0010", DiagnosticSeverity.Warning, "test message", span,
            hint: "did you mean 'survey'?");

        var output = new RustcStyleDiagnosticFormatter().Format(d);
        Assert.Contains("warning TH0010: test message", output);
        Assert.Contains("--> x.th:2:3", output);
    }
}
