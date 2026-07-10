using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using Therion.Workspace.Import;

namespace Therion.Mcp.Tests;

public class ImportToolsTests
{
    /// <summary>The fixtures from tests/Therion.Workspace.Tests, so the tool is held to the library's output.</summary>
    private const string Svx = """
        *begin cave
        *title "Test Cave"
        *fix entrance 0 0 0
        *data normal from to tape compass clino
        1 2 10.0 100 -5
        2 3 8.5 110 0
        *end cave
        """;

    private const string Dat =
        "Test Cave\r\n" +
        "SURVEY NAME: A\r\n" +
        "SURVEY DATE: 7 1 2024  COMMENT:hi\r\n" +
        "SURVEY TEAM:\r\n" +
        "Alice;Bob\r\n" +
        "DECLINATION: 2.50  FORMAT: DDDDLUDRADLN  CORRECTIONS: 0 0 0\r\n" +
        "\r\n" +
        "FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT FLAGS COMMENTS\r\n" +
        "\r\n" +
        "1 2 10.00 100.0 -5.0 1.0 2.0 -999.0 1.5\r\n" +
        "2 3 8.50 110.0 0.0 1.0 2.0 0.5 1.5\r\n";

    private const string Gpx =
        "<?xml version=\"1.0\"?>\n" +
        "<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\">\n" +
        "  <wpt lat=\"46.5\" lon=\"8.0\"><name>Entrance A</name><ele>1850.0</ele></wpt>\n" +
        "  <wpt lat=\"46.51\" lon=\"8.01\"><name>P2</name></wpt>\n" +
        "</gpx>\n";

    [Fact]
    public async Task Import_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();

        var result = await new ImportTools(host, new MutationEngine(host)).ImportSurvey("raw/a.svx");

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    /// <summary>The tool must not reformat, re-order or otherwise "improve" what the importer produced.</summary>
    [Theory]
    [InlineData("raw/cave.svx", Svx)]
    [InlineData("raw/cave.dat", Dat)]
    [InlineData("raw/points.gpx", Gpx)]
    public async Task Generated_text_is_identical_to_a_direct_library_call(string path, string content)
    {
        using var fixture = WithSource(path, content);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey(path);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(Expected(path, content), result.Data!.Text);
        Assert.False(result.Data.Truncated);
    }

    [Theory]
    [InlineData("raw/cave.svx", "survex")]
    [InlineData("raw/cave.dat", "compass")]
    [InlineData("raw/points.gpx", "gpx")]
    public async Task Format_comes_from_the_extension(string path, string expected)
    {
        using var fixture = WithSource(path, SourceFor(path));
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey(path);

        Assert.Equal(expected, result.Data!.Format);
    }

    /// <summary>A Survex file with the wrong extension still imports when the caller says so.</summary>
    [Fact]
    public async Task An_explicit_format_overrides_the_extension()
    {
        using var fixture = WithSource("raw/cave.txt", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var byExtension = await tools.ImportSurvey("raw/cave.txt");
        var explicitly = await tools.ImportSurvey("raw/cave.txt", format: "survex");

        Assert.Equal(ToolErrorCodes.InvalidArgument, byExtension.Error!.Code);
        Assert.Contains("Name the format", byExtension.Error.Message);
        Assert.True(explicitly.Ok);
        Assert.Contains("survey cave", explicitly.Data!.Text);
    }

    [Fact]
    public async Task An_unknown_format_name_is_refused()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", format: "walls");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("compass, survex, gpx", result.Error.Message);
    }

    [Fact]
    public async Task Gpx_uses_the_survey_name_it_is_given()
    {
        using var fixture = WithSource("raw/points.gpx", Gpx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/points.gpx", surveyName: "entrances");

        Assert.Contains("survey entrances", result.Data!.Text);
        Assert.Contains("cs lat-long", result.Data.Text);
    }

    [Fact]
    public async Task Dry_run_with_a_target_shows_the_plan_and_writes_nothing()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", target: "caves/imported.th");

        Assert.True(result.Ok);
        Assert.True(result.Data!.Mutation!.DryRun);
        Assert.Equal("caves/imported.th", result.Data.Target);
        Assert.NotNull(result.Data.Text);
        Assert.False(File.Exists(fixture.PathTo("caves", "imported.th")));
    }

    [Fact]
    public async Task Apply_writes_a_th_that_parses_clean()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", target: "caves/imported.th", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Null(result.Data!.Text);   // it is on disk now; do not send it twice
        Assert.False(result.Data.Mutation!.DryRun);

        var written = File.ReadAllText(fixture.PathTo("caves", "imported.th"));
        Assert.Equal(SurvexImporter.Import(Svx), written);
        Assert.Equal(0, Errors(written));
    }

    [Fact]
    public async Task Apply_never_overwrites_an_existing_file()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        File.WriteAllText(fixture.PathTo("caves", "imported.th"), "# mine\n");
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", target: "caves/imported.th", dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Equal("# mine\n", File.ReadAllText(fixture.PathTo("caves", "imported.th")));
    }

    [Fact]
    public async Task Writing_without_a_target_is_refused()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", dryRun: false);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("target", result.Error.Message);
    }

    [Fact]
    public async Task A_target_that_is_not_a_th_is_refused()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", target: "caves/imported.txt", dryRun: false);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    /// <summary>
    /// GpxImporter.Parse swallows malformed XML and returns no waypoints, so a naive wrapper would
    /// translate garbage into an empty-but-valid survey and report success.
    /// </summary>
    [Theory]
    [InlineData("this is not xml at all")]
    [InlineData("<?xml version=\"1.0\"?><gpx version=\"1.1\"></gpx>")]
    public async Task Gpx_with_no_waypoints_is_refused_rather_than_yielding_an_empty_survey(string content)
    {
        using var fixture = WithSource("raw/broken.gpx", content);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/broken.gpx");

        Assert.Equal(ToolErrorCodes.ImportFailed, result.Error!.Code);
        Assert.Contains("no waypoints", result.Error.Message);
    }

    [Fact]
    public async Task An_import_that_yields_nothing_is_reported()
    {
        using var fixture = WithSource("raw/empty.svx", "");
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/empty.svx");

        Assert.Equal(ToolErrorCodes.ImportFailed, result.Error!.Code);
        Assert.Contains("no survey data", result.Error.Message);
    }

    [Fact]
    public async Task Text_is_capped_and_flagged_at_the_byte_budget()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey("raw/cave.svx", maxBytes: 20);

        Assert.True(result.Data!.Truncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Data.Text!) <= 20);
    }

    [Theory]
    [InlineData("../../elsewhere.svx", null)]
    [InlineData("raw/cave.svx", "../../out.th")]
    public async Task Paths_outside_the_workspace_are_refused(string source, string? target)
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ImportSurvey(source, target);

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    [Fact]
    public async Task A_missing_source_is_reported()
    {
        using var fixture = WithSource("raw/cave.svx", Svx);
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.FileNotFound, (await tools.ImportSurvey("raw/nope.svx")).Error!.Code);
    }

    private static string SourceFor(string path) => Path.GetExtension(path) switch
    {
        ".svx" => Svx,
        ".dat" => Dat,
        _ => Gpx,
    };

    private static string Expected(string path, string content) => Path.GetExtension(path) switch
    {
        ".svx" => SurvexImporter.Import(content),
        ".dat" => CompassImporter.Import(content),
        _ => GpxImporter.ToTherion(content, "gps"),
    };

    private static int Errors(string therion) =>
        new Therion.Syntax.ThParser().Parse("imported.th", therion)
            .Diagnostics.Count(d => d.Severity == Therion.Core.DiagnosticSeverity.Error);

    private static FixtureWorkspace WithSource(string relativePath, string content)
    {
        var fixture = FixtureWorkspace.Create();
        var full = fixture.PathTo(relativePath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return fixture;
    }

    private static async Task<ImportTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new ImportTools(host, new MutationEngine(host));
    }
}
