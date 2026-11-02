using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

// The Relational Map window gets a fresh view-model per open over the long-lived document
// service. Once the window is closed and nothing references the view-model any more, the
// document service must not keep it (and the graph it holds) alive and rebuilding.
public class RelationalMapViewModelLifetimeTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndCloseMap(IDocumentService docs, IWorkspaceSession session)
    {
        var vm = new RelationalMapViewModel(docs, session);
        // The window is closed: the shell drops its only reference to the view-model.
        return new WeakReference(vm);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndCloseUnwiredMap() => new(new RelationalMapViewModel());

    // Control: a view-model that subscribed to nothing is collectable in this harness, so the
    // test below fails only because of what the wired constructor leaves behind.
    [Fact]
    public void A_view_model_without_subscriptions_is_released()
    {
        var weak = OpenAndCloseUnwiredMap();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive);
    }

    [Fact]
    public void A_closed_map_view_model_is_released_by_the_document_service()
    {
        var settings = new FakeSettings();
        var session = new WorkspaceSessionService(new StubSniffer(), settings);
        var docs = new DocumentService(new StubResolver(), session, settings);

        var weak = OpenAndCloseMap(docs, session);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive,
            "the closed map's view-model is still reachable (its document/session subscriptions were never removed)");
        GC.KeepAlive(docs);
        GC.KeepAlive(session);
    }
}
