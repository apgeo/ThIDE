// A closed document must become collectable: the Ctrl+Tab most-recently-used list is only an
// ordering over the open dockables, not an owner of closed ones.

using System.Reflection;
using System.Runtime.CompilerServices;
using Dock.Model.Core;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class MainWindowMruLifetimeTests
{
    [Fact]
    public void Closed_document_is_dropped_from_the_switcher_mru_and_becomes_collectable() => UiThread.Run(() =>
    {
        var docs = new ScriptedDocumentService();
        var vm = MainWindowViewModelHost.Create(docs);

        var weak = OpenActivateAndClose(docs);

        Assert.False(MruHolds(vm, weak),
            "the Ctrl+Tab MRU list still references the closed document");

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive, "the closed document is still reachable");
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenActivateAndClose(ScriptedDocumentService docs)
    {
        // A plain-text document: its (synchronous) parse leaves no queued dispatcher job behind.
        var doc = docs.OpenTextDocument("/invented/project/notes.txt", "a note\n");
        docs.CloseDocument(doc);
        return new WeakReference(doc);
    }

    private static bool MruHolds(MainWindowViewModel vm, WeakReference weak)
    {
        var field = typeof(MainWindowViewModel).GetField("_mru", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(vm) is not List<IDockable> mru) return false;
        return weak.Target is { } target && mru.Any(d => ReferenceEquals(d, target));
    }
}
