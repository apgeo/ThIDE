using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class RenameToolsTests
{
    [Fact]
    public async Task Rename_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new RenameTools(host, new MutationEngine(host));

        var result = await tools.RenameSymbol("upper", "lower");

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task Dry_run_is_the_default_and_writes_nothing()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.RenameSymbol("upper", "haut", kind: "survey");

        Assert.True(result.Ok);
        Assert.True(result.Data!.Mutation.DryRun);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    [Fact]
    public async Task Dry_run_previews_every_file_the_rename_touches()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", "haut", kind: "survey");

        var files = result.Data!.Mutation.Files.Select(f => f.Path).OrderBy(p => p).ToList();
        Assert.Equal(["caves/lower.th", "caves/upper.th"], files);
        Assert.Contains(result.Data.Mutation.Files, f => f.Preview.Any(p => p.After.Contains("survey haut")));
    }

    /// <summary>The equate in lower.th says `1@upper`; renaming the survey must rewrite that half too.</summary>
    [Fact]
    public async Task Renaming_a_survey_rewrites_its_at_qualified_references_in_other_files()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", "haut", kind: "survey", dryRun: false);

        Assert.True(result.Ok);
        Assert.Contains("survey haut", File.ReadAllText(fixture.PathTo("caves", "upper.th")));
        Assert.Contains("equate 1@haut a@lower", File.ReadAllText(fixture.PathTo("caves", "lower.th")));
    }

    [Fact]
    public async Task Renaming_a_station_rewrites_its_declaration_and_its_equate_reference()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("1@upper", "entrance", kind: "station", dryRun: false);

        Assert.True(result.Ok);
        Assert.Equal("station", result.Data!.Kind);
        Assert.Equal("upper.1", result.Data.Symbol);
        Assert.Contains("entrance 2 10.0 90 0", File.ReadAllText(fixture.PathTo("caves", "upper.th")));
        Assert.Contains("equate entrance@upper a@lower", File.ReadAllText(fixture.PathTo("caves", "lower.th")));
    }

    /// <summary>Renaming must not break the project it just edited — that is what the lint evidence is for.</summary>
    [Fact]
    public async Task Applying_a_rename_leaves_the_project_no_worse()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", "haut", kind: "survey", dryRun: false);

        Assert.Equal(0, result.Data!.Mutation.NewErrors);
        Assert.Equal(0, result.Data.Mutation.NewWarnings);
    }

    /// <summary>Station `1` under `upper` is not station `1` under `lower`; only one may be rewritten.</summary>
    [Fact]
    public async Task Renaming_is_scope_correct_across_same_named_stations()
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

        await tools.RenameSymbol("1@upper", "entrance", kind: "station", dryRun: false);

        Assert.Contains("entrance 2 10.0", File.ReadAllText(fixture.PathTo("caves", "upper.th")));
        Assert.Contains("1 2 5.0", File.ReadAllText(fixture.PathTo("caves", "lower.th")));
    }

    [Fact]
    public async Task A_name_already_taken_in_the_same_scope_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.RenameSymbol("1@upper", "2", kind: "station", dryRun: false);

        Assert.Equal(ToolErrorCodes.NameCollision, result.Error!.Code);
        Assert.Contains("upper.2", result.Error.Message);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    /// <summary>The same name under a different survey is a different symbol, so it is not a collision.</summary>
    [Fact]
    public async Task A_name_taken_in_another_survey_is_not_a_collision()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("a@lower", "2", kind: "station", dryRun: false);

        Assert.True(result.Ok);
    }

    [Theory]
    [InlineData("a.b", "'.'")]
    [InlineData("a@b", "'@'")]
    [InlineData("a/b", "'/'")]
    [InlineData("a:b", "':'")]
    public async Task A_new_name_that_is_a_path_or_a_reference_is_refused(string newName, string expected)
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", newName, kind: "survey");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains(expected, result.Error.Message);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("what?")]
    [InlineData("a&b")]
    [InlineData("-entrance")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" trailing ")]
    public async Task An_illegal_new_name_is_refused(string newName)
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", newName, kind: "survey");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public async Task Renaming_a_symbol_to_its_own_name_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", "upper", kind: "survey");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public async Task An_unknown_symbol_is_reported_as_such()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("nosuchsurvey", "x", kind: "survey");

        Assert.Equal(ToolErrorCodes.SymbolNotFound, result.Error!.Code);
    }

    [Theory]
    [InlineData("map")]
    [InlineData("scrapObject")]
    public async Task Kinds_without_an_occurrence_index_are_refused(string kind)
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.RenameSymbol("upper", "haut", kind: kind);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    /// <summary>
    /// The plan's offsets came from a model built when the workspace was loaded. If the file moved
    /// underneath, applying would rewrite the wrong bytes — so the whole rename is refused.
    /// </summary>
    [Fact]
    public async Task A_file_edited_since_the_workspace_was_loaded_refuses_the_rename()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        var tools = await LoadedToolsAsync(fixture);

        var target = fixture.PathTo("caves", "upper.th");
        File.WriteAllText(target, "# a comment nobody planned for\n" + File.ReadAllText(target));
        var afterInterference = File.ReadAllText(target);

        var result = await tools.RenameSymbol("upper", "haut", kind: "survey", dryRun: false);

        Assert.Equal(ToolErrorCodes.StalePlan, result.Error!.Code);
        Assert.Equal(afterInterference, File.ReadAllText(target));
        // …and the other file in the plan is untouched too.
        Assert.Contains("equate 1@upper", File.ReadAllText(fixture.PathTo("caves", "lower.th")));
    }

    private static async Task<RenameTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new RenameTools(host, new MutationEngine(host));
    }
}
