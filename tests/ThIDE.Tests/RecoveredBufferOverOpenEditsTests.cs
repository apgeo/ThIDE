using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// Restoring a crash-recovered buffer for a file that is already open with newer, unsaved edits must
/// not replace those edits without asking.
/// </summary>
public class RecoveredBufferOverOpenEditsTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    [Fact]
    public async Task A_buffer_open_with_newer_edits_is_not_replaced()
    {
        using var dir = new TempDir();
        var path = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = new DocumentService(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

        await svc.OpenFileAsync(path);
        var doc = Assert.Single(svc.Documents);
        const string newer = "survey a\n  # edits made after the restart\nendsurvey\n";
        doc.DocumentText = newer;
        Assert.True(doc.IsDirty);

        await svc.OpenRecoveredAsync(path, "survey a\n  # crash-time edits\nendsurvey\n");

        Assert.True(newer == doc.DocumentText,
            "restoring the recovered copy silently replaced newer unsaved edits in the open buffer");
    }
}
