using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// One external change that touches many tracked files (a checkout, a sync, Format all) should
/// publish the graph once, or at most a couple of times, not once per file. Today every re-parsed
/// file starts its own graph rebuild and its own Changed, so N files cost N cross-file builds and
/// N full UI refreshes.
/// </summary>
public class WorkspaceSessionRebuildCoalescingTests
{
    private const int FileCount = 12;

    [Fact]
    public async Task Many_external_changes_at_once_publish_the_graph_once()
    {
        using var dir = new TempDir();
        var sources = new string[FileCount];
        var config = "encoding utf-8\n";
        for (int i = 0; i < FileCount; i++)
        {
            sources[i] = dir.Write($"s{i}.th", $"survey s{i}\n  centreline\n    data normal from to length compass clino\n    1 2 1 0 0\n  endcentreline\nendsurvey\n");
            config += $"source s{i}.th\n";
        }
        var thconfig = dir.Write("cave.thconfig", config);

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        Assert.True(await session.SetActiveThconfigAsync(thconfig));
        Assert.Equal(FileCount, session.Model!.PerFile.Count);

        int changed = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref changed);

        // One burst, like a checkout: every tracked file is rewritten inside one debounce window.
        for (int i = 0; i < FileCount; i++)
            File.WriteAllText(sources[i], $"survey s{i}\n  centreline\n    data normal from to length compass clino\n    1 2 2 0 0\n  endcentreline\nendsurvey\n");

        await Task.Delay(TimeSpan.FromSeconds(2));

        int published = Volatile.Read(ref changed);
        Assert.InRange(published, 1, 2);
    }
}
