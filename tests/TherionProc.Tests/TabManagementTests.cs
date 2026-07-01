using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using TherionProc.Services;

namespace TherionProc.Tests;

// the recently-closed-tab stack that backs Ctrl+Shift+T / "Reopen Closed Tab".
public class TabManagementTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewService() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    [Fact]
    public async Task Closing_a_tab_then_reopen_restores_it()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "survey a\nendsurvey\n");
        var b = dir.Write("b.th", "survey b\nendsurvey\n");
        var svc = NewService();

        await svc.OpenFileAsync(a);
        await svc.OpenFileAsync(b);
        Assert.Equal(2, svc.Documents.Count);
        Assert.False(svc.HasRecentlyClosed);

        var docB = svc.Documents.First(d => d.FilePath == b);
        svc.CloseDocument(docB);
        Assert.Single(svc.Documents);
        Assert.True(svc.HasRecentlyClosed);

        var reopened = await svc.ReopenLastClosedAsync();
        Assert.True(reopened);
        Assert.Equal(2, svc.Documents.Count);
        Assert.Contains(svc.Documents, d => d.FilePath == b);
    }

    [Fact]
    public async Task Reopen_with_nothing_closed_returns_false()
    {
        var svc = NewService();
        Assert.False(svc.HasRecentlyClosed);
        Assert.False(await svc.ReopenLastClosedAsync());
    }

    [Fact]
    public async Task Already_reopened_file_is_skipped()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = NewService();

        await svc.OpenFileAsync(a);
        var doc = svc.Documents.First();
        svc.CloseDocument(doc);
        await svc.OpenFileAsync(a);          // reopened manually
        Assert.Single(svc.Documents);

        // The stack entry now points at an already-open file → nothing more to reopen.
        Assert.False(svc.HasRecentlyClosed);
        Assert.False(await svc.ReopenLastClosedAsync());
    }
}
