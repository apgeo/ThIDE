// The app's own state files (settings.json, bookmarks.json, keyboard.json): a save replaces the file
// rather than rewriting it in place, an unreadable file is kept aside before the next save replaces
// it, and settings changes made from two threads at once are all kept.

using ThIDE.Services;

namespace ThIDE.Tests;

public sealed class StateFileDurabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-state-" + Guid.NewGuid().ToString("N"));

    public StateFileDurabilityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    public async Task Settings_changes_made_from_two_threads_at_once_are_all_kept()
    {
        var path = PathOf("settings.json");
        var service = new AppSettingsService(path);
        const int each = 150;
        using var start = new Barrier(2);

        // One thread adds recent files, the other recent directories: each change reads the settings
        // and saves them back, which is where a read-then-save loses the other thread's change.
        var files = Task.Run(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < each; i++)
            {
                var item = "/invented/f" + i;
                service.Update(s => s with { RecentFiles = s.RecentFiles.Append(item).ToList() });
            }
        });
        var dirs = Task.Run(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < each; i++)
            {
                var item = "/invented/d" + i;
                service.Update(s => s with { RecentDirectories = s.RecentDirectories.Append(item).ToList() });
            }
        });
        await Task.WhenAll(files, dirs);

        Assert.Equal(each, service.Current.RecentFiles.Count);
        Assert.Equal(each, service.Current.RecentDirectories.Count);
        var reloaded = new AppSettingsService(path).Current;
        Assert.Equal(each, reloaded.RecentFiles.Count);
        Assert.Equal(each, reloaded.RecentDirectories.Count);
    }

    [Fact]
    public void Update_on_an_interface_without_its_own_implementation_still_saves()
    {
        var fake = new SaveOnlySettings();
        IAppSettingsService service = fake;

        service.Update(s => s with { ShowWelcomeOnStartup = false });

        Assert.False(fake.Current.ShowWelcomeOnStartup);
        Assert.Equal(1, fake.Saves);
    }

    // A save writes a new file and moves it over the old one: a reader that already has the old file
    // open still sees the old bytes. A save that truncates and rewrites in place changes them under it,
    // and a write that fails half-way leaves that truncated file behind.
    [Theory]
    [InlineData("settings")]
    [InlineData("bookmarks")]
    [InlineData("keyboard")]
    public void A_save_replaces_the_file_instead_of_rewriting_it_in_place(string kind)
    {
        if (OperatingSystem.IsWindows()) return;   // replacing a file that is open is a Unix behaviour

        var path = PathOf(kind + ".json");
        Action save = kind switch
        {
            "settings" => SaveSettings(path),
            "bookmarks" => SaveBookmarks(path),
            _ => SaveShortcuts(path),
        };
        save();   // first write: the file exists from here on
        var before = File.ReadAllBytes(path);

        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        save();

        var seen = new MemoryStream();
        reader.Position = 0;
        reader.CopyTo(seen);
        Assert.Equal(before, seen.ToArray());
        Assert.NotEqual(before, File.ReadAllBytes(path));
    }

    private static Action SaveSettings(string path)
    {
        var service = new AppSettingsService(path);
        int n = 0;
        return () => service.Update(s => s with { RecentFiles = [.. s.RecentFiles, "/invented/" + n++] });
    }

    private static Action SaveBookmarks(string path)
    {
        var service = new BookmarksService(path);
        int n = 0;
        return () => service.AddBookmark("/invented/cave.th", ++n, "mark " + n);
    }

    private static Action SaveShortcuts(string path)
    {
        var service = new JsonKeyboardShortcutService(path);
        var keys = new[] { "Ctrl+Alt+F1", "Ctrl+Alt+F2", "Ctrl+Alt+F3" };
        int n = 0;
        var command = service.Defaults.Keys.First();
        return () => service.Set(command, keys[n++ % keys.Length]);
    }

    [Fact]
    public void Unreadable_bookmarks_are_kept_aside_before_the_next_save()
    {
        var path = PathOf("bookmarks.json");
        const string truncated = "[{\"filePath\":\"/invented/cave.th\",\"line\":3,";
        File.WriteAllText(path, truncated);

        var service = new BookmarksService(path);
        Assert.Empty(service.Bookmarks);
        service.AddBookmark("/invented/cave.th", 7, "after");

        AssertKeptAside(path, truncated);
    }

    [Fact]
    public void Unreadable_shortcuts_are_kept_aside_before_the_next_save()
    {
        var path = PathOf("keyboard.json");
        const string truncated = "{\"Build.Compile\":\"F6\",";
        File.WriteAllText(path, truncated);

        var service = new JsonKeyboardShortcutService(path);
        var command = service.Defaults.Keys.First();
        Assert.Equal(service.Defaults[command], service.Gestures[command]);
        service.Set(command, "Ctrl+Alt+F9");

        AssertKeptAside(path, truncated);
    }

    private void AssertKeptAside(string path, string original)
    {
        var copies = Directory.GetFiles(_dir, Path.GetFileName(path) + ".unreadable-*");
        var copy = Assert.Single(copies);
        Assert.Equal(original, File.ReadAllText(copy));
        Assert.NotEqual(original, File.ReadAllText(path));
    }

    private sealed class SaveOnlySettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public int Saves { get; private set; }
        public void Save(AppSettings settings) { Current = settings; Saves++; }
        public event EventHandler? Changed { add { } remove { } }
    }
}
