// The workspace-level undo of multi-file writes: a rename, an edit batch (New trip, Link survey,
// Format all…) and Replace All are each one undo step that puts every file back, on disk and in the
// open editors, and refuses rather than overwrite a later edit. Invented files in temp folders only.

using System.Collections.ObjectModel;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Tests;

public class WorkspaceUndoTests
{
    private static IDocumentService Docs(ObservableCollection<FileDocumentViewModel> open, FileDocumentViewModel? active = null) =>
        WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Documents" => open,
            "get_Active" => active,
            _ => null,
        });

    private static List<(int, int)> Hits(string text, string name)
    {
        var hits = new List<(int, int)>();
        for (int i = text.IndexOf(name, StringComparison.Ordinal); i >= 0; i = text.IndexOf(name, i + name.Length, StringComparison.Ordinal))
            hits.Add((i, name.Length));
        return hits;
    }

    [Fact]
    public async Task Rename_across_two_open_files_is_undone_in_both_on_disk_and_in_memory()
    {
        using var dir = new TempDir();
        const string mainText = "survey main\n  equate zz9@cave p1\nendsurvey\n";
        const string caveText = "survey cave\n  centreline\n    data normal from to length compass clino\n    zz9 p1 5 0 0\n  endcentreline\nendsurvey\n";
        var main = dir.Write("main.th", mainText);
        var cave = dir.Write("cave.th", caveText);
        var mainDoc = new FileDocumentViewModel(main, mainText, new MeasurementsViewModel());
        var caveDoc = new FileDocumentViewModel(cave, caveText, new MeasurementsViewModel());
        var open = new ObservableCollection<FileDocumentViewModel> { mainDoc, caveDoc };
        var docs = Docs(open, mainDoc);
        var history = new WorkspaceUndoService(docs);

        var record = await RenameApplier.ApplyAsync(
            [new RenameFileChanges(main, mainText, Hits(mainText, "zz9")), new RenameFileChanges(cave, caveText, Hits(caveText, "zz9"))],
            "zz9", "entrance", docs, null, null, history);

        Assert.NotNull(record);
        Assert.Same(record, history.NextUndo);
        Assert.Equal(2, record!.Files.Count);
        Assert.Contains("entrance@cave", File.ReadAllText(main));
        Assert.Contains("entrance p1", File.ReadAllText(cave));
        Assert.Contains("entrance p1", caveDoc.DocumentText);

        var outcome = await history.UndoAsync();

        Assert.Equal(WorkspaceUndoStatus.Done, outcome.Status);
        Assert.Equal(mainText, File.ReadAllText(main));
        Assert.Equal(caveText, File.ReadAllText(cave));
        Assert.Equal(mainText, mainDoc.DocumentText);
        Assert.Equal(caveText, caveDoc.DocumentText);
        Assert.False(mainDoc.IsDirty);
        Assert.False(caveDoc.IsDirty);
        Assert.Null(history.NextUndo);
        Assert.Same(record, history.NextRedo);

        await history.RedoAsync();
        Assert.Contains("entrance p1", File.ReadAllText(cave));
        Assert.Contains("entrance p1", caveDoc.DocumentText);
    }

    [Fact]
    public async Task Undo_is_refused_without_writing_when_a_file_was_edited_after_the_operation()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "station old\n");
        var b = dir.Write("b.th", "equate old x\n");
        var docs = Docs([]);
        var history = new WorkspaceUndoService(docs);
        await RenameApplier.ApplyAsync(
            [new RenameFileChanges(a, "station old\n", [(8, 3)]), new RenameFileChanges(b, "equate old x\n", [(7, 3)])],
            "old", "new", docs, null, null, history);

        File.AppendAllText(b, "# later work\n");
        var outcome = await history.UndoAsync();

        Assert.Equal(WorkspaceUndoStatus.Refused, outcome.Status);
        Assert.Contains("b.th", outcome.Message);
        Assert.Equal("station new\n", File.ReadAllText(a));            // not half-undone
        Assert.Equal("equate new x\n# later work\n", File.ReadAllText(b));
        Assert.NotNull(history.NextUndo);
    }

    [Fact]
    public async Task A_rename_whose_file_cannot_be_written_says_so_and_puts_the_written_files_back()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "station old\n");
        var b = dir.Write("b.th", "equate old x\n");
        File.SetAttributes(b, FileAttributes.ReadOnly);   // chmod a-w off Windows
        try
        {
            var aDoc = new FileDocumentViewModel(a, "station old\n", new MeasurementsViewModel());
            var docs = Docs([aDoc], aDoc);
            var history = new WorkspaceUndoService(docs);

            var ex = await Assert.ThrowsAsync<RenameWriteException>(() => RenameApplier.ApplyAsync(
                [new RenameFileChanges(a, "station old\n", [(8, 3)]), new RenameFileChanges(b, "equate old x\n", [(7, 3)])],
                "old", "new", docs, null, null, history));

            Assert.Contains("b.th", ex.Message);
            Assert.DoesNotContain(dir.Path, ex.Message);             // a sentence, not the .NET text with the full path
            Assert.IsType<UnauthorizedAccessException>(ex.InnerException);   // kept for Copy details
            Assert.Equal("station old\n", File.ReadAllText(a));      // not half-renamed
            Assert.Equal("station old\n", aDoc.DocumentText);
            Assert.Null(history.NextUndo);
        }
        finally { File.SetAttributes(b, FileAttributes.Normal); }
    }

    [Fact]
    public async Task An_edit_batch_with_a_created_file_is_undone_by_deleting_it_and_restoring_the_edit()
    {
        using var dir = new TempDir();
        var main = dir.Write("main.th", "survey main\nendsurvey\n");
        var trip = Path.Combine(dir.Path, "trips", "t1.th");
        var docs = Docs([]);
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "SetActiveThconfigAsync" ? Task.FromResult(true) : null);
        var editor = new WorkspaceEditService(docs, session);
        var batch = new EditBatch(
            [new FileTextEdits(main, [TextEdit.Insert(12, "  input trips/t1.th\n")])],
            [new FileCreate(trip, "survey t1\nendsurvey\n")],
            []);

        var outcome = await editor.ApplyAsync(batch);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(2, outcome.Changes!.Count);

        var history = new WorkspaceUndoService(docs, session);
        history.Push(new WorkspaceUndoRecord("New trip", "Created t1.th", outcome.Changes));
        var undone = await history.UndoAsync();

        Assert.Equal(WorkspaceUndoStatus.Done, undone.Status);
        Assert.False(File.Exists(trip));
        Assert.Equal("survey main\nendsurvey\n", File.ReadAllText(main));

        await history.RedoAsync();
        Assert.Equal("survey t1\nendsurvey\n", File.ReadAllText(trip));
        Assert.Equal("survey main\n  input trips/t1.th\nendsurvey\n", File.ReadAllText(main));
    }

    [Fact]
    public async Task Undoing_a_switch_to_utf8_writes_the_file_back_in_its_old_encoding()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "old.th");
        const string text = "encoding iso-8859-1\nsurvey Bédeilhac\nendsurvey\n";
        var original = System.Text.Encoding.Latin1.GetBytes(text);
        File.WriteAllBytes(path, original);
        var docs = Docs([]);
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "SetActiveThconfigAsync" ? Task.FromResult(true) : null);
        // What Normalise ▸ UTF-8 plans: the encoding line rewritten, so the file is saved as UTF-8.
        var outcome = await new WorkspaceEditService(docs, session).ApplyAsync(new EditBatch(
            [new FileTextEdits(path, [new TextEdit(9, 10, "iso-8859-1", "utf-8")])], [], []));
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("encoding utf-8\nsurvey Bédeilhac\nendsurvey\n", File.ReadAllText(path, System.Text.Encoding.UTF8));

        var history = new WorkspaceUndoService(docs, session);
        history.Push(new WorkspaceUndoRecord("Normalise project", "", outcome.Changes!));
        Assert.Equal(WorkspaceUndoStatus.Done, (await history.UndoAsync()).Status);

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_created_file_open_in_the_editor_is_closed_when_its_creation_is_undone()
    {
        using var dir = new TempDir();
        var trip = Path.Combine(dir.Path, "t1.th");
        File.WriteAllText(trip, "survey t1\nendsurvey\n");
        var open = new ObservableCollection<FileDocumentViewModel> { new(trip, "survey t1\nendsurvey\n", new MeasurementsViewModel()) };
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(
            m => m.Name == "get_Documents" ? open : null,
            (m, args) => { if (m.Name == "CloseDocument") open.Remove((FileDocumentViewModel)args[0]!); });
        var history = new WorkspaceUndoService(docs);
        history.Push(new WorkspaceUndoRecord("New trip", "Created t1.th",
            [new FileChange(trip, null, "survey t1\nendsurvey\n") { Created = File.ReadAllBytes(trip) }]));

        var outcome = await history.UndoAsync();

        Assert.Equal(WorkspaceUndoStatus.Done, outcome.Status);
        Assert.False(File.Exists(trip));
        Assert.Empty(open);
    }

    [Fact]
    public void Edit_menu_names_the_operation_and_the_notification_offers_its_undo()
    {
        UiThread.Run(() =>
        {
            using var dir = new TempDir();
            var a = dir.Write("a.th", "station new\n");
            var b = dir.Write("b.th", "equate new x\n");
            var docs = new ScriptedDocumentService();
            var history = new WorkspaceUndoService(docs);
            var vm = MainWindowViewModelHost.Create(docs, workspaceUndo: history);
            Assert.False(vm.UndoWorkspaceCommand.CanExecute(null));

            history.Push(new WorkspaceUndoRecord("Rename", "Renamed old → new in 2 file(s).",
                [new FileChange(a, "station old\n", "station new\n"), new FileChange(b, "equate old x\n", "equate new x\n")]));

            Assert.Equal("Undo Rename", vm.WorkspaceUndoHeader);
            Assert.True(vm.UndoWorkspaceCommand.CanExecute(null));
            var toast = vm.Notifications.History[0];
            Assert.Contains("a.th", toast.Message);
            Assert.Contains("b.th", toast.Message);
            Assert.True(toast.HasAction);

            toast.Action!();   // the toast's Undo button

            Assert.Equal("station old\n", File.ReadAllText(a));
            Assert.Equal("equate old x\n", File.ReadAllText(b));
            Assert.False(vm.UndoWorkspaceCommand.CanExecute(null));
            Assert.Equal("Redo Rename", vm.WorkspaceRedoHeader);
        });
    }
}
