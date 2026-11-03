using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using Therion.Semantics;
using Therion.Workspace;

namespace Therion.Mcp.Tests;

/// <summary>
/// The jail resolves symlinks before it compares, so every path it hands a tool is canonical. The file
/// list and the per-file model are keyed by whatever path the loader walked, which is not. A project
/// reached through a symlink therefore has to be matched in the same form on both sides, or files that
/// are plainly part of the project are reported as not being in it.
/// </summary>
public class SymlinkedWorkspaceTests
{
    /// <summary>Headless host: the root is canonical, but a sourced subdirectory is a symlink.</summary>
    [Fact]
    public async Task File_scoped_tools_find_a_file_sourced_through_a_symlinked_subdirectory()
    {
        if (OperatingSystem.IsWindows()) return;   // creating a symlink needs a privilege there

        using var fixture = FixtureWorkspace.Create();
        Directory.Move(fixture.PathTo("caves"), fixture.PathTo("caves_real"));
        Directory.CreateSymbolicLink(fixture.PathTo("caves"), fixture.PathTo("caves_real"));

        await using var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var diagnostics = await new DiagnosticsTools(host).GetDiagnostics(file: "caves/upper.th");
        Assert.True(diagnostics.Ok,
            $"get_diagnostics on a file sourced through a symlinked directory answered: {diagnostics.Error?.Code} {diagnostics.Error?.Message}");

        var format = await new FormatTools(host, new MutationEngine(host)).FormatFile("caves/upper.th");
        Assert.True(format.Ok,
            $"format_file on a file sourced through a symlinked directory answered: {format.Error?.Code} {format.Error?.Message}");
    }

    /// <summary>
    /// In-app host: the session's root is the directory the user opened, uncanonicalised, as
    /// WorkspaceSessionService stores it. Every tool must still accept the project's own files.
    /// </summary>
    [Fact]
    public async Task Tools_accept_project_files_when_the_root_itself_is_reached_through_a_symlink()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = FixtureWorkspace.Create();
        var link = fixture.Root + "_link";
        Directory.CreateSymbolicLink(link, fixture.Root);

        try
        {
            await using var workspace = await WorkspaceLoader.OpenAsync(Path.Combine(link, "project.thconfig"));
            var host = new UncanonicalHost(workspace, link);

            var diagnostics = await new DiagnosticsTools(host).GetDiagnostics(file: "caves/upper.th");
            var edit = await new EditTools(host, new MutationEngine(host))
                .EditFile("caves/upper.th", "12.5", "12.6", dryRun: false);

            Assert.True(diagnostics.Ok && edit.Ok,
                $"with a symlinked root, get_diagnostics answered: {diagnostics.Error?.Code} {diagnostics.Error?.Message} | "
                + $"edit_file answered: {edit.Error?.Code} {edit.Error?.Message}");
            Assert.Contains("12.6", File.ReadAllText(fixture.PathTo("caves", "upper.th")));
        }
        finally
        {
            try { File.Delete(link); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Mirrors LiveWorkspaceHost: the root and the file list come straight from the session, in the
    /// form the user opened them, and loading over MCP is refused.
    /// </summary>
    private sealed class UncanonicalHost : IWorkspaceHost
    {
        private readonly TherionWorkspace _workspace;
        private WorkspaceSemanticModel _model;

        public UncanonicalHost(TherionWorkspace workspace, string root)
        {
            _workspace = workspace;
            _model = workspace.BuildSemanticModel();
            Root = root;
        }

        public bool IsLoaded => true;
        public string? Root { get; }
        public string? EntryPointPath => _workspace.EntryPointPath;

        public ValueTask<WorkspaceSnapshot> GetAsync(CancellationToken ct = default) =>
            new(new WorkspaceSnapshot(_workspace.LoadedFiles, _model, Root!, EntryPointPath!, []));

        public ValueTask<WorkspaceSnapshot> LoadAsync(string pathOrFolder, CancellationToken ct = default) =>
            throw new WorkspaceLoadException("The in-app server follows the project open in the IDE.", []);

        public ValueTask<WorkspaceSnapshot> ReloadAsync(CancellationToken ct = default)
        {
            _model = _workspace.BuildSemanticModel();
            return GetAsync(ct);
        }
    }
}
