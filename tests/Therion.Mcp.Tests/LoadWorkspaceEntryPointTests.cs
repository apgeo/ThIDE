using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

/// <summary>
/// load_workspace defines the jail every other path argument is held to. It must therefore only accept
/// something that is a Therion project, or one read-only call relocates the jail to any directory.
/// </summary>
public class LoadWorkspaceEntryPointTests
{
    [Fact]
    public async Task Load_workspace_refuses_a_file_that_is_not_a_thconfig_or_th()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thmcp_notes_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var notes = Path.Combine(dir, "notes.txt");
        File.WriteAllText(notes, "shopping list: lamp, rope, batteries\n");
        File.WriteAllText(Path.Combine(dir, "unrelated.txt"), "not part of any cave project\n");

        try
        {
            await using var host = new WorkspaceHost();
            var tools = new WorkspaceTools(host);

            var load = await tools.LoadWorkspace(notes);

            Assert.False(load.Ok, "load_workspace accepted a plain text file as a project entry point and made its directory the jail root");
            Assert.Equal(ToolErrorCodes.WorkspaceLoadFailed, load.Error!.Code);

            var read = await tools.ReadFile("unrelated.txt");
            Assert.False(read.Ok, "read_file served a file next to a non-Therion entry point");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
