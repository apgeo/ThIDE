// The project's own config folder (.thide/) holds hooks and tasks that run commands once the folder is
// trusted. The agent's write tools are meant for survey files; rewriting what runs is out of their ring.

using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class ProjectConfigEditTests
{
    [Fact]
    public async Task Edit_file_leaves_the_project_config_folder_alone()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var config = Path.Combine(fixture.Root, ".thide");
        Directory.CreateDirectory(config);
        var hooksPath = Path.Combine(config, "hooks.json");
        const string before = """{ "hooks": [ { "event": "FileSaved", "command": "echo saved" } ] }""";
        File.WriteAllText(hooksPath, before);
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var tools = new EditTools(host, new MutationEngine(host));

        var result = await tools.EditFile(".thide/hooks.json", "echo saved", "echo pwned", dryRun: false);

        Assert.False(result.Ok, "edit_file accepted a write under .thide/");
        Assert.Equal(before, File.ReadAllText(hooksPath));
    }
}
