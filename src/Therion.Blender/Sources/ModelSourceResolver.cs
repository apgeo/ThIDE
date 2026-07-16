// Source acquisition (BA-B4): decide which model file to convert — an explicit external
// file, the workspace's freshest build artifact, or a fresh re-export — and report its
// provenance and freshness. Pure policy over two fakeable seams (IModelArtifactProvider,
// IModelReExporter); the app supplies the mechanism (OutputArtifactCollector /
// ITherionCompiler) at wiring time so the library stays dependency-free (D-20).

using Therion.Blender.Parsing;

namespace Therion.Blender.Sources;

/// <summary>Resolves a <see cref="ModelSourceRequest"/> to a concrete model file.</summary>
public sealed class ModelSourceResolver
{
    private readonly IModelArtifactProvider? _artifacts;
    private readonly IModelReExporter? _reExporter;

    /// <param name="artifacts">Workspace artifact discovery (required for workspace requests).</param>
    /// <param name="reExporter">Re-export mechanism (required only when a request allows re-export).</param>
    public ModelSourceResolver(IModelArtifactProvider? artifacts = null, IModelReExporter? reExporter = null)
    {
        _artifacts = artifacts;
        _reExporter = reExporter;
    }

    public async Task<ResolvedModelSource> ResolveAsync(ModelSourceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrEmpty(request.ExternalFilePath))
            return ResolveExternalFile(request.ExternalFilePath);

        return await ResolveWorkspaceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static ResolvedModelSource ResolveExternalFile(string path)
    {
        if (!File.Exists(path))
            throw new ModelSourceNotFoundException($"Model file not found: {path}");

        var format = DetectFormat(path);
        if (format == CaveSourceFormat.Unknown)
            throw new ModelSourceNotFoundException(
                $"Unrecognized model format for \"{path}\": expected a Therion .lox or Survex .3d file.");

        return new ResolvedModelSource
        {
            Path = path,
            Format = format,
            Kind = ModelSourceKind.ExternalFile,
            IsFresh = true,
        };
    }

    private async Task<ResolvedModelSource> ResolveWorkspaceAsync(ModelSourceRequest request, CancellationToken cancellationToken)
    {
        if (_artifacts is null)
            throw new InvalidOperationException(
                "A workspace source request needs an IModelArtifactProvider; none was supplied.");

        var best = PickBest(_artifacts.Discover(), request.PreferLox);
        bool stale = best is not null && request.SourceModifiedUtc is { } since && best.LastWriteUtc < since;

        if (best is null || stale)
        {
            if (request.AllowReExport && _reExporter is not null)
            {
                var exported = await _reExporter.ReExportAsync(cancellationToken).ConfigureAwait(false);
                if (exported is not null)
                    return new ResolvedModelSource
                    {
                        Path = exported.Path,
                        Format = exported.Format,
                        Kind = ModelSourceKind.ReExported,
                        IsFresh = true,
                        Artifact = exported,
                    };
            }

            if (best is null)
                throw new ModelSourceNotFoundException(
                    "No .lox/.3d model artifact was found for the workspace. Build the project (or enable re-export) first.");

            // Stale, but it's all we have — hand it back with a warning.
            return new ResolvedModelSource
            {
                Path = best.Path,
                Format = best.Format,
                Kind = ModelSourceKind.WorkspaceArtifact,
                IsFresh = false,
                StalenessReason = "The model artifact is older than the survey sources; rebuild for an up-to-date render.",
                Artifact = best,
            };
        }

        return new ResolvedModelSource
        {
            Path = best.Path,
            Format = best.Format,
            Kind = ModelSourceKind.WorkspaceArtifact,
            IsFresh = true,
            Artifact = best,
        };
    }

    /// <summary>Best artifact: prefer <c>.lox</c> (walls) when asked, then newest.</summary>
    internal static ModelArtifact? PickBest(IReadOnlyList<ModelArtifact> artifacts, bool preferLox)
    {
        ModelArtifact? best = null;
        foreach (var candidate in artifacts)
        {
            if (candidate.Format is not (CaveSourceFormat.Lox or CaveSourceFormat.Survex3d)) continue;
            if (best is null || IsBetter(candidate, best, preferLox)) best = candidate;
        }
        return best;
    }

    private static bool IsBetter(ModelArtifact candidate, ModelArtifact current, bool preferLox)
    {
        if (preferLox)
        {
            bool candidateLox = candidate.Format == CaveSourceFormat.Lox;
            bool currentLox = current.Format == CaveSourceFormat.Lox;
            if (candidateLox != currentLox) return candidateLox;
        }
        return candidate.LastWriteUtc > current.LastWriteUtc;
    }

    /// <summary>Format by extension, falling back to a small content sniff.</summary>
    private static CaveSourceFormat DetectFormat(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".lox") return CaveSourceFormat.Lox;
        if (ext == ".3d") return CaveSourceFormat.Survex3d;

        try
        {
            Span<byte> head = stackalloc byte[64];
            using var stream = File.OpenRead(path);
            int read = stream.Read(head);
            return CaveModelReader.Detect(head[..read], path);
        }
        catch
        {
            return CaveSourceFormat.Unknown;
        }
    }
}
