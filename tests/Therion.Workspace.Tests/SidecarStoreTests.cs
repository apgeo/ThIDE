using Therion.Workspace;

namespace Therion.Workspace.Tests;

/// <summary>A throwaway temp directory that deletes itself on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thws_sidecar_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort on a temp dir */ }
    }
}

/// <summary>
/// The lead status sidecar. It had no tests before it moved lib-side; the MCP server now writes the
/// very file the IDE reads, so its behaviour is a contract between two programs.
/// </summary>
public class LeadStatusStoreTests
{
    [Fact]
    public void Round_trips_a_status_per_root()
    {
        using var dir = new TempDir();
        var store = new LeadStatusStore(dir.Path);

        store.Set("C:/proj", "cave.upper.7", "pushed");

        Assert.Equal("pushed", store.Get("C:/proj", "cave.upper.7"));
    }

    [Fact]
    public void An_unset_lead_is_open()
    {
        using var dir = new TempDir();
        var store = new LeadStatusStore(dir.Path);

        Assert.Equal(LeadStatusStore.Open, store.Get("C:/proj", "cave.upper.7"));
        Assert.Equal(LeadStatusStore.Open, store.Get(null, "cave.upper.7"));
    }

    /// <summary>Setting a lead back to "open" removes it rather than storing the default.</summary>
    [Fact]
    public void Setting_open_clears_the_entry()
    {
        using var dir = new TempDir();
        var store = new LeadStatusStore(dir.Path);
        store.Set("C:/proj", "cave.upper.7", "dead");

        store.Set("C:/proj", "cave.upper.7", LeadStatusStore.Open);

        Assert.Equal(LeadStatusStore.Open, store.Get("C:/proj", "cave.upper.7"));
    }

    [Fact]
    public void Statuses_are_scoped_to_their_workspace_root()
    {
        using var dir = new TempDir();
        var store = new LeadStatusStore(dir.Path);

        store.Set("C:/one", "x", "checked");

        Assert.Equal("checked", store.Get("C:/one", "x"));
        Assert.Equal(LeadStatusStore.Open, store.Get("C:/two", "x"));
    }

    /// <summary>Two stores over the same directory must see each other's writes — the IDE and the MCP server do.</summary>
    [Fact]
    public void A_second_store_over_the_same_directory_sees_the_first_ones_writes()
    {
        using var dir = new TempDir();
        new LeadStatusStore(dir.Path).Set("C:/proj", "cave.7", "pushed");

        var reader = new LeadStatusStore(dir.Path);

        Assert.Equal("pushed", reader.Get("C:/proj", "cave.7"));
    }

    [Fact]
    public void A_null_root_or_empty_location_is_a_noop()
    {
        using var dir = new TempDir();
        var store = new LeadStatusStore(dir.Path);

        store.Set(null, "x", "dead");
        store.Set("C:/proj", "", "dead");

        Assert.Empty(Directory.GetFiles(dir.Path));
    }
}

public class ProjectSidecarTests
{
    [Fact]
    public void The_key_is_stable_and_ignores_trailing_separators_and_case()
    {
        var a = ProjectSidecar.KeyFor("C:/proj");
        var b = ProjectSidecar.KeyFor("C:/proj/");

        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Equal(16, a!.Length);
    }

    [Fact]
    public void Different_roots_get_different_keys()
    {
        Assert.NotEqual(ProjectSidecar.KeyFor("C:/one"), ProjectSidecar.KeyFor("C:/two"));
    }

    [Fact]
    public void No_root_means_no_key()
    {
        Assert.Null(ProjectSidecar.KeyFor(null));
        Assert.Null(ProjectSidecar.KeyFor(""));
    }
}
