using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Tests;

// The Save / Don't save / Cancel decision shared by closing a tab and closing the main window: a
// document may close only when the user discards it or its text really is on disk.
public class SaveBeforeCloseTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewService() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    private static Task<SaveBeforeBuildChoice> Answer(SaveBeforeBuildChoice choice) => Task.FromResult(choice);

    private static async Task<(DocumentService Svc, FileDocumentViewModel Doc, string Path)> OpenEdited(TempDir dir, string name, string text, string edited)
    {
        var path = dir.Write(name, text);
        var svc = NewService();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();
        doc.DocumentText = edited;
        return (svc, doc, path);
    }

    [Fact]
    public async Task Nothing_dirty_closes_without_asking()
    {
        using var dir = new TempDir();
        var path = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = NewService();
        await svc.OpenFileAsync(path);
        bool asked = false;

        var close = await SaveBeforeClose.ConfirmAsync(svc.Documents, _ => { asked = true; return Answer(SaveBeforeBuildChoice.Cancel); }, svc);

        Assert.True(close);
        Assert.False(asked);
    }

    [Theory]
    [InlineData(SaveBeforeBuildChoice.Cancel, false)]
    [InlineData(SaveBeforeBuildChoice.DontSave, true)]
    public async Task Cancel_keeps_the_documents_and_dont_save_lets_them_go(SaveBeforeBuildChoice choice, bool expected)
    {
        using var dir = new TempDir();
        var (svc, doc, path) = await OpenEdited(dir, "a.th", "survey a\nendsurvey\n", "survey a\n  # edited\nendsurvey\n");
        IReadOnlyList<FileDocumentViewModel>? asked = null;

        var close = await SaveBeforeClose.ConfirmAsync(svc.Documents, d => { asked = d; return Answer(choice); }, svc);

        Assert.Equal(expected, close);
        Assert.Same(doc, Assert.Single(asked!));
        Assert.Equal("survey a\nendsurvey\n", File.ReadAllText(path));   // neither answer writes
    }

    [Fact]
    public async Task Save_of_a_file_deleted_on_disk_writes_it_again_and_closes()
    {
        using var dir = new TempDir();
        var (svc, doc, path) = await OpenEdited(dir, "a.th", "survey a\nendsurvey\n", "survey a\n  # kept\nendsurvey\n");
        File.Delete(path);

        var close = await SaveBeforeClose.ConfirmAsync(svc.Documents, _ => Answer(SaveBeforeBuildChoice.Save), svc);

        Assert.True(close);
        Assert.Equal("survey a\n  # kept\nendsurvey\n", File.ReadAllText(path));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task Save_that_writes_nothing_keeps_the_document_open()
    {
        using var dir = new TempDir();
        // U+0219 (s with comma below) is not in ISO-8859-2, so the save cannot write it.
        var (svc, doc, path) = await OpenEdited(dir, "b.th", "encoding iso8859-2\nsurvey b\nendsurvey\n",
            "encoding iso8859-2\nsurvey b\n  # peșteră\nendsurvey\n");

        var close = await SaveBeforeClose.ConfirmAsync(svc.Documents, _ => Answer(SaveBeforeBuildChoice.Save), svc);

        Assert.False(close);
        Assert.True(doc.IsDirty);
        Assert.Contains(doc, svc.Documents);
    }

    [Fact]
    public async Task Save_that_throws_keeps_the_document_open_and_reports_why()
    {
        using var dir = new TempDir();
        var (svc, doc, path) = await OpenEdited(dir, "gone/a.th", "survey a\nendsurvey\n", "survey a\n  # edited\nendsurvey\n");
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        string? reported = null;

        var close = await SaveBeforeClose.ConfirmAsync(svc.Documents, _ => Answer(SaveBeforeBuildChoice.Save), svc, m => reported = m);

        Assert.False(close);
        Assert.True(doc.IsDirty);
        Assert.False(string.IsNullOrEmpty(reported));
    }
}
