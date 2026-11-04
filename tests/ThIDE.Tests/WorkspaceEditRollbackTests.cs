using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

/// <summary>
/// A batch whose writes fail after some of its edits are already on disk. The service writes creates,
/// then copies, then edits, so only a failure in a later edit reaches the rollback of an earlier one.
/// The failure is injected where the service tells the session about the write it is about to make,
/// which works on every platform and as root, unlike a read-only file.
/// </summary>
public class WorkspaceEditRollbackTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-edit-rollback-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string P(string name) => Path.Combine(_root, name);

    /// <summary>A service whose session refuses the first announced write to <paramref name="failAt"/>.</summary>
    private static WorkspaceEditService ServiceFailingAt(string failAt)
    {
        int armed = 1;
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name == "get_Documents" ? new ObservableCollection<FileDocumentViewModel>() : null);
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "SetActiveThconfigAsync" ? Task.FromResult(true) : null,
            (m, args) =>
            {
                if (m.Name == "SuppressSelfWrite" && (string)args[0]! == failAt && Interlocked.Exchange(ref armed, 0) == 1)
                    throw new IOException("disk full (injected)");
            });
        return new WorkspaceEditService(docs, session);
    }

    [Fact]
    public async Task A_later_edit_failing_puts_back_the_bytes_of_the_edits_creates_and_copies_written_before_it()
    {
        // A Latin-1 file with CRLF line ends: its own bytes must come back, not a re-encoding.
        var first = P("first.th");
        var firstBytes = Encoding.Latin1.GetBytes("encoding iso8859-1\r\nsurvey Grotte_Café\r\nendsurvey\r\n");
        File.WriteAllBytes(first, firstBytes);
        var second = P("second.th");
        File.WriteAllText(second, "survey b\nendsurvey\n");
        var third = P("third.th");
        File.WriteAllText(third, "survey c\nendsurvey\n");
        File.WriteAllText(P("src.th"), "survey copied\nendsurvey\n");
        var batch = new EditBatch(
            [new FileTextEdits(first, [TextEdit.Insert(40, "  # a\r\n")]),
             new FileTextEdits(second, [TextEdit.Insert(9, "  # b\n")]),
             new FileTextEdits(third, [TextEdit.Insert(9, "  # c\n")])],
            [new FileCreate(P("new/notes.th"), "# notes\n")],
            [new FileCopy(P("src.th"), P("copies/deep/copy.th"))]);

        var outcome = await ServiceFailingAt(third).ApplyAsync(batch);

        Assert.False(outcome.Success);
        Assert.Contains("injected", outcome.Error);
        Assert.Empty(outcome.Written);
        Assert.Equal(firstBytes, File.ReadAllBytes(first));
        Assert.Equal("survey b\nendsurvey\n", File.ReadAllText(second));
        Assert.Equal("survey c\nendsurvey\n", File.ReadAllText(third));
        Assert.False(Directory.Exists(P("new")), "the folder made for the created file was left behind");
        Assert.False(Directory.Exists(P("copies")), "the folders made for the copy were left behind");
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task The_second_of_two_edits_failing_restores_the_first()
    {
        var first = P("main.th");
        File.WriteAllText(first, "survey a\nendsurvey\n");
        var second = P("other.th");
        File.WriteAllText(second, "survey b\nendsurvey\n");
        var batch = new EditBatch(
            [new FileTextEdits(first, [TextEdit.Insert(9, "  input other.th\n")]),
             new FileTextEdits(second, [TextEdit.Insert(9, "  # b\n")])],
            [], []);

        var outcome = await ServiceFailingAt(second).ApplyAsync(batch);

        Assert.False(outcome.Success);
        Assert.Empty(outcome.Written);
        Assert.Equal("survey a\nendsurvey\n", File.ReadAllText(first));
        Assert.Equal("survey b\nendsurvey\n", File.ReadAllText(second));
    }
}
