// Concrete source acquisition for the Blender module (BA-B12). Discovers the active
// workspace's .lox/.3d build artifacts via the existing OutputArtifactCollector, and resolves
// a RenderSource through the library's tested ModelSourceResolver (external file / workspace
// artifact / freshness). Re-export and leads-register wiring are follow-ups (see notes).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Build;

namespace ThIDE.Services;

/// <summary>Supplies Blender model sources from the active workspace.</summary>
public sealed class BlenderSourceProvider : IBlenderSourceProvider
{
    private readonly Func<string?> _workspaceRoot;
    private readonly IOutputArtifactCollector _collector;

    /// <param name="workspaceRoot">Accessor for the active workspace root (from the session);
    /// injected as a function so the provider doesn't take the whole session interface.</param>
    public BlenderSourceProvider(Func<string?> workspaceRoot, IOutputArtifactCollector collector)
    {
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
    }

    public IReadOnlyList<ModelArtifact> DiscoverArtifacts()
    {
        var root = _workspaceRoot();
        if (string.IsNullOrEmpty(root)) return [];

        var result = new List<ModelArtifact>();
        foreach (var artifact in _collector.Collect(root))
        {
            var format = FormatFor(artifact.Path);
            if (format is null) continue; // .lox/.3d only — skip PDFs/SVGs/logs
            result.Add(new ModelArtifact(artifact.Path, format.Value, artifact.SizeBytes, artifact.LastWriteUtc));
        }
        result.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc)); // newest first
        return result;
    }

    public async Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
    {
        // Reuse the tested resolver (external file / workspace artifact / freshness). Re-export
        // (IModelReExporter over a temp thconfig + ITherionCompiler) and leads (from the register)
        // are follow-ups; until then a workspace request with no artifact reports "build first".
        var resolver = new ModelSourceResolver(new ArtifactAdapter(DiscoverArtifacts), reExporter: null);
        var resolved = await resolver.ResolveAsync(request, ct).ConfigureAwait(false);
        return new RenderSource(resolved, []);
    }

    private static CaveSourceFormat? FormatFor(string path)
        => path.EndsWith(".lox", StringComparison.OrdinalIgnoreCase) ? CaveSourceFormat.Lox
         : path.EndsWith(".3d", StringComparison.OrdinalIgnoreCase) ? CaveSourceFormat.Survex3d
         : null;

    private sealed class ArtifactAdapter(Func<IReadOnlyList<ModelArtifact>> discover) : IModelArtifactProvider
    {
        public IReadOnlyList<ModelArtifact> Discover() => discover();
    }
}
