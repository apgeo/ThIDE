using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

// Covers the task-1 auto-pick rule: the active thconfig for a freshly-opened root is the
// one remembered for that root, else the highest in the tree (shallowest) and, among ties,
// the most recently modified.
public class WorkspaceSessionAutoPickTests
{
    private static WorkspaceSessionService NewSession(IAppSettingsService settings) =>
        new(new StubSniffer(), settings);

    [Fact]
    public async Task Shallowest_thconfig_wins_over_a_newer_deeper_one()
    {
        using var dir = new TempDir();
        var top = dir.Write("top.thconfig");
        var deep = dir.Write("sub/deep.thconfig");
        File.SetLastWriteTimeUtc(top, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(deep, DateTime.UtcNow); // newer, but deeper

        await using var session = NewSession(new FakeSettings());
        await session.SetRootAsync(dir.Path);

        AssertActive(top, session);
    }

    [Fact]
    public async Task Newest_wins_among_thconfigs_at_the_same_depth()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.thconfig");
        var b = dir.Write("b.thconfig");
        File.SetLastWriteTimeUtc(a, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(b, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await using var session = NewSession(new FakeSettings());
        await session.SetRootAsync(dir.Path);

        AssertActive(b, session);
    }

    [Fact]
    public async Task Remembered_thconfig_for_root_is_preferred_over_the_auto_pick()
    {
        using var dir = new TempDir();
        dir.Write("top.thconfig");                  // would be the auto-pick (shallowest)
        var deep = dir.Write("sub/deep.thconfig");

        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir.Path));
        var settings = new FakeSettings(AppSettings.Default with
        {
            LastThconfigByRoot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = deep,
            },
        });

        await using var session = NewSession(settings);
        await session.SetRootAsync(dir.Path);

        AssertActive(deep, session);
    }

    [Fact]
    public async Task Activating_a_thconfig_remembers_it_for_the_root()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.thconfig");
        var b = dir.Write("b.thconfig");
        File.SetLastWriteTimeUtc(a, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(b, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // newer → auto-picked

        var settings = new FakeSettings();
        await using var session = NewSession(settings);
        await session.SetRootAsync(dir.Path);

        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir.Path));
        Assert.True(settings.Current.LastThconfigByRoot.ContainsKey(key));
        Assert.Equal(
            Path.GetFullPath(b),
            Path.GetFullPath(settings.Current.LastThconfigByRoot[key]),
            ignoreCase: true);
    }

    private static void AssertActive(string expected, WorkspaceSessionService session)
    {
        Assert.NotNull(session.ActiveThconfig);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(session.ActiveThconfig!.FullPath), ignoreCase: true);
    }
}
