using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class WorkspaceToolsTests
{
    [Fact]
    public async Task Workspace_info_says_so_when_nothing_is_loaded()
    {
        await using var host = new WorkspaceHost();
        var tools = new WorkspaceTools(host);

        var result = await tools.WorkspaceInfo();

        Assert.True(result.Ok);
        Assert.False(result.Data!.Loaded);
        Assert.Null(result.Data.Root);
    }

    [Fact]
    public async Task Load_workspace_reports_the_entry_point_and_its_file_graph()
    {
        using var fixture = FixtureWorkspace.Create();
        await using var host = new WorkspaceHost();
        var tools = new WorkspaceTools(host);

        var result = await tools.LoadWorkspace(fixture.Thconfig);

        Assert.True(result.Ok);
        var info = result.Data!;
        Assert.True(info.Loaded);
        Assert.Equal("project.thconfig", info.EntryPoint);
        // thconfig + upper.th; abandoned.th is unreachable.
        Assert.Equal(2, info.FileCount);
        Assert.Contains("project.thconfig", info.EntryPointCandidates);
    }

    [Fact]
    public async Task Load_workspace_accepts_a_project_folder()
    {
        using var fixture = FixtureWorkspace.Create();
        await using var host = new WorkspaceHost();
        var tools = new WorkspaceTools(host);

        var result = await tools.LoadWorkspace(fixture.Root);

        Assert.True(result.Ok);
        Assert.Equal("project.thconfig", result.Data!.EntryPoint);
    }

    [Fact]
    public async Task Load_workspace_reports_a_missing_path_as_an_error_not_an_exception()
    {
        await using var host = new WorkspaceHost();
        var tools = new WorkspaceTools(host);

        var result = await tools.LoadWorkspace(Path.Combine(Path.GetTempPath(), "no_such_project_" + Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.WorkspaceLoadFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Read_and_list_refuse_to_work_without_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new WorkspaceTools(host);

        var list = await tools.ListFiles();
        var read = await tools.ReadFile("anything.th");

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, list.Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, read.Error!.Code);
    }

    [Fact]
    public async Task List_files_returns_the_loaded_graph_relative_and_sorted()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListFiles();

        Assert.True(result.Ok);
        Assert.Equal(new[] { "caves/upper.th", "project.thconfig" }, result.Data!.Files);
        Assert.Equal(2, result.Data.Total);
        Assert.False(result.Data.Truncated);
    }

    [Fact]
    public async Task List_files_filters_by_extension_with_or_without_the_dot()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var dotted = await tools.ListFiles(extension: ".th");
        var bare = await tools.ListFiles(extension: "th");

        Assert.Equal(new[] { "caves/upper.th" }, dotted.Data!.Files);
        Assert.Equal(dotted.Data.Files, bare.Data!.Files);
    }

    [Fact]
    public async Task List_files_orphans_only_finds_the_file_no_thconfig_reaches()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListFiles(orphansOnly: true);

        Assert.Equal(new[] { "caves/abandoned.th" }, result.Data!.Files);
    }

    [Fact]
    public async Task List_files_pages_and_flags_truncation()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var first = await tools.ListFiles(limit: 1);

        Assert.Equal(new[] { "caves/upper.th" }, first.Data!.Files);
        Assert.Equal(2, first.Data.Total);
        Assert.True(first.Data.Truncated);

        var second = await tools.ListFiles(offset: 1, limit: 1);

        Assert.Equal(new[] { "project.thconfig" }, second.Data!.Files);
        Assert.False(second.Data.Truncated);
    }

    [Fact]
    public async Task Read_file_returns_the_whole_file_by_default()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ReadFile("caves/upper.th");

        Assert.True(result.Ok);
        Assert.Contains("survey upper", result.Data!.Text);
        Assert.False(result.Data.Truncated);
        Assert.Equal(result.Data.TotalLines, result.Data.LineCount);
    }

    [Fact]
    public async Task Read_file_slices_by_line_and_flags_the_remainder()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ReadFile("caves/upper.th", offset: 1, limit: 1);

        Assert.Equal(1, result.Data!.LineCount);
        Assert.Equal(1, result.Data.Offset);
        Assert.Contains("centreline", result.Data.Text);
        Assert.True(result.Data.Truncated);
    }

    [Fact]
    public async Task Read_file_truncates_at_the_byte_cap()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ReadFile("caves/upper.th", maxBytes: 10);

        Assert.True(result.Ok);
        Assert.True(result.Data!.Text.Length <= 10);
        Assert.True(result.Data.Truncated);
    }

    [Fact]
    public async Task Read_file_refuses_to_leave_the_workspace()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ReadFile("../../etc/passwd");

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    [Fact]
    public async Task Read_file_reports_a_missing_file_inside_the_workspace()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ReadFile("caves/nope.th");

        Assert.Equal(ToolErrorCodes.FileNotFound, result.Error!.Code);
    }

    /// <summary>
    /// Therion projects are full of Latin-1 files, declared by an <c>encoding</c> directive rather
    /// than sniffed. read_file must honour the directive, as the editor and the parser do.
    /// </summary>
    [Fact]
    public async Task Read_file_decodes_a_latin1_file_per_its_encoding_directive()
    {
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "grotte.th"),
            "encoding iso-8859-1\n# Grotte de Bédeilhac\nsurvey g\nendsurvey\n",
            System.Text.Encoding.Latin1);

        var tools = await LoadedToolsAsync(fixture);
        var result = await tools.ReadFile("caves/grotte.th");

        Assert.True(result.Ok);
        Assert.Contains("Bédeilhac", result.Data!.Text);
    }

    private static async Task<WorkspaceTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new WorkspaceTools(host);
    }
}
