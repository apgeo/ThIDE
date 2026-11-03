using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// When a recovered buffer cannot go over the open file (it has newer unsaved edits), the recovered
/// text still opens, as its own unsaved document, so neither version is lost.
/// </summary>
public class RecoveredBufferBesideOpenEditsTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    [Fact]
    public async Task The_recovered_text_opens_beside_newer_edits_as_an_unsaved_document()
    {
        using var dir = new TempDir();
        var path = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = new DocumentService(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));
        await svc.OpenFileAsync(path);
        svc.Documents[0].DocumentText = "survey a\n  # newer\nendsurvey\n";

        const string crashText = "survey a\n  # crash-time edits\nendsurvey\n";
        var recovered = await svc.OpenRecoveredAsync(path, crashText);

        Assert.Equal(2, svc.Documents.Count);
        Assert.Equal(crashText, recovered.DocumentText);
        Assert.True(recovered.IsDirty);
        Assert.False(System.IO.File.Exists(recovered.FilePath));
        Assert.Equal("a.recovered.th", System.IO.Path.GetFileName(recovered.FilePath));

        // Restoring the same text again does not open a third copy for the unchanged original.
        var again = await svc.OpenRecoveredAsync(path, svc.Documents[0].DocumentText);
        Assert.Same(svc.Documents[0], again);
        Assert.Equal(2, svc.Documents.Count);
    }
}
