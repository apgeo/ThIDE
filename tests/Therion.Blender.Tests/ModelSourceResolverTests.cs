// Source-acquisition policy tests with fake seams (BA-B4 "service-level tests with fake
// workspace"): external-file resolution, artifact ranking (prefer-lox + newest),
// freshness → re-export, re-export disabled/failed fallbacks, and the not-found paths.

using Therion.Blender;
using Therion.Blender.Sources;

namespace Therion.Blender.Tests;

public class ModelSourceResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-blend-src").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content ?? [1, 2, 3, 4]);
        return path;
    }

    private sealed class FakeArtifacts(params ModelArtifact[] artifacts) : IModelArtifactProvider
    {
        public IReadOnlyList<ModelArtifact> Discover() => artifacts;
    }

    private sealed class FakeReExporter(ModelArtifact? result) : IModelReExporter
    {
        public int Calls { get; private set; }
        public Task<ModelArtifact?> ReExportAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static ModelArtifact Artifact(string path, CaveSourceFormat format, int ageDays) =>
        new(path, format, 100, DateTimeOffset.UtcNow.AddDays(-ageDays));

    [Fact]
    public async Task ExternalLoxFile_ResolvesAsExternal()
    {
        var path = WriteFile("cave.lox");
        var resolver = new ModelSourceResolver();

        var resolved = await resolver.ResolveAsync(ModelSourceRequest.ForExternalFile(path));

        Assert.Equal(ModelSourceKind.ExternalFile, resolved.Kind);
        Assert.Equal(CaveSourceFormat.Lox, resolved.Format);
        Assert.True(resolved.IsFresh);
        Assert.Equal(path, resolved.Path);
    }

    [Fact]
    public async Task ExternalThreeDFile_ResolvesAsSurvex()
    {
        var path = WriteFile("cave.3d");
        var resolved = await new ModelSourceResolver().ResolveAsync(ModelSourceRequest.ForExternalFile(path));
        Assert.Equal(CaveSourceFormat.Survex3d, resolved.Format);
    }

    [Fact]
    public async Task ExternalMissingFile_Throws()
    {
        var request = ModelSourceRequest.ForExternalFile(Path.Combine(_dir, "nope.lox"));
        await Assert.ThrowsAsync<ModelSourceNotFoundException>(() => new ModelSourceResolver().ResolveAsync(request));
    }

    [Fact]
    public async Task ExternalUnknownFormat_Throws()
    {
        var path = WriteFile("cave.xyz", "not a cave model"u8.ToArray());
        var request = ModelSourceRequest.ForExternalFile(path);
        await Assert.ThrowsAsync<ModelSourceNotFoundException>(() => new ModelSourceResolver().ResolveAsync(request));
    }

    [Fact]
    public async Task Workspace_PrefersLoxOverNewerThreeD()
    {
        var lox = Artifact("a.lox", CaveSourceFormat.Lox, ageDays: 5);       // older
        var svx = Artifact("a.3d", CaveSourceFormat.Survex3d, ageDays: 1);   // newer
        var resolver = new ModelSourceResolver(new FakeArtifacts(svx, lox));

        var resolved = await resolver.ResolveAsync(ModelSourceRequest.ForWorkspace());

        Assert.Equal(CaveSourceFormat.Lox, resolved.Format);
        Assert.Equal("a.lox", resolved.Path);
        Assert.Equal(ModelSourceKind.WorkspaceArtifact, resolved.Kind);
    }

    [Fact]
    public async Task Workspace_PicksNewestAmongSameFormat()
    {
        var older = Artifact("old.lox", CaveSourceFormat.Lox, ageDays: 10);
        var newer = Artifact("new.lox", CaveSourceFormat.Lox, ageDays: 2);
        var resolver = new ModelSourceResolver(new FakeArtifacts(older, newer));

        var resolved = await resolver.ResolveAsync(ModelSourceRequest.ForWorkspace());
        Assert.Equal("new.lox", resolved.Path);
    }

    [Fact]
    public async Task Workspace_AcceptsThreeDWhenNoLox()
    {
        var resolver = new ModelSourceResolver(new FakeArtifacts(Artifact("a.3d", CaveSourceFormat.Survex3d, 1)));
        var resolved = await resolver.ResolveAsync(ModelSourceRequest.ForWorkspace());
        Assert.Equal(CaveSourceFormat.Survex3d, resolved.Format);
    }

    [Fact]
    public async Task Workspace_FreshArtifact_IsUsedWithoutReExport()
    {
        var fresh = Artifact("a.lox", CaveSourceFormat.Lox, ageDays: 0);      // now
        var reExporter = new FakeReExporter(Artifact("re.lox", CaveSourceFormat.Lox, 0));
        var resolver = new ModelSourceResolver(new FakeArtifacts(fresh), reExporter);

        var request = ModelSourceRequest.ForWorkspace(
            sourceModifiedUtc: DateTimeOffset.UtcNow.AddDays(-1), allowReExport: true);
        var resolved = await resolver.ResolveAsync(request);

        Assert.True(resolved.IsFresh);
        Assert.Equal(ModelSourceKind.WorkspaceArtifact, resolved.Kind);
        Assert.Equal(0, reExporter.Calls);
    }

    [Fact]
    public async Task Workspace_StaleArtifact_TriggersReExport()
    {
        var stale = Artifact("a.lox", CaveSourceFormat.Lox, ageDays: 30);
        var reExporter = new FakeReExporter(Artifact("re.lox", CaveSourceFormat.Lox, 0));
        var resolver = new ModelSourceResolver(new FakeArtifacts(stale), reExporter);

        var request = ModelSourceRequest.ForWorkspace(
            sourceModifiedUtc: DateTimeOffset.UtcNow.AddDays(-1), allowReExport: true);
        var resolved = await resolver.ResolveAsync(request);

        Assert.Equal(ModelSourceKind.ReExported, resolved.Kind);
        Assert.Equal("re.lox", resolved.Path);
        Assert.True(resolved.IsFresh);
        Assert.Equal(1, reExporter.Calls);
    }

    [Fact]
    public async Task Workspace_StaleArtifact_ReExportDisabled_ReturnsStale()
    {
        var stale = Artifact("a.lox", CaveSourceFormat.Lox, ageDays: 30);
        var resolver = new ModelSourceResolver(new FakeArtifacts(stale));

        var request = ModelSourceRequest.ForWorkspace(
            sourceModifiedUtc: DateTimeOffset.UtcNow.AddDays(-1), allowReExport: false);
        var resolved = await resolver.ResolveAsync(request);

        Assert.False(resolved.IsFresh);
        Assert.NotNull(resolved.StalenessReason);
        Assert.Equal("a.lox", resolved.Path);
    }

    [Fact]
    public async Task Workspace_NoArtifact_ReExports()
    {
        var reExporter = new FakeReExporter(Artifact("re.lox", CaveSourceFormat.Lox, 0));
        var resolver = new ModelSourceResolver(new FakeArtifacts(), reExporter);

        var resolved = await resolver.ResolveAsync(ModelSourceRequest.ForWorkspace(allowReExport: true));

        Assert.Equal(ModelSourceKind.ReExported, resolved.Kind);
        Assert.Equal(1, reExporter.Calls);
    }

    [Fact]
    public async Task Workspace_NoArtifact_NoReExport_Throws()
    {
        var resolver = new ModelSourceResolver(new FakeArtifacts());
        await Assert.ThrowsAsync<ModelSourceNotFoundException>(
            () => resolver.ResolveAsync(ModelSourceRequest.ForWorkspace()));
    }

    [Fact]
    public async Task Workspace_ReExportReturnsNull_FallsBackToStaleArtifact()
    {
        var stale = Artifact("a.lox", CaveSourceFormat.Lox, ageDays: 30);
        var reExporter = new FakeReExporter(result: null); // export produced nothing
        var resolver = new ModelSourceResolver(new FakeArtifacts(stale), reExporter);

        var request = ModelSourceRequest.ForWorkspace(
            sourceModifiedUtc: DateTimeOffset.UtcNow.AddDays(-1), allowReExport: true);
        var resolved = await resolver.ResolveAsync(request);

        Assert.Equal(ModelSourceKind.WorkspaceArtifact, resolved.Kind);
        Assert.False(resolved.IsFresh);
        Assert.Equal(1, reExporter.Calls);
    }

    [Fact]
    public async Task Workspace_WithoutProvider_Throws()
    {
        var resolver = new ModelSourceResolver();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(ModelSourceRequest.ForWorkspace()));
    }
}
