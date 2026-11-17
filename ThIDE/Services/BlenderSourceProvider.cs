// Concrete source acquisition for the Blender module. Discovers the active workspace's
// .lox/.3d build artifacts via the existing OutputArtifactCollector, resolves a RenderSource
// through the library's tested ModelSourceResolver (external file / workspace artifact /
// freshness), re-exports the model when the workspace has none (when a re-exporter is given), and
// attaches the workspace's open leads to a model that belongs to the workspace.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Build;
using Therion.Cave3D;
using Therion.Semantics;
using Therion.Workspace;

namespace ThIDE.Services;

/// <summary>Supplies Blender model sources from the active workspace.</summary>
public sealed class BlenderSourceProvider : IBlenderSourceProvider
{
    private readonly Func<string?> _workspaceRoot;
    private readonly IOutputArtifactCollector _collector;
    private readonly Func<IReadOnlyList<SourceLead>>? _leads;
    private readonly IModelReExporter? _reExporter;
    private readonly Func<DateTimeOffset?>? _sourcesModifiedUtc;

    /// <param name="workspaceRoot">Accessor for the active workspace root (from the session);
    /// injected as a function so the provider doesn't take the whole session interface.</param>
    /// <param name="leads">The workspace's leads to mark in the render (see
    /// <see cref="OpenLeads"/>); null for none.</param>
    /// <param name="reExporter">Exports the model when the workspace has no current one and the
    /// request allows it (see <see cref="BlenderModelReExporter"/>); null for none.</param>
    /// <param name="sourcesModifiedUtc">When the project's sources last changed on disk (see
    /// <see cref="NewestSource"/>): a build artifact older than that is stale. Null for no check.</param>
    public BlenderSourceProvider(Func<string?> workspaceRoot, IOutputArtifactCollector collector,
        Func<IReadOnlyList<SourceLead>>? leads = null, IModelReExporter? reExporter = null,
        Func<DateTimeOffset?>? sourcesModifiedUtc = null)
    {
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _leads = leads;
        _reExporter = reExporter;
        _sourcesModifiedUtc = sourcesModifiedUtc;
    }

    /// <summary>The newest write time of the project's files: every file of the workspace's
    /// semantic model and the active thconfig. Null when none of them exists on disk.</summary>
    public static DateTimeOffset? NewestSource(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        DateTimeOffset? newest = null;
        foreach (var file in files)
        {
            if (string.IsNullOrEmpty(file)) continue;
            var info = new FileInfo(file);
            if (!info.Exists) continue;
            var at = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (newest is null || at > newest) newest = at;
        }
        return newest;
    }

    /// <summary><see cref="NewestSource(IEnumerable{string})"/> of a workspace and its thconfig.</summary>
    public static DateTimeOffset? NewestSource(WorkspaceSemanticModel? workspace, string? thconfig)
    {
        var files = new List<string>();
        if (workspace is not null) files.AddRange(workspace.PerFile.Keys);
        if (!string.IsNullOrEmpty(thconfig)) files.Add(thconfig);
        return NewestSource(files);
    }

    /// <summary>
    /// The leads a render marks: the explicit station-flag leads of the leads register
    /// (continuation / dig / air-draught — the register's default view), minus those whose
    /// lifecycle status says they are finished ("pushed" or "dead"). The note is the station's
    /// comment or its flag words.
    /// </summary>
    public static IReadOnlyList<SourceLead> OpenLeads(IEnumerable<Lead> leads, Func<string, string> statusOf)
    {
        ArgumentNullException.ThrowIfNull(leads);
        ArgumentNullException.ThrowIfNull(statusOf);
        var result = new List<SourceLead>();
        foreach (var lead in leads)
        {
            if (!lead.IsStationFlag) continue;
            string status = statusOf(lead.Location);
            if (string.Equals(status, "pushed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "dead", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new SourceLead(lead.Location, lead.KindLabel, lead.Description));
        }
        return result;
    }

    /// <summary><see cref="OpenLeads"/> of a workspace's semantic model and status store.</summary>
    public static IReadOnlyList<SourceLead> OpenLeads(
        WorkspaceSemanticModel? workspace, ILeadStatusStore status, string? root)
        => OpenLeads(LeadAnalysis.Analyze(workspace), location => status.Get(root, location));

    public bool HasProject => !string.IsNullOrEmpty(_workspaceRoot());

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
        // Reuse the tested resolver (external file / workspace artifact / freshness / re-export).
        var resolver = new ModelSourceResolver(new ArtifactAdapter(DiscoverArtifacts), _reExporter);
        if (request.ExternalFilePath is null && request.SourceModifiedUtc is null && _sourcesModifiedUtc is not null)
            request = request with { SourceModifiedUtc = _sourcesModifiedUtc() };
        var resolved = await resolver.ResolveAsync(request, ct).ConfigureAwait(false);
        // The leads are the workspace's: a model from elsewhere could share station names with
        // it and get leads it does not have.
        var leads = _leads is not null && BelongsToWorkspace(resolved) ? _leads() : [];
        return new RenderSource(resolved, leads);
    }

    private bool BelongsToWorkspace(ResolvedModelSource source)
    {
        if (source.Kind is ModelSourceKind.WorkspaceArtifact or ModelSourceKind.ReExported) return true;
        var root = _workspaceRoot();
        if (string.IsNullOrEmpty(root)) return false;
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return Path.GetFullPath(source.Path).StartsWith(folder, comparison);
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
