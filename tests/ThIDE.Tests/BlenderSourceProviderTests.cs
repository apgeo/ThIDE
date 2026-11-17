// Tests for the concrete workspace source provider (BA-B12): artifact discovery over the real
// OutputArtifactCollector against a temp workspace, and RenderSource resolution for an external
// file and a discovered artifact. A fake IWorkspaceSession supplies the root path.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Therion.Blender.Sources;
using Therion.Build;
using Therion.Cave3D;
using Therion.Core;
using Therion.Semantics;
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

    // ---- leads ----

    private static readonly SourceLead[] WorkspaceLeads = [new("cave.upper.7", "continuation", "draught")];

    private static BlenderSourceProvider ProviderWithLeads(string? root)
        => new(() => root, new OutputArtifactCollector(), () => WorkspaceLeads);

    [Fact]
    public async Task Acquire_WorkspaceArtifact_CarriesTheWorkspaceLeads()
    {
        File.WriteAllBytes(Path.Combine(_root, "build.lox"), new byte[10]);

        var source = await ProviderWithLeads(_root).AcquireAsync(ModelSourceRequest.ForWorkspace());

        Assert.Equal(WorkspaceLeads, source.Leads);
    }

    [Fact]
    public async Task Acquire_FileInsideTheWorkspace_CarriesTheWorkspaceLeads()
    {
        // The panel picks a listed artifact by path, as an external-file request.
        var rez = Path.Combine(_root, "rez");
        Directory.CreateDirectory(rez);
        var file = Path.Combine(rez, "cave.lox");
        File.WriteAllBytes(file, new byte[10]);

        var source = await ProviderWithLeads(_root).AcquireAsync(ModelSourceRequest.ForExternalFile(file));

        Assert.Equal(WorkspaceLeads, source.Leads);
    }

    [Fact]
    public async Task Acquire_FileFromElsewhere_GetsNoLeads()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "thide-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        try
        {
            var file = Path.Combine(elsewhere, "other.lox");
            File.WriteAllBytes(file, new byte[10]);
            // A sibling folder whose name starts with the workspace's must not count as inside it.
            var sibling = _root + "-copy";
            Directory.CreateDirectory(sibling);
            var siblingFile = Path.Combine(sibling, "copy.lox");
            File.WriteAllBytes(siblingFile, new byte[10]);

            var provider = ProviderWithLeads(_root);
            Assert.Empty((await provider.AcquireAsync(ModelSourceRequest.ForExternalFile(file))).Leads);
            Assert.Empty((await provider.AcquireAsync(ModelSourceRequest.ForExternalFile(siblingFile))).Leads);
            Directory.Delete(sibling, recursive: true);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void OpenLeads_KeepsUnfinishedStationFlagLeads_WithTheirNotes()
    {
        Lead[] leads =
        [
            new("cave.a.1", LeadKind.ContinuationFlag, "big black space", default) { FlagLabel = "continuation" },
            new("cave.a.2", LeadKind.StationFlag, "dig", default) { FlagLabel = "dig" },
            new("cave.a.3", LeadKind.ContinuationFlag, "continuation", default) { FlagLabel = "continuation" },
            new("cave.a.4", LeadKind.ContinuationFlag, "continuation", default) { FlagLabel = "continuation" },
            new("cave.a.5", LeadKind.ContinuationFlag, "continuation", default) { FlagLabel = "continuation" },
            new("cave.a.6", LeadKind.CommentMarker, "QM here", default),
            new("cave.a.7", LeadKind.DeadEnd, "leaf node", default),
        ];
        var status = new System.Collections.Generic.Dictionary<string, string>
        {
            ["cave.a.3"] = "pushed",
            ["cave.a.4"] = "DEAD",
            ["cave.a.5"] = "checked",
        };

        var open = BlenderSourceProvider.OpenLeads(leads, l => status.GetValueOrDefault(l, "open"));

        Assert.Equal(["cave.a.1", "cave.a.2", "cave.a.5"], open.Select(l => l.Station));
        Assert.Equal("big black space", open[0].Note);
        Assert.Equal("continuation", open[0].Kind);
        Assert.Equal("dig", open[1].Kind);
    }
}
