using System;
using System.IO;
using System.Linq;
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

    private static string Survey(string name, int length) =>
        $"survey {name}\n  centreline\n    data normal from to length compass clino\n    1 2 {length} 0 0\n  endcentreline\nendsurvey\n";

    [Fact]
    public async Task Many_external_changes_at_once_publish_the_graph_once()
    {
        using var dir = new TempDir();
        var sources = new string[FileCount];
        var config = "encoding utf-8\n";
        for (int i = 0; i < FileCount; i++)
        {
            sources[i] = dir.Write($"s{i}.th", Survey($"s{i}", 1));
            config += $"source s{i}.th\n";
        }
        var thconfig = dir.Write("cave.thconfig", config);

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        // The burst below is one burst only if the loop writing it finishes inside the quiet period; on a
        // loaded machine twelve writes plus their watcher events can spread past the default 200 ms.
        session.RebuildQuietPeriod = TimeSpan.FromSeconds(1);
        Assert.True(await session.SetActiveThconfigAsync(thconfig));
        Assert.Equal(FileCount, session.Model!.PerFile.Count);

        int changed = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref changed);

        // One burst, like a checkout: every tracked file is rewritten (its survey renamed, so the
        // published model shows which rewrites it has seen).
        for (int i = 0; i < FileCount; i++)
            File.WriteAllText(sources[i], Survey($"t{i}", 2));

        // Done when the published model has seen every rewrite and no rebuild is still pending,
        // rather than after a fixed sleep that a loaded machine can outrun.
        bool AllPublished() =>
            !session.IsRebuildPending
            && session.Model is { } m
            && Enumerable.Range(0, FileCount).All(i => m.SurveysByFullName.ContainsKey($"t{i}"));
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!AllPublished() && DateTime.UtcNow < until) await Task.Delay(50);
        Assert.True(AllPublished(), "the rewrites never all reached the published model");

        int published = Volatile.Read(ref changed);
        Assert.InRange(published, 1, 2);
    }
}
