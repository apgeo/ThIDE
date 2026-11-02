namespace Therion.Workspace.Tests;

/// <summary>
/// The IDE and its in-app MCP server each hold their own <see cref="LeadStatusStore"/> over the same
/// sidecar file. A status one of them writes must survive the other's next write.
/// </summary>
public class LeadStatusStoreSharingTests
{
    [Fact]
    public void Interleaved_stores_keep_each_others_statuses()
    {
        using var dir = new TempDir();
        const string root = "C:/proj";
        var ide = new LeadStatusStore(dir.Path);
        var mcp = new LeadStatusStore(dir.Path);

        ide.Get(root, "x");                    // the panel has loaded (and cached) the map
        mcp.Set(root, "a", "pushed");          // the assistant marks a lead through the MCP host
        ide.Set(root, "b", "checked");         // the user marks another lead in the panel

        var onDisk = new LeadStatusStore(dir.Path);
        Assert.Equal("pushed", onDisk.Get(root, "a"));
        Assert.Equal("checked", onDisk.Get(root, "b"));
    }

    [Fact]
    public void A_store_sees_a_status_another_store_wrote_after_its_first_read()
    {
        using var dir = new TempDir();
        const string root = "C:/proj";
        var ide = new LeadStatusStore(dir.Path);
        var mcp = new LeadStatusStore(dir.Path);

        ide.Get(root, "a");
        mcp.Set(root, "a", "pushed");

        Assert.Equal("pushed", ide.Get(root, "a"));
    }
}
