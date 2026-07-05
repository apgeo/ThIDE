using ThIDE.Services;

namespace ThIDE.Tests;

// the project metadata sidecar store.
public class ProjectMetadataStoreTests
{
    [Fact]
    public void Round_trips_metadata_per_root()
    {
        using var dir = new TempDir();
        var store = new ProjectMetadataStore(dir.Path);
        var md = new ProjectMetadata
        {
            Name = "Cave System",
            Region = "Apuseni",
            Crs = "EPSG:3844",
            DeclinationSource = "WMM 2020",
            License = "CC-BY",
            Notes = "multi-line\nnotes",
        };

        store.Save("C:/proj", md);
        var loaded = store.Load("C:/proj");

        Assert.Equal("Cave System", loaded.Name);
        Assert.Equal("Apuseni", loaded.Region);
        Assert.Equal("EPSG:3844", loaded.Crs);
        Assert.Equal("WMM 2020", loaded.DeclinationSource);
        Assert.Equal("CC-BY", loaded.License);
        Assert.Equal("multi-line\nnotes", loaded.Notes);
    }

    [Fact]
    public void Unknown_root_and_null_root_return_empty()
    {
        using var dir = new TempDir();
        var store = new ProjectMetadataStore(dir.Path);
        Assert.Equal(string.Empty, store.Load("C:/never-saved").Name);
        Assert.Equal(string.Empty, store.Load(null).Name);
    }

    [Fact]
    public void Null_root_save_is_a_noop()
    {
        using var dir = new TempDir();
        var store = new ProjectMetadataStore(dir.Path);
        store.Save(null, new ProjectMetadata { Name = "x" });   // no key → nothing written
        Assert.Equal(string.Empty, store.Load("C:/proj").Name);
    }
}
