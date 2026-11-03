// Diagnostics published into the panel from outside the parser (a task, the compiler) must stay
// listed until the next run replaces them; activating another document only re-reads the
// parse/semantic set.

using System.Collections.Immutable;
using Therion.Core;

namespace ThIDE.Tests;

public class MainWindowDiagnosticsRetentionTests
{
    [Fact]
    public void Published_task_diagnostics_survive_a_document_change() => UiThread.Run(() =>
    {
        var docs = new ScriptedDocumentService();
        var tasks = new StubTaskRunner();
        var vm = MainWindowViewModelHost.Create(docs, tasks);
        var reported = Diagnostic.Create("TH_TASK_001", DiagnosticSeverity.Error, "task reported an error",
            new SourceSpan("/invented/project/b.th", SourceLocation.Start, SourceLocation.Start, 0, 0));

        Assert.NotNull(tasks.PublishDiagnostics);   // the shell wires this callback at construction
        tasks.PublishDiagnostics!(ImmutableArray.Create(reported));
        Assert.Contains(reported, vm.Diagnostics.All);

        // Clicking the entry opens b.th and makes it the active document → per-document tools refresh.
        docs.RaiseDocumentChanged();

        Assert.Contains(reported, vm.Diagnostics.All);
    });
}
