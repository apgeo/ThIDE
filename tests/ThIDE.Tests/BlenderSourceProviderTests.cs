// Tests for the concrete workspace source provider (BA-B12): artifact discovery over the real
// OutputArtifactCollector against a temp workspace, and RenderSource resolution for an external
// file and a discovered artifact. A fake IWorkspaceSession supplies the root path.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Build;
using ThIDE.Services;

namespace ThIDE.Tests;

public class BlenderSourceProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-src-" + Guid.NewGuid().ToString("N"));

    public BlenderSourceProviderTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static BlenderSourceProvider Provider(string? root)
        => new(() => root, new OutputArtifactCollector());

    [Fact]
    public void DiscoverArtifacts_FindsLoxAnd3d_NewestFirst_SkippingOtherOutputs()
    {
        var rez = Path.Combine(_root, "rez");
        Directory.CreateDirectory(rez);
        File.WriteAllBytes(Path.Combine(rez, "cave.lox"), new byte[10]);
        File.WriteAllBytes(Path.Combine(rez, "cave.3d"), new byte[10]);
        File.WriteAllText(Path.Combine(rez, "map.pdf"), "not a model");
        // Make the .3d newer so ordering is observable.
        File.SetLastWriteTimeUtc(Path.Combine(rez, "cave.3d"), DateTime.UtcNow.AddMinutes(5));

        var artifacts = Provider(_root).DiscoverArtifacts();

        Assert.Equal(2, artifacts.Count);
        Assert.DoesNotContain(artifacts, a => a.Path.EndsWith(".pdf"));
        Assert.Equal(CaveSourceFormat.Survex3d, artifacts[0].Format); // newest first
        Assert.Contains(artifacts, a => a.Format == CaveSourceFormat.Lox);
    }

    [Fact]
    public void DiscoverArtifacts_NoWorkspace_IsEmpty()
    {
        Assert.Empty(Provider(root: null).DiscoverArtifacts());
    }

    [Fact]
    public async Task Acquire_ExternalFile_ResolvesIt()
    {
        var file = Path.Combine(_root, "external.lox");
        File.WriteAllBytes(file, new byte[10]);

        var source = await Provider(_root).AcquireAsync(ModelSourceRequest.ForExternalFile(file));

        Assert.Equal(file, source.Model.Path);
        Assert.Equal(CaveSourceFormat.Lox, source.Model.Format);
        Assert.Empty(source.Leads);
    }

    [Fact]
    public async Task Acquire_Workspace_UsesTheDiscoveredArtifact()
    {
        File.WriteAllBytes(Path.Combine(_root, "build.lox"), new byte[10]);
        var source = await Provider(_root).AcquireAsync(ModelSourceRequest.ForWorkspace());
        Assert.EndsWith("build.lox", source.Model.Path);
    }

    [Fact]
    public async Task Acquire_WorkspaceWithNoArtifact_ReportsNotFound()
    {
        await Assert.ThrowsAsync<ModelSourceNotFoundException>(
            () => Provider(_root).AcquireAsync(ModelSourceRequest.ForWorkspace()));
    }
}
