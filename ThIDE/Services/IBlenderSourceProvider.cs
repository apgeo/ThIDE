// App seam for the Blender module's source acquisition (BA-B12). The library stays
// dependency-free (D-20): it takes a RenderSource; the app resolves one from the active
// workspace (discovered .lox/.3d artifacts, external files, or a Therion re-export) and
// gathers the leads register. The ViewModel depends on this interface so it is testable with
// a fake, and the concrete adapter over OutputArtifactCollector/ITherionCompiler/LeadAnalysis
// is wired separately.

using Therion.Blender;
using Therion.Blender.Sources;

namespace ThIDE.Services;

/// <summary>Supplies model sources for the Blender Animation panel.</summary>
public interface IBlenderSourceProvider
{
    /// <summary>The model artifacts discovered for the active workspace's last build
    /// (for the source dropdown). Empty when there is no workspace or no build output.</summary>
    IReadOnlyList<ModelArtifact> DiscoverArtifacts();

    /// <summary>Resolves a source request into a <see cref="RenderSource"/> (the model file
    /// plus the workspace leads), re-exporting via Therion when asked and allowed. Throws
    /// <see cref="ModelSourceNotFoundException"/> when nothing usable can be produced.</summary>
    Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default);
}
