using System.Text;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using Therion.Syntax;

namespace Therion.Mcp.Tests;

public class FormatToolsTests
{
    [Fact]
    public async Task Format_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();

        var result = await new FormatTools(host, new MutationEngine(host)).FormatFile("caves/upper.th");

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task Returns_text_and_writes_nothing_by_default()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.FormatFile("caves/upper.th");

        Assert.True(result.Ok);
        Assert.NotNull(result.Data!.Text);
        Assert.Null(result.Data.Mutation);
        Assert.Contains("survey upper", result.Data.Text);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    /// <summary>The text is whatever TherionWriter emits — the same thing `therion-cli format` prints.</summary>
    [Fact]
    public async Task Text_is_what_the_writer_emits()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);
        var path = fixture.PathTo("caves", "upper.th");

        var result = await tools.FormatFile("caves/upper.th");

        var expected = new TherionWriter().Write(
            new ThParser().Parse(path, EncodingResolver.ReadAllText(path)).Value!);
        Assert.Equal(expected, result.Data!.Text);
    }

    /// <summary>A formatter that keeps changing its mind cannot be run in a loop or a commit hook.</summary>
    [Fact]
    public async Task Formatting_is_idempotent()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var once = await tools.FormatFile("caves/upper.th", write: true);
        Assert.True(once.Ok);
        var afterFirst = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var twice = await tools.FormatFile("caves/upper.th", write: true);

        Assert.True(twice.Ok);
        Assert.False(twice.Data!.Changed);
        Assert.Empty(twice.Data.Mutation!.Files);
        Assert.Equal(afterFirst, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    [Fact]
    public async Task Writing_replaces_the_file_and_reports_the_lint()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FormatFile("caves/upper.th", write: true);

        Assert.True(result.Ok);
        Assert.True(result.Data!.Changed);
        Assert.Null(result.Data.Text);
        Assert.False(result.Data.Mutation!.DryRun);
        Assert.Equal(0, result.Data.Mutation.NewErrors);
        Assert.Contains("survey upper", File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    /// <summary>Re-emitting a tree the parser could not make sense of would rewrite the user's file wrongly.</summary>
    [Fact]
    public async Task A_file_with_parse_errors_is_refused_and_the_errors_are_named()
    {
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "upper.th"), """
            survey upper
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
              endcentreline
            endcentreline
            """);   // TH0021: the wrong terminator closes 'survey'

        var tools = await LoadedToolsAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.FormatFile("caves/upper.th", write: true);

        Assert.Equal(ToolErrorCodes.ParseErrors, result.Error!.Code);
        Assert.Contains("TH0021", result.Error.Message);
        Assert.Contains("caves/upper.th:", result.Error.Message);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    /// <summary>
    /// Warnings mean the file is odd, not that its tree is missing text. A missing `endsurvey` is a
    /// warning, and formatting supplies the terminator — which is the point of formatting.
    /// </summary>
    [Fact]
    public async Task A_file_with_only_warnings_still_formats()
    {
        using var fixture = FixtureWorkspace.CreateBroken();   // bad data value + missing include
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FormatFile("caves/upper.th");

        Assert.True(result.Ok);
        Assert.NotNull(result.Data!.Text);
    }

    [Fact]
    public async Task Writing_preserves_the_declared_encoding()
    {
        using var fixture = FixtureWorkspace.Create();
        var target = fixture.PathTo("caves", "grotte.th");
        File.WriteAllText(target, "encoding iso-8859-1\nsurvey grotte\n  # Bédeilhac\nendsurvey\n", Encoding.Latin1);
        File.AppendAllText(fixture.Thconfig, "\nsource caves/grotte.th\n");

        var tools = await LoadedToolsAsync(fixture);
        var result = await tools.FormatFile("caves/grotte.th", write: true);

        Assert.True(result.Ok);
        var bytes = File.ReadAllBytes(target);
        Assert.Contains((byte)0xE9, bytes);                       // é is still one Latin-1 byte
        Assert.Contains("Bédeilhac", Encoding.Latin1.GetString(bytes));
    }

    /// <summary>
    /// format_file re-emits a file from its parse tree. If it used the tree the workspace parsed at
    /// load time, an edit made on disk since would be silently overwritten by the older content.
    /// </summary>
    [Fact]
    public async Task Formatting_a_file_edited_since_load_keeps_the_edit()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");

        File.WriteAllText(target, """
            survey upper
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
                2 3 12.5 100 -5
                3 4 99.0 180 0
              endcentreline
            endsurvey
            """);

        var result = await tools.FormatFile("caves/upper.th", write: true);

        Assert.True(result.Ok, result.Error?.Message);
        var written = File.ReadAllText(target);
        Assert.Contains("3 4 99.0 180 0", written);   // the shot added after load survived
    }

    /// <summary>The same, without writing: the returned text must describe the file as it is now.</summary>
    [Fact]
    public async Task Formatted_text_reflects_the_current_file_not_the_loaded_tree()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        File.WriteAllText(fixture.PathTo("caves", "upper.th"), "survey renamed_on_disk\nendsurvey\n");

        var result = await tools.FormatFile("caves/upper.th");

        Assert.Contains("survey renamed_on_disk", result.Data!.Text);
    }

    [Fact]
    public async Task A_stale_sha256_refuses_the_write()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.FormatFile("caves/upper.th", write: true, expectedSha256: new string('0', 64));

        Assert.Equal(ToolErrorCodes.FileChanged, result.Error!.Code);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    [Fact]
    public async Task Refuses_a_path_outside_the_workspace()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FormatFile("../../etc/passwd", write: true);

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    [Fact]
    public async Task Refuses_a_file_the_project_does_not_include()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FormatFile("caves/abandoned.th");

        Assert.Equal(ToolErrorCodes.FileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Text_is_capped_and_flagged_when_it_exceeds_the_byte_budget()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.FormatFile("caves/upper.th", maxBytes: 10);

        Assert.True(result.Ok);
        Assert.True(result.Data!.Truncated);
        Assert.Equal(10, result.Data.Text!.Length);
    }

    private static async Task<FormatTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new FormatTools(host, new MutationEngine(host));
    }
}
