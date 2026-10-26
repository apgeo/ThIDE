using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Every option value lands in a Therion command line (the thconfig is a script Therion runs), so a
// value that could add a line, close a quote or climb out of the project is refused by all builders.
public class TopodroidScaffoldValidationTests
{
    private static ScaffoldOptions Valid() => new()
    {
        ProjectName = "probe",
        InnerSurveyName = "invented",
        SourceFileName = "invented.th",
        Title = "Invented Cave",
        EntranceStation = "0",
        CoordinateSystem = "lat-long",
        FixC1 = "46.0000", FixC2 = "24.0000", FixC3 = "500",
        Exports = new[] { new ExportItem(ExportKind.Model, "loch", ".lox") },
    };

    public static TheoryData<string, string> BadValues() => new()
    {
        { "Encoding", "utf-8\nsystem \"touch PWNED\"" },
        { "Encoding", "utf 8" },
        { "Encoding", "utf-8\"" },
        { "OutputDir", "rez\"\nsystem \"touch PWNED\"\n#" },
        { "OutputDir", ".." },
        { "OutputDir", "rez/../.." },
        { "OutputDir", "/tmp/out" },
        { "OutputDir", "C:/out" },
        { "OutputDir", "rez\\out" },
        { "OutputDir", "." },
        { "OutputDir", "rez//x" },
        { "SourceDir", ".." },
        { "SourceDir", "th/\"" },
        { "SourceDir", "/etc" },
        { "GraphicsDir", "../g" },
        { "OutputBaseName", "../x" },
        { "OutputBaseName", "x\"\nsystem \"touch PWNED\"" },
        { "OutputBaseName", ".." },
        { "Title", "a\nendsurvey" },
        { "CoordinateSystem", "lat-long\n  fix 0 0 0 0" },
        { "CoordinateSystem", "\"lat-long\"" },
        { "CoordinateSystem", ".." + "/x y" },
        { "FixC1", "46\nsystem x" },
        { "FixC1", "46,5" },
        { "FixC2", "east" },
        { "FixC3", "500 # x" },
        { "EntranceStation", "0\n" },
        { "EntranceStation", "0\"" },
        { "EntranceStation", "0 x" },
        { "EntranceStation", "-0" },
        { "InnerSurveyName", "inv\nsystem" },
        { "InnerSurveyName", "inv\"" },
        { "InnerSurveyName", "a.b" },
        { "InnerSurveyName", "a@b" },
        { "SourceFileName", "in\"vented.th" },
        { "SourceFileName", "in\nvented.th" },
    };

    private static ScaffoldOptions With(string field, string value) => field switch
    {
        "Encoding" => Valid() with { Encoding = value },
        "OutputDir" => Valid() with { OutputDir = value },
        "SourceDir" => Valid() with { SourceDir = value },
        "GraphicsDir" => Valid() with { GraphicsDir = value, CreateGraphicsDir = true },
        "OutputBaseName" => Valid() with { OutputBaseName = value },
        "Title" => Valid() with { Title = value },
        "CoordinateSystem" => Valid() with { CoordinateSystem = value },
        "FixC1" => Valid() with { FixC1 = value },
        "FixC2" => Valid() with { FixC2 = value },
        "FixC3" => Valid() with { FixC3 = value },
        "EntranceStation" => Valid() with { EntranceStation = value },
        "InnerSurveyName" => Valid() with { InnerSurveyName = value },
        "SourceFileName" => Valid() with { SourceFileName = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    [Theory]
    [MemberData(nameof(BadValues))]
    public void Every_builder_refuses_a_value_that_cannot_be_written_as_itself(string field, string value)
    {
        var o = With(field, value);
        Assert.Throws<ArgumentException>(() => TopodroidProjectScaffold.BuildPlan(o));
        Assert.Throws<ArgumentException>(() => TopodroidProjectScaffold.BuildConnectionTh(o));
        Assert.Throws<ArgumentException>(() => TopodroidProjectScaffold.BuildThconfig(o));
        Assert.Throws<ArgumentException>(() => TopodroidProjectScaffold.ExpectedOutputs(o));
    }

    [Theory]
    [InlineData("SourceDir", ".")]
    [InlineData("SourceDir", "data/th")]
    [InlineData("OutputDir", "out/maps")]
    [InlineData("OutputBaseName", "cave-2026.v1")]
    [InlineData("CoordinateSystem", "EPSG:31700")]
    [InlineData("CoordinateSystem", "UTM35N")]
    [InlineData("FixC1", "-46.5e0")]
    [InlineData("EntranceStation", "5@sub")]
    [InlineData("EntranceStation", "1.2@deeper.sub")]
    [InlineData("Encoding", "iso8859-2")]
    [InlineData("Title", "Peștera \"Mare\" #1")]
    [InlineData("SourceFileName", "invented cave.th")]
    public void Ordinary_values_are_accepted(string field, string value)
    {
        var plan = TopodroidProjectScaffold.BuildPlan(With(field, value));
        Assert.NotEmpty(plan.Files);
    }

    [Fact]
    public void A_quote_in_the_title_is_written_as_therions_doubled_quote()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(Valid() with { Title = "Say \"hi\"" });
        Assert.Contains("survey probe -title \"Say \"\"hi\"\"\" -entrance", th);
    }

    [Fact]
    public void A_title_read_from_a_survey_goes_back_out_with_its_quotes_escaped()
    {
        const string survey = """
            survey invented -title "a ""b"" c"
              centerline
                0 1 5.0 90.0 0.0
              endcenterline
            endsurvey
            """;
        var header = TopodroidSurveyHeaderReader.Read(survey);
        Assert.Equal("a \"b\" c", header.Title);
        Assert.Equal("a \"b\" c", TopodroidProjectScaffold.Parse(survey).Title);

        var th = TopodroidProjectScaffold.BuildConnectionTh(Valid() with { Title = header.Title });
        Assert.Contains("-title \"a \"\"b\"\" c\"", th);
    }

    [Fact]
    public void A_station_in_a_nested_survey_is_referenced_through_it()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(Valid() with { EntranceStation = "5@sub" });
        Assert.Contains("-entrance 5@sub.invented", th);
        Assert.Contains("fix 5@sub.invented 46.0000 24.0000 500", th);
    }

    [Fact]
    public void A_survey_with_its_own_fix_gets_no_second_one()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(Valid() with { SurveyHasFix = true, FixC1 = "", FixC2 = "" });

        Assert.DoesNotContain("cs ", th);
        Assert.DoesNotContain("fix ", th);
        Assert.Contains("georeferenced by its own fix", th);
        Assert.Contains("-entrance 0@invented", th);
    }

    [Fact]
    public void Coordinates_given_for_a_survey_with_its_own_fix_are_refused()
    {
        var o = Valid() with { SurveyHasFix = true };
        var ex = Assert.Throws<ArgumentException>(() => TopodroidProjectScaffold.BuildPlan(o));
        Assert.Contains("second fix", ex.Message);
        Assert.Throws<ArgumentException>(() => TopodroidProjectScaffold.BuildConnectionTh(o));
    }
}
