using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// An exception from one Changed subscriber during an external-change rebuild must not stop every
/// later external change from rebuilding the graph.
/// </summary>
public class WorkspaceSessionRebuildFaultTests
{
    private static string Survey(int length) =>
        $"survey a\n  centreline\n    data normal from to length compass clino\n    1 2 {length} 0 0\n  endcentreline\nendsurvey\n";

    [Fact]
    public async Task A_throwing_subscriber_does_not_stop_later_rebuilds()
    {
        using var dir = new TempDir();
        var source = dir.Write("a.th", Survey(1));
        var thconfig = dir.Write("cave.thconfig", "encoding utf-8\nsource a.th\n");

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        Assert.True(await session.SetActiveThconfigAsync(thconfig));

        int changed = 0, thrown = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref changed);
        session.Changed += (_, _) =>
        {
            if (Interlocked.Exchange(ref thrown, 1) == 0) throw new InvalidOperationException("subscriber fault");
        };

        // Wait for the throwing subscriber, not the counting one: the counter runs first, so seeing it
        // alone can race ahead of the throw. The bound is generous for a loaded machine; a pass is quick.
        File.WriteAllText(source, Survey(2));
        await WaitUntil(() => Volatile.Read(ref thrown) == 1, TimeSpan.FromSeconds(30));
        Assert.Equal(1, Volatile.Read(ref thrown));

        File.WriteAllText(source, Survey(3));
        await WaitUntil(() => Volatile.Read(ref changed) >= 2, TimeSpan.FromSeconds(30));

        Assert.True(Volatile.Read(ref changed) >= 2,
            "after one subscriber threw, a later external change no longer rebuilt the graph (Changed was not raised again)");
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(50);
    }
}
