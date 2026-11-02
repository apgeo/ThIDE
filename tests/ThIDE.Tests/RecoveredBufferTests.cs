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

// Restoring a buffer recovered after a crash must always end with an unsaved document holding the
// recovered text — the restore deletes the recovery copies afterwards.
public class RecoveredBufferTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewService() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    [Fact]
    public async Task A_file_the_text_guard_blocks_is_restored_with_the_recovered_text()
    {
        using var dir = new TempDir();
        // A NUL byte in the first 8 KB makes a normal open refuse the file (the user had used "Open anyway").
        var path = dir.Write("odd.th", "survey odd\n\0\nendsurvey\n");
        var svc = NewService();
        await svc.OpenFileAsync(path);
        Assert.Empty(svc.Documents);

        var doc = await svc.OpenRecoveredAsync(path, "survey odd\n  # unsaved work\nendsurvey\n");

        Assert.Same(doc, Assert.Single(svc.Documents));
        Assert.Equal("survey odd\n  # unsaved work\nendsurvey\n", doc.DocumentText);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public async Task A_file_that_still_opens_gets_the_recovered_text_over_it()
    {
        using var dir = new TempDir();
        var path = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = NewService();

        var doc = await svc.OpenRecoveredAsync(path, "survey a\n  # unsaved work\nendsurvey\n");

        Assert.Equal("survey a\n  # unsaved work\nendsurvey\n", doc.DocumentText);
        Assert.True(doc.IsDirty);
        Assert.True(await svc.SaveDocumentAsync(doc));
        Assert.Equal("survey a\n  # unsaved work\nendsurvey\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_deleted_file_is_restored_unsaved_and_saving_writes_it_again()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "gone.th");
        var svc = NewService();

        var doc = await svc.OpenRecoveredAsync(path, "survey gone\nendsurvey\n");

        Assert.True(doc.IsDirty, "a clean document would close without a prompt and drop the recovered text");
        Assert.True(await svc.SaveDocumentAsync(doc));
        Assert.Equal("survey gone\nendsurvey\n", File.ReadAllText(path));
    }
}
