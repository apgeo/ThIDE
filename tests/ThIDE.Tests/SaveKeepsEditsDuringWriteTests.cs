using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using ThIDE.Services;

namespace ThIDE.Tests;

// Text typed while a save's file write is in flight belongs to the user; the save must not
// replace it with the snapshot it wrote, nor mark the document clean.
public class SaveKeepsEditsDuringWriteTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewService() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    private static string LargeSurvey()
    {
        // Big enough that the write is still in flight when the next edit lands.
        var sb = new StringBuilder("survey big\n  centreline\n    data normal from to length compass clino\n");
        for (int i = 0; i < 300_000; i++)
            sb.Append("    ").Append(i).Append(' ').Append(i + 1).Append(" 3.25 180.5 -2.0\n");
        sb.Append("  endcentreline\nendsurvey\n");
        return sb.ToString();
    }

    [Fact]
    public async Task Save_keeps_an_edit_made_while_the_write_is_in_flight()
    {
        using var dir = new TempDir();
        var original = LargeSurvey();
        var path = dir.Write("big.th", original);
        var svc = NewService();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();

        var edited = original + "# first edit\n";
        doc.DocumentText = edited;
        Assert.True(doc.IsDirty);

        // The save snapshots the text and awaits the file write; the method returns at that await.
        var save = svc.SaveDocumentAsync(doc);
        Assert.False(save.IsCompleted, "the save finished before control came back, so no edit can land during the write");
        var typedDuringWrite = edited + "# typed while saving\n";
        doc.DocumentText = typedDuringWrite;
        await save;

        Assert.Equal(typedDuringWrite, doc.DocumentText);
        Assert.True(doc.IsDirty, "the text typed during the save is not on disk, so the document must still be dirty");
    }
}
