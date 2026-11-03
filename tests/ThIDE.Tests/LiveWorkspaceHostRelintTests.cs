// The in-app mutation path re-lints the files it just wrote through LiveWorkspaceHost.ReloadAsync. The
// result's NewErrors/Diagnostics must describe the file as written, not the model from before the write.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Therion.Mcp;
using Therion.Mcp.Mutations;
using Therion.Workspace.Editing;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public class LiveWorkspaceHostRelintTests
{
    // Inline bridge: runs the delegate on the calling thread, so no Avalonia dispatcher is involved.
    private sealed class InlineUiBridge : IUiBridge
    {
        public bool IsAvailable => true;
        public bool FollowAgent => true;
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
        public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);
        public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
            Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);
        private static Task<UiActionResult> Ok() => Task.FromResult(new UiActionResult(true, ""));
        public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => Ok();
        public Task<UiActionResult> FocusToolAsync(string toolId) => Ok();
        public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => Ok();
        public Task<UiActionResult> ShowInThreeDAsync(string station) => Ok();
        public Task<UiActionResult> ShowToastAsync(string message, string kind) => Ok();
    }

    private sealed class NoDirtyBuffers : IUnsavedBufferProvider
    {
        public IReadOnlyList<(string Path, string Text)> DirtyBuffers() => [];
    }

    private const string ValidCave =
        "survey test\n  centerline\n    data normal from to length compass clino\n    1 2 10.0 90 0\n  endcenterline\nendsurvey\n";

    [Fact]
    public async Task Mutation_relint_sees_the_written_text()
    {
        using var dir = new TempDir();
        var cave = dir.Write("cave.th", ValidCave);
        var cfg = dir.Write("cave.thconfig", "source cave.th\n");
        var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        try
        {
            await session.SetRootAsync(dir.Path);
            Assert.True(await session.SetActiveThconfigAsync(cfg), "fixture thconfig failed to load");

            // No open editors: the write goes to disk, and the re-lint has to read it back from there.
            var host = new LiveWorkspaceHost(session, new NoDirtyBuffers(), new InlineUiBridge());
            var engine = new MutationEngine(host);

            // A length reading that is not a number introduces one error that the file did not have.
            int start = ValidCave.IndexOf("10.0", StringComparison.Ordinal);
            var plan = new MutationPlan([new EditFile(cave, [new TextEdit(start, 4, "10.0", "abc")])]);

            var result = await engine.ApplyAsync(plan, dryRun: false);

            Assert.True(result.Ok, result.Error?.Message);
            Assert.Contains("abc", File.ReadAllText(cave));   // the write itself landed
            Assert.Equal(1, result.Data!.NewErrors);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
