using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using Therion.Testing;
using Therion.Workspace.Editing;

namespace Therion.Mcp.Tests;

/// <summary>
/// The agent's write paths (edit_file, format_file, a plan that creates a file) against the shared
/// fixture matrix: every Therion encoding and line-ending style, lossy files, unencodable edits, links,
/// modes, read-only files and folders, a case-variant sibling. Each cell is written exactly or refused
/// with the file untouched.
/// </summary>
public class WritePathConformanceTests
{
    public static TheoryData<string> AllFixtures => WriteFixtures.Names();

    public static TheoryData<string> CreatableFixtures =>
        WriteFixtures.Names(f => f.Kind is FixtureKind.Plain or FixtureKind.Unencodable);

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Edit_file_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;
        await using var host = new WorkspaceHost();
        await host.LoadAsync(placed.Thconfig);

        var result = await new EditTools(host, new MutationEngine(host))
            .EditFile("caves/a.th", WriteFixture.Find, placed.Fixture.Replace, dryRun: false);

        WritePathConformance.Check(placed, WriterKind.TextEdit, result.Ok, "edit_file");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Format_file_write_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;
        await using var host = new WorkspaceHost();
        await host.LoadAsync(placed.Thconfig);

        var result = await new FormatTools(host, new MutationEngine(host)).FormatFile("caves/a.th", write: true);

        WritePathConformance.Check(placed, WriterKind.Reindent, result.Ok, "format_file");
    }

    /// <summary>A plan's new file (import, scaffold) is written in the encoding its content declares, or not at all.</summary>
    [Theory]
    [MemberData(nameof(CreatableFixtures))]
    public async Task A_planned_create_writes_the_declared_encoding_or_nothing(string fixture)
    {
        var f = WriteFixtures.Get(fixture);
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get("utf-8-plain/lf"))!;
        await using var host = new WorkspaceHost();
        await host.LoadAsync(placed.Thconfig);
        var target = Path.Combine(placed.Root, "new", "deep", "b.th");
        var content = f.Kind == FixtureKind.Unencodable ? f.EditedText : f.Text;

        var result = await new MutationEngine(host).ApplyAsync(new MutationPlan([new CreateFile(target, content)]), dryRun: false);

        if (f.Kind == FixtureKind.Unencodable)
        {
            Assert.False(result.Ok, $"[{fixture}] content its encoding cannot hold was created");
            Assert.False(File.Exists(target));
            Assert.False(Directory.Exists(Path.Combine(placed.Root, "new")), $"[{fixture}] a refused create left its folders behind");
            return;
        }
        Assert.True(result.Ok, $"[{fixture}] {result.Error?.Code} {result.Error?.Message}");
        var declared = f.Encoding.Directive is null ? TherionEncodings.Get("utf-8-plain") : f.Encoding;
        Assert.Equal(declared.Strict().GetBytes(content), File.ReadAllBytes(target));
        Assert.Equal(content, SourceFileIo.Read(target).Text);
    }
}
