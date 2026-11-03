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

    [Fact]
    public async Task No_mutation_can_create_files_in_the_project_config_folder()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var engine = new MutationEngine(host);
        var config = Path.Combine(fixture.Root, ".thide");

        foreach (var change in new FileChange[]
                 {
                     new CreateDirectory(config),
                     new CreateFile(Path.Combine(config, "tasks.json"), "{}"),
                     new WriteFile(Path.Combine(config, "hooks.json"), "{}"),
                 })
        {
            var result = await engine.ApplyAsync(new MutationPlan([change]), dryRun: false);
            Assert.False(result.Ok, $"{change.GetType().Name} reached the config folder");
            Assert.Equal(ToolErrorCodes.ProtectedPath, result.Error!.Code);
        }
        Assert.False(Directory.Exists(config));
    }

    [Theory]
    [InlineData(".THIDE/hooks.json")]
    [InlineData(".Thide/tasks.json")]
    public async Task The_config_folder_is_protected_in_any_case(string relative)
    {
        // On a case-insensitive volume (macOS, Windows) this is the real hooks file; on a
        // case-sensitive one refusing it costs nothing, so the guard ignores case everywhere.
        using var fixture = FixtureWorkspace.CreateBroken();
        Directory.CreateDirectory(Path.Combine(fixture.Root, ".thide"));
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var result = await new MutationEngine(host).ApplyAsync(
            new MutationPlan([new WriteFile(Path.Combine(fixture.Root, relative), "{}")]), dryRun: false);

        Assert.False(result.Ok, $"a write to {relative} was accepted");
        Assert.Equal(ToolErrorCodes.ProtectedPath, result.Error!.Code);
    }

    [Fact]
    public async Task A_link_to_the_config_folder_does_not_lead_into_it()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var config = Path.Combine(fixture.Root, ".thide");
        Directory.CreateDirectory(config);
        var hooksPath = Path.Combine(config, "hooks.json");
        File.WriteAllText(hooksPath, "{}");
        var link = Path.Combine(fixture.Root, "settings");
        if (!TryLinkDirectory(link, config)) return;   // unprivileged Windows: nothing to prove
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var result = await new MutationEngine(host).ApplyAsync(
            new MutationPlan([new WriteFile(Path.Combine(link, "hooks.json"), "{ \"pwned\": true }")]), dryRun: false);

        Assert.False(result.Ok, "a write through a link to .thide/ was accepted");
        Assert.Equal(ToolErrorCodes.ProtectedPath, result.Error!.Code);
        Assert.Equal("{}", File.ReadAllText(hooksPath));
    }

    [Fact]
    public async Task A_config_folder_that_is_a_link_protects_its_target()
    {
        // The hooks are read from wherever .thide points, so that folder is the protected one.
        using var fixture = FixtureWorkspace.CreateBroken();
        var shared = Path.Combine(fixture.Root, "shared-config");
        Directory.CreateDirectory(shared);
        var hooksPath = Path.Combine(shared, "hooks.json");
        File.WriteAllText(hooksPath, "{}");
        if (!TryLinkDirectory(Path.Combine(fixture.Root, ".thide"), shared)) return;
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var result = await new MutationEngine(host).ApplyAsync(
            new MutationPlan([new WriteFile(hooksPath, "{ \"pwned\": true }")]), dryRun: false);

        Assert.False(result.Ok, "a write to the folder .thide links to was accepted");
        Assert.Equal(ToolErrorCodes.ProtectedPath, result.Error!.Code);
        Assert.Equal("{}", File.ReadAllText(hooksPath));
    }

    private static bool TryLinkDirectory(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return false; }
    }
}
