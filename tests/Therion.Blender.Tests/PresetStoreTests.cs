// User preset store tests (BA-B9 batch 3): save/load/update/delete round-trips over a temp
// directory, slug behaviour, and tolerance of foreign/corrupt files. IDisposable cleans the
// temp dir.

using Therion.Blender;
using Therion.Blender.Presets;

namespace Therion.Blender.Tests;

public class PresetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static RenderPreset Preset(string name) => new()
    {
        Name = name,
        Description = "test",
        BuiltIn = false,
        Spec = new SceneSpec { Camera = new CameraSpec { Template = CameraTemplate.Orbit } },
    };

    [Fact]
    public void Load_OnMissingDirectory_IsEmpty()
    {
        Assert.Empty(new PresetStore(_dir).Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = new PresetStore(_dir);
        store.Save(Preset("My Orbit"));
        store.Save(Preset("Deep dive"));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(["Deep dive", "My Orbit"], loaded.Select(p => p.Name)); // ordinal sort
        Assert.All(loaded, p => Assert.False(p.BuiltIn));
    }

    [Fact]
    public void Save_SameName_Overwrites_NotDuplicates()
    {
        var store = new PresetStore(_dir);
        store.Save(Preset("Cave A") with { Description = "first" });
        store.Save(Preset("Cave A") with { Description = "second" });

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("second", loaded[0].Description);
    }

    [Fact]
    public void Save_ForcesUserPreset_EvenIfMarkedBuiltIn()
    {
        var store = new PresetStore(_dir);
        store.Save(BuiltInPresets.OrbitShowcase()); // BuiltIn = true
        Assert.False(store.Load()[0].BuiltIn);      // persisted as a user preset
    }

    [Fact]
    public void Delete_RemovesTheFile()
    {
        var store = new PresetStore(_dir);
        store.Save(Preset("Temp"));
        Assert.True(store.Exists("Temp"));
        Assert.True(store.Delete("Temp"));
        Assert.False(store.Exists("Temp"));
        Assert.False(store.Delete("Temp")); // already gone
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_SkipsUnreadableFiles()
    {
        var store = new PresetStore(_dir);
        store.Save(Preset("Good"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "junk" + PresetStore.Extension), "{ not valid json");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored — wrong extension");

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("Good", loaded[0].Name);
    }

    [Theory]
    [InlineData("Peștera de Aur", "pe-tera-de-aur")]
    [InlineData("  Orbit / Showcase!  ", "orbit-showcase")]
    [InlineData("***", "preset")]
    public void Slug_IsFilesystemSafe(string name, string expected)
    {
        Assert.Equal(expected, PresetStore.Slug(name));
    }

    [Fact]
    public void NamesWithSameSlug_ShareAFile_LastWins()
    {
        // Distinct display names that slug identically collapse to one file (documented).
        var store = new PresetStore(_dir);
        store.Save(Preset("Cave A"));
        store.Save(Preset("cave  a"));
        Assert.Single(store.Load());
    }
}
