using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// Two overlapping activations: the one the user asked for last must be the one that stays active,
/// and the indexing indicator must stay on while any activation is still indexing.
/// The earlier activation is held inside its own IndexingChanged notification, which the service
/// raises synchronously on the activating thread just before it builds the semantic model.
/// </summary>
public class WorkspaceSessionActivationOrderTests
{
    private static string Full(string p) => Path.GetFullPath(p);

    private sealed class HeldActivation
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Release = new(false);
        private int _armed = 1;

        public void OnIndexingChanged(WorkspaceSessionService session)
        {
            if (!session.IsIndexing) return;
            if (Interlocked.Exchange(ref _armed, 0) == 0) return;
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task The_activation_requested_last_stays_active()
    {
        using var dir = new TempDir();
        var slow = dir.Write("slow.thconfig", "encoding utf-8\n");
        var fast = dir.Write("fast.thconfig", "encoding utf-8\n");

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(dir.Path);

        var held = new HeldActivation();
        session.IndexingChanged += (_, _) => held.OnIndexingChanged(session);

        var slowActivation = Task.Run(() => session.SetActiveThconfigAsync(slow));
        Assert.True(held.Entered.Wait(TimeSpan.FromSeconds(30)), "the slow activation never started indexing");

        Assert.True(await session.SetActiveThconfigAsync(fast));
        Assert.Equal(Full(fast), Full(session.ActiveThconfig!.FullPath), ignoreCase: true);

        held.Release.Set();
        Assert.True(await slowActivation);

        Assert.Equal(Full(fast), Full(session.ActiveThconfig!.FullPath), ignoreCase: true);
    }

    [Fact]
    public async Task Indexing_stays_on_while_an_earlier_activation_is_still_indexing()
    {
        using var dir = new TempDir();
        var slow = dir.Write("slow.thconfig", "encoding utf-8\n");
        var fast = dir.Write("fast.thconfig", "encoding utf-8\n");

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(dir.Path);

        var held = new HeldActivation();
        session.IndexingChanged += (_, _) => held.OnIndexingChanged(session);

        var slowActivation = Task.Run(() => session.SetActiveThconfigAsync(slow));
        Assert.True(held.Entered.Wait(TimeSpan.FromSeconds(30)), "the slow activation never started indexing");

        await session.SetActiveThconfigAsync(fast);
        try
        {
            Assert.True(session.IsIndexing, "IsIndexing switched off while the slow activation was still indexing");
        }
        finally
        {
            held.Release.Set();
            await slowActivation;
        }
    }
}
