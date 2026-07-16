using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class ExportToolsTests
{
    [Fact]
    public async Task Exports_need_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new ExportTools(host, new MutationEngine(host));

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ExportGis()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ExportTables()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.GenerateReport()).Error!.Code);
    }

    // ---- export_gis -----------------------------------------------------------------------------

    /// <summary>Nothing is fixed under a `cs`, so there is nothing to place on a map. Say so.</summary>
    [Fact]
    public async Task Gis_export_of_an_ungeoreferenced_project_is_refused()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportGis("geoJson");

        Assert.Equal(ToolErrorCodes.NothingToExport, result.Error!.Code);
        Assert.Contains("fix", result.Error.Message);
    }

    [Theory]
    [InlineData("csv", "upper.1")]
    [InlineData("geoJson", "FeatureCollection")]
    [InlineData("gpx", "<wpt")]
    [InlineData("kml", "<Placemark")]
    public async Task Gis_export_returns_the_document_for_each_format(string format, string expected)
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();   // upper.1 is fixed under cs UTM33
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportGis(format);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Null(result.Data!.Mutation);
        Assert.Contains(expected, result.Data.Text);
    }

    [Fact]
    public async Task An_unknown_gis_format_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportGis("shapefile");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("geoJson", result.Error.Message);
    }

    [Fact]
    public async Task Gis_export_writes_its_target()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportGis("csv", target: "out/points.csv", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Null(result.Data!.Text);
        Assert.Equal("out/points.csv", result.Data.Target);
        Assert.Equal("create", result.Data.Mutation!.Files[0].Action);
        Assert.Contains("upper.1", File.ReadAllText(fixture.PathTo("out", "points.csv")));
    }

    /// <summary>An export is a generated artifact: regenerating it is the normal case, unlike a scaffold.</summary>
    [Fact]
    public async Task Re_exporting_replaces_the_previous_artifact()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);
        await tools.ExportGis("csv", target: "out/points.csv", dryRun: false);

        var again = await tools.ExportGis("csv", target: "out/points.csv", dryRun: false);

        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal("replace", again.Data!.Mutation!.Files[0].Action);
    }

    [Fact]
    public async Task Export_dry_run_writes_nothing_but_shows_the_plan()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportGis("csv", target: "out/points.csv");

        Assert.True(result.Data!.Mutation!.DryRun);
        Assert.NotNull(result.Data.Text);
        Assert.False(File.Exists(fixture.PathTo("out", "points.csv")));
    }

    [Fact]
    public async Task Export_refuses_a_target_outside_the_workspace()
    {
        using var fixture = FixtureWorkspace.CreateDisconnected();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportGis("csv", target: "../../points.csv", dryRun: false);

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    // ---- export_tables --------------------------------------------------------------------------

    [Theory]
    [InlineData("stations", "csv", "Station,Kind,File,Line")]
    [InlineData("shots", "csv", "From,To,Length")]
    [InlineData("stations", "markdown", "| Station |")]
    [InlineData("stations", "html", "<table>")]
    [InlineData("stations", "latex", "\\begin{tabular}")]
    public async Task Tables_render_in_each_format(string kind, string format, string expected)
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportTables(kind, format);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Contains(expected, result.Data!.Text);
    }

    [Theory]
    [InlineData("passages", "csv")]
    [InlineData("stations", "pdf")]
    public async Task An_unknown_table_or_format_is_refused(string kind, string format)
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.ExportTables(kind, format)).Error!.Code);
    }

    [Fact]
    public async Task Tables_write_their_target()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ExportTables("shots", "csv", target: "out/shots.csv", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Contains("From,To,Length", File.ReadAllText(fixture.PathTo("out", "shots.csv")));
    }

    // ---- generate_report ------------------------------------------------------------------------

    [Fact]
    public async Task Report_is_a_standalone_html_document()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GenerateReport(projectName: "Meziad");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("html", result.Data!.Format);
        Assert.Contains("<html", result.Data.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meziad", result.Data.Text);
    }

    [Fact]
    public async Task Report_writes_its_target_and_can_be_regenerated()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        await tools.GenerateReport(target: "out/report.html", dryRun: false);
        var again = await tools.GenerateReport(target: "out/report.html", dryRun: false);

        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal("replace", again.Data!.Mutation!.Files[0].Action);
        Assert.Contains("<html", File.ReadAllText(fixture.PathTo("out", "report.html")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_document_larger_than_the_budget_is_capped_and_flagged()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GenerateReport(maxBytes: 40);

        Assert.True(result.Data!.Truncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Data.Text!) <= 40);
    }

    private static async Task<ExportTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new ExportTools(host, new MutationEngine(host));
    }
}
