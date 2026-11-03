// A layout swap rebuilds the whole dock tree and every panel view in it. If that hangs or crashes, the
// unsaved edits must already be in the crash-recovery folder, not wait for the next 5-second autosave.

using System.Windows.Input;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class LayoutSwapRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-recovery-layout-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    [Theory]
    [InlineData(nameof(MainWindowViewModel.ResetLayoutCommand))]
    [InlineData(nameof(MainWindowViewModel.ApplySplitLayout2Command))]
    [InlineData(nameof(MainWindowViewModel.ApplySplitLayout3Command))]
    public void A_layout_swap_first_writes_the_unsaved_buffers_to_crash_recovery(string command) => UiThread.Run(() =>
    {
        var recovery = new CrashRecoveryService(_root);
        recovery.MarkRunning();
        var docs = new ScriptedDocumentService();
        var vm = MainWindowViewModelHost.Create(docs, crashRecovery: recovery);
        const string edit = "survey cave\n  # unsaved edit\nendsurvey\n";
        var doc = docs.OpenTextDocument("/invented/project/cave.txt", "survey cave\nendsurvey\n");
        doc.DocumentText = edit;
        Assert.True(doc.IsDirty);

        ((ICommand)typeof(MainWindowViewModel).GetProperty(command)!.GetValue(vm)!).Execute(null);

        Assert.Contains(recovery.GetRecoverable(), b => b.OriginalPath == doc.FilePath && b.Text == edit);
        recovery.MarkCleanShutdown();
    });
}
