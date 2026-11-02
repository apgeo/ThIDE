using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

/// <summary>
/// An export regenerates an artifact, so replacing yesterday's output is normal. Replacing a survey
/// source file with a CSV is not, and neither is writing over a file the IDE holds open with unsaved
/// edits.
/// </summary>
public class ExportTargetProtectionTests
{
    [Fact]
    public async Task Export_refuses_to_overwrite_a_loaded_survey_source_file()
    {
        using var fixture = FixtureWorkspace.Create();
        var (tools, _) = await LoadedAsync(fixture);
        var survey = fixture.PathTo("caves", "upper.th");
        var before = File.ReadAllText(survey);

        var result = await tools.ExportTables("shots", "csv", target: "caves/upper.th", dryRun: false);

        Assert.False(result.Ok, "export_tables replaced a loaded .th survey source with its CSV output");
        Assert.Equal(before, File.ReadAllText(survey));
    }

    [Fact]
    public async Task Export_refuses_to_overwrite_a_file_that_is_open_and_dirty_in_the_ide()
    {
        using var fixture = FixtureWorkspace.Create();
        var survey = fixture.PathTo("caves", "upper.th");
        var inner = new WorkspaceHost();
        await inner.LoadAsync(fixture.Thconfig);
        var host = new DirtyHost(inner, survey);
        var tools = new ExportTools(host, new MutationEngine(host));
        var before = File.ReadAllText(survey);

        var result = await tools.ExportTables("shots", "csv", target: "caves/upper.th", dryRun: false);

        Assert.False(result.Ok, "export_tables wrote over a file that is open with unsaved edits, forking the editor buffer from disk");
        Assert.Equal(ToolErrorCodes.FileDirty, result.Error!.Code);
        Assert.Equal(before, File.ReadAllText(survey));
    }

    private static async Task<(ExportTools Tools, WorkspaceHost Host)> LoadedAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return (new ExportTools(host, new MutationEngine(host)), host);
    }

    /// <summary>Wraps a real host but reports one file as open-and-dirty, the way the in-app host does.</summary>
    private sealed class DirtyHost(IWorkspaceHost inner, string dirtyAbsolutePath) : IWorkspaceHost
    {
        public bool IsLoaded => inner.IsLoaded;
        public string? Root => inner.Root;
        public string? EntryPointPath => inner.EntryPointPath;
        public async ValueTask<WorkspaceSnapshot> GetAsync(CancellationToken ct = default) =>
            (await inner.GetAsync(ct)) with { DirtyFiles = [dirtyAbsolutePath] };
        public ValueTask<WorkspaceSnapshot> LoadAsync(string p, CancellationToken ct = default) => inner.LoadAsync(p, ct);
        public ValueTask<WorkspaceSnapshot> ReloadAsync(CancellationToken ct = default) => inner.ReloadAsync(ct);
    }
}
