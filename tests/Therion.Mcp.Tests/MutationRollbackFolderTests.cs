using Therion.Mcp.Mutations;

namespace Therion.Mcp.Tests;

/// <summary>A rolled-back plan leaves the folder tree as it found it, not only the files.</summary>
public class MutationRollbackFolderTests
{
    [Fact]
    public async Task A_rolled_back_create_removes_the_folders_it_made()
    {
        using var fixture = FixtureWorkspace.Create();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var engine = new MutationEngine(host);

        var plan = new MutationPlan([
            new CreateFile(fixture.PathTo("caves", "new", "deep", "a.th"), "survey a\nendsurvey\n"),
            // The parent is an existing file, so this create fails after the first one landed.
            new CreateFile(fixture.PathTo("caves", "abandoned.th", "child.th"), "survey c\nendsurvey\n"),
        ]);

        var result = await engine.ApplyAsync(plan, dryRun: false);

        Assert.False(result.Ok);
        Assert.False(File.Exists(fixture.PathTo("caves", "new", "deep", "a.th")));
        Assert.True(!Directory.Exists(fixture.PathTo("caves", "new")),
            "the rollback deleted the created file but left the folders 'caves/new/deep' that the plan made");
    }
}
