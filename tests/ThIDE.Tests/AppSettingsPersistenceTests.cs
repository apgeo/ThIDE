using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// A settings file that fails to parse (truncated by an interrupted write, or a hand-edit typo)
/// holds the user's recent files, hooks, colours and MCP configuration. The first save after such
/// a launch must not replace it with defaults without keeping the original bytes somewhere.
/// </summary>
public class AppSettingsPersistenceTests
{
    [Fact]
    public void First_save_after_a_corrupt_load_keeps_the_original_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thide-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            // Valid JSON cut short, as a truncating write that died mid-way leaves it.
            const string truncated = "{\"RecentFiles\":[\"/invented/cave/thconfig\"],\"PinnedFiles\":[\"/invented/cave/a.th\"";
            File.WriteAllText(path, truncated);

            var service = new AppSettingsService(path);
            service.Save(service.Current);

            var survivors = Directory.GetFiles(dir)
                .Where(f => File.ReadAllText(f) == truncated)
                .ToList();
            Assert.True(survivors.Count > 0,
                "the unreadable settings file was replaced by defaults and no copy of the original was kept; "
                + "files now in the directory: " + string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName)));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
