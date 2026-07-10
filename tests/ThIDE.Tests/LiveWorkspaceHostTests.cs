// T-03.2: the in-app MCP host reads the running IDE, not the disk. LiveWorkspaceHost overlays the
// current unsaved editor buffers onto the live session model, so get_diagnostics reports what the user
// sees in the editor — even before a save. Also checks that a burst of concurrent tool calls can't race
// the shared session (the host's gate serializes the buffer revalidation).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Therion.Mcp;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public class LiveWorkspaceHostTests
{
    // Inline bridge: runs the delegate on the calling thread. No Avalonia dispatcher, so the test never
    // hits the headless UI-thread deadlock, and concurrent calls exercise real parallelism.
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

    // Hands the live host a fixed set of "unsaved" buffers instead of the real document list.
    private sealed class FakeBuffers(IReadOnlyList<(string Path, string Text)> buffers) : IUnsavedBufferProvider
    {
        public IReadOnlyList<(string Path, string Text)> DirtyBuffers() => buffers;
    }

    private const string ValidCave =
        "survey test\n  centerline\n    data normal from to length compass clino\n    1 2 10.0 90 0\n  endcenterline\nendsurvey\n";

    // The same survey with a non-numeric length reading — an error the on-disk file does not have.
    private const string BrokenCave =
        "survey test\n  centerline\n    data normal from to length compass clino\n    1 2 twelve 90 0\n  endcenterline\nendsurvey\n";

    private static async Task<(WorkspaceSessionService Session, string CavePath, TempDir Dir)> LoadFixtureAsync()
    {
        var dir = new TempDir();
        var cave = dir.Write("cave.th", ValidCave);
        var cfg = dir.Write("cave.thconfig", "source cave.th\n");
        var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(dir.Path);
        Assert.True(await session.SetActiveThconfigAsync(cfg), "fixture thconfig failed to load");
        return (session, cave, dir);
    }

    [Fact]
    public async Task Diagnostics_reflect_an_unsaved_buffer_not_the_disk_file()
    {
        var (session, cavePath, dir) = await LoadFixtureAsync();
        try
        {
            var bridge = new InlineUiBridge();

            // No dirty buffers → the disk file, which is valid: no errors.
            var cleanHost = new LiveWorkspaceHost(session, new FakeBuffers([]), bridge);
            var clean = await new DiagnosticsTools(cleanHost).GetDiagnostics();
            Assert.True(clean.Ok);
            Assert.Equal(0, clean.Data!.Errors);

            // An unsaved buffer with a non-numeric reading → the live host must report its error.
            var dirtyHost = new LiveWorkspaceHost(session, new FakeBuffers([(cavePath, BrokenCave)]), bridge);
            var dirty = await new DiagnosticsTools(dirtyHost).GetDiagnostics();
            Assert.True(dirty.Ok);
            Assert.True(dirty.Data!.Errors > 0, "the buffer's bad reading should surface as an error");
        }
        finally
        {
            await session.DisposeAsync();
            dir.Dispose();
        }
    }

    [Fact]
    public async Task Concurrent_reads_do_not_race()
    {
        var (session, cavePath, dir) = await LoadFixtureAsync();
        try
        {
            var host = new LiveWorkspaceHost(
                session, new FakeBuffers([(cavePath, BrokenCave)]), new InlineUiBridge());

            // Hammer GetAsync from many threads at once: the host's gate must serialize the buffer
            // revalidation so the shared session never throws a cross-thread "collection modified".
            var tasks = Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
            {
                var snap = await host.GetAsync();
                Assert.NotNull(snap.Model);
            }));
            await Task.WhenAll(tasks);   // reaching here without an exception is the assertion
        }
        finally
        {
            await session.DisposeAsync();
            dir.Dispose();
        }
    }

    // Regression (code review #3): the in-app LoadedFiles must include the .thconfig entry point and any
    // .th2 the project loads — not just the .th files. Before the fix it came from model.PerFile.Keys,
    // which holds only .th, so list_files/format_file/workspace_info silently dropped the rest.
    [Fact]
    public async Task List_files_includes_the_thconfig_and_th2_not_just_th()
    {
        var dir = new TempDir();
        dir.Write("cave.th", "input scrap.th2\n" + ValidCave);
        dir.Write("scrap.th2", "encoding utf-8\nscrap s1 -projection plan\nendscrap\n");
        var cfg = dir.Write("cave.thconfig", "source cave.th\n");
        var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        try
        {
            await session.SetRootAsync(dir.Path);
            Assert.True(await session.SetActiveThconfigAsync(cfg));

            var host = new LiveWorkspaceHost(session, new FakeBuffers([]), new InlineUiBridge());
            var files = await new WorkspaceTools(host).ListFiles();

            Assert.True(files.Ok);
            Assert.Contains(files.Data!.Files, f => f.EndsWith(".thconfig", StringComparison.Ordinal));
            Assert.Contains(files.Data.Files, f => f.EndsWith(".th2", StringComparison.Ordinal));
        }
        finally
        {
            await session.DisposeAsync();
            dir.Dispose();
        }
    }

    // Regression (code review #2): an offset edit to a file open with unsaved changes would splice the
    // buffer's offsets into the disk bytes — so the in-app mutation path refuses it with file_dirty
    // rather than risk corrupting it (the T-03.6 dirty-file policy will supersede this).
    [Fact]
    public async Task Editing_an_open_dirty_file_is_refused_with_file_dirty()
    {
        var (session, cavePath, dir) = await LoadFixtureAsync();
        try
        {
            // cave.th is open with an unsaved edit (still-valid text so the model builds).
            var dirtyText = ValidCave.Replace("10.0", "11.0", StringComparison.Ordinal);
            var host = new LiveWorkspaceHost(session, new FakeBuffers([(cavePath, dirtyText)]), new InlineUiBridge());

            var rename = new RenameTools(host, new MutationEngine(host));
            var result = await rename.RenameSymbol("test", "test2", kind: "survey", dryRun: false);

            Assert.False(result.Ok);
            Assert.Equal(ToolErrorCodes.FileDirty, result.Error!.Code);
        }
        finally
        {
            await session.DisposeAsync();
            dir.Dispose();
        }
    }
}
