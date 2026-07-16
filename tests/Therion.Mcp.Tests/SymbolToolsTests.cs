using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class SymbolToolsTests
{
    [Fact]
    public async Task Symbol_tools_need_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new SymbolTools(host);

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ListSymbols()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.GotoDefinition("upper")).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.FindReferences("upper")).Error!.Code);
    }

    [Fact]
    public async Task List_symbols_reports_surveys_and_stations_with_qualified_names()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListSymbols();

        Assert.True(result.Ok);
        var names = result.Data!.Symbols.Select(s => $"{s.Kind}:{s.Name}").ToList();
        Assert.Contains("survey:upper", names);
        Assert.Contains("survey:lower", names);
        Assert.Contains("station:upper.1", names);
        Assert.Contains("station:lower.a", names);
    }

    [Fact]
    public async Task Symbols_carry_a_workspace_relative_declaration()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListSymbols(kind: "survey", nameContains: "upper");

        var survey = Assert.Single(result.Data!.Symbols);
        Assert.Equal("caves/upper.th", survey.Declaration!.File);
        Assert.Equal(1, survey.Declaration.Line);
    }

    [Fact]
    public async Task List_symbols_filters_by_kind_and_pages()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var surveys = await tools.ListSymbols(kind: "survey");
        var firstOnly = await tools.ListSymbols(kind: "survey", limit: 1);

        Assert.Equal(2, surveys.Data!.Total);
        Assert.All(surveys.Data.Symbols, s => Assert.Equal("survey", s.Kind));
        Assert.Single(firstOnly.Data!.Symbols);
        Assert.True(firstOnly.Data.Truncated);
    }

    [Fact]
    public async Task Unknown_symbol_kind_is_an_argument_error()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListSymbols(kind: "passage");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    /// <summary>The `point@survey` form is how Therion refers across files; it must resolve to the other file.</summary>
    [Fact]
    public async Task Goto_definition_resolves_an_at_qualified_station_across_files()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GotoDefinition("1@upper", kind: "station");

        Assert.True(result.Ok);
        Assert.Equal("caves/upper.th", result.Data!.File);
        Assert.Equal(4, result.Data.Line);
    }

    [Fact]
    public async Task Goto_definition_resolves_a_dotted_qualified_station()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GotoDefinition("upper.1");

        Assert.True(result.Ok);
        Assert.Equal("caves/upper.th", result.Data!.File);
    }

    [Fact]
    public async Task Goto_definition_reports_an_unknown_name_as_a_missing_symbol()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GotoDefinition("nosuchstation");

        Assert.Equal(ToolErrorCodes.SymbolNotFound, result.Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_name_is_an_argument_error(string name)
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.GotoDefinition(name)).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.FindReferences(name)).Error!.Code);
    }

    [Theory]
    [InlineData("passage")]
    [InlineData("2")]
    public async Task Unknown_reference_kind_is_an_argument_error(string kind)
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.GotoDefinition("upper", kind)).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.FindReferences("upper", kind)).Error!.Code);
    }

    /// <summary>
    /// The station is declared by a shot row in upper.th and mentioned by an equate in lower.th.
    /// Both occurrences must come back, exactly one of them flagged as the declaration.
    /// </summary>
    [Fact]
    public async Task Find_references_spans_files_and_marks_the_declaration()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindReferences("1@upper", kind: "station");

        Assert.True(result.Ok);
        Assert.Equal(2, result.Data!.Total);
        Assert.Equal("caves/upper.th", result.Data.Definition!.File);

        var declaration = Assert.Single(result.Data.References, r => r.IsDeclaration);
        Assert.Equal("caves/upper.th", declaration.Location.File);

        var usage = Assert.Single(result.Data.References, r => !r.IsDeclaration);
        Assert.Equal("caves/lower.th", usage.Location.File);
    }

    /// <summary>An equate is where a station is merged, not merely mentioned — the caller needs it named as such.</summary>
    [Fact]
    public async Task Find_references_reports_the_equate_that_aggregates_the_station()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindReferences("1@upper", kind: "station");

        var aggregation = Assert.Single(result.Data!.Aggregations);
        Assert.Equal("equate", aggregation.Kind);
        Assert.Equal("caves/lower.th", aggregation.Location.File);
    }

    /// <summary>Only the survey half of `1@upper` is a survey reference; resolving it must not land on the station.</summary>
    [Fact]
    public async Task Find_references_resolves_the_survey_half_of_an_at_reference()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindReferences("upper", kind: "survey");

        Assert.True(result.Ok);
        Assert.Contains(result.Data!.References, r => !r.IsDeclaration && r.Location.File == "caves/lower.th");
        Assert.Contains(result.Data.References, r => r.IsDeclaration && r.Location.File == "caves/upper.th");
    }

    /// <summary>Scope, not text: station `1` under `upper` is a different symbol from `1` under `lower`.</summary>
    [Fact]
    public async Task Stations_of_the_same_name_in_different_surveys_are_different_symbols()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        File.WriteAllText(fixture.PathTo("caves", "lower.th"), """
            survey lower
              centreline
                data normal from to length compass clino
                1 2 5.0 0 0
              endcentreline
            endsurvey
            """);
        var tools = await LoadedToolsAsync(fixture);

        var upper = await tools.FindReferences("1@upper", kind: "station");
        var lower = await tools.FindReferences("1@lower", kind: "station");

        Assert.Equal("caves/upper.th", upper.Data!.Definition!.File);
        Assert.Equal("caves/lower.th", lower.Data!.Definition!.File);
        Assert.All(upper.Data.References, r => Assert.Equal("caves/upper.th", r.Location.File));
        Assert.All(lower.Data.References, r => Assert.Equal("caves/lower.th", r.Location.File));
    }

    [Fact]
    public async Task Find_references_reports_an_unknown_name_as_a_missing_symbol()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FindReferences("nosuchstation");

        Assert.Equal(ToolErrorCodes.SymbolNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Find_references_pages_the_occurrence_list()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var page = await tools.FindReferences("1@upper", kind: "station", limit: 1);

        Assert.Single(page.Data!.References);
        Assert.Equal(2, page.Data.Total);
        Assert.True(page.Data.Truncated);
    }

    private static async Task<SymbolTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new SymbolTools(host);
    }
}
