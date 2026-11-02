using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using ThIDE.Services;

namespace ThIDE.Tests;

// A save that writes nothing must say so: the close guard treats a save that returns normally as
// "the edits are on disk" and lets the tab close.
public class SaveWithoutWriteTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewService() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    [Fact]
    public async Task Save_of_a_file_deleted_on_disk_either_writes_it_or_fails_loudly()
    {
        using var dir = new TempDir();
        var path = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = NewService();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();

        doc.DocumentText = "survey a\n  # edited after the file was removed\nendsurvey\n";
        Assert.True(doc.IsDirty);
        File.Delete(path);

        // Returns without throwing...
        await svc.SaveDocumentAsync(doc);

        // ...so the edits must now be on disk and the document clean. Neither holds.
        Assert.True(File.Exists(path), "the save returned normally but recreated nothing on disk");
        Assert.False(doc.IsDirty, "the save returned normally but the document is still unsaved");
    }

    [Fact]
    public async Task Save_of_text_the_declared_encoding_cannot_hold_either_writes_or_fails_loudly()
    {
        using var dir = new TempDir();
        var path = dir.Write("b.th", "encoding iso8859-2\nsurvey b\nendsurvey\n");
        var svc = NewService();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();

        // U+0219 (s with comma below) is not in ISO-8859-2.
        doc.DocumentText = "encoding iso8859-2\nsurvey b\n  # peșteră\nendsurvey\n";
        Assert.True(doc.IsDirty);

        await svc.SaveDocumentAsync(doc);

        Assert.False(doc.IsDirty, "the save returned normally but the document is still unsaved");
    }
}
