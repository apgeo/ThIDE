using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class StructuralToolsTests
{
    [Fact]
    public async Task Structural_analysis_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();

        var result = await new StructuralTools(host).StructuralAnalysis("caves/upper.th");

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    /// <summary>These are the numbers `therion-cli structural` prints for the same file.</summary>
    [Fact]
    public async Task Fits_a_plane_to_the_geo_shots()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("caves/upper.th");

        Assert.True(result.Ok);
        var plane = Assert.Single(result.Data!.Planes);
        Assert.Equal("upper.geo1", plane.Name);
        Assert.True(plane.Valid);
        Assert.Equal(72.368, plane.Dip!.Value, precision: 3);
        Assert.Equal(135.0, plane.Strike!.Value, precision: 3);
        Assert.Equal(225.0, plane.DipDirection!.Value, precision: 3);
        Assert.Equal(3, plane.PointCount);
    }

    [Fact]
    public async Task Azimuths_are_magnetic_north_until_a_declination_is_given()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("caves/upper.th");

        Assert.Equal(0, result.Data!.DeclinationApplied);
        Assert.Equal("none", result.Data.DeclinationSource);
    }

    /// <summary>A manual declination rotates every azimuth by exactly that much.</summary>
    [Fact]
    public async Task Manual_declination_rotates_the_azimuths()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var magnetic = await tools.StructuralAnalysis("caves/upper.th");
        var trueNorth = await tools.StructuralAnalysis("caves/upper.th", declination: "3.5");

        Assert.Equal(3.5, trueNorth.Data!.DeclinationApplied);
        Assert.Equal("manual", trueNorth.Data.DeclinationSource);
        Assert.Equal(magnetic.Data!.Planes[0].Strike!.Value + 3.5, trueNorth.Data.Planes[0].Strike!.Value, precision: 3);
        Assert.Equal(magnetic.Data.Planes[0].Dip!.Value, trueNorth.Data.Planes[0].Dip!.Value, precision: 3);
    }

    [Fact]
    public async Task A_declination_that_is_neither_survey_nor_a_number_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("caves/upper.th", declination: "north");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public async Task An_unmatched_keyword_finds_no_planes_and_is_not_an_error()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("caves/upper.th", keyword: "bedding");

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Planes);
    }

    [Fact]
    public async Task Refuses_a_path_outside_the_workspace()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("../../elsewhere.th");

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    [Fact]
    public async Task Refuses_a_file_that_is_not_a_th_survey()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("project.thconfig");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public async Task Refuses_a_file_the_project_does_not_include()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.StructuralAnalysis("caves/abandoned.th");

        Assert.Equal(ToolErrorCodes.FileNotFound, result.Error!.Code);
    }

    private static async Task<StructuralTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new StructuralTools(host);
    }
}
