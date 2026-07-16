namespace Therion.Blender.Sources;

/// <summary>
/// Supplies the model artifacts present for the active workspace. The app implements
/// this over <c>Therion.Build.OutputArtifactCollector</c> (scan the build working
/// directory, keep the <c>.lox</c>/<c>.3d</c> outputs); tests fake it.
/// </summary>
public interface IModelArtifactProvider
{
    /// <summary>All discovered model artifacts (order irrelevant; the resolver ranks them).</summary>
    IReadOnlyList<ModelArtifact> Discover();
}

/// <summary>
/// Re-runs Therion to produce a fresh <c>.lox</c> when no current artifact exists. The
/// app implements this by writing a temporary thconfig (<c>export model -fmt loch</c>)
/// and driving <c>ITherionCompiler.CompileAsync</c>; tests fake it.
/// </summary>
public interface IModelReExporter
{
    /// <summary>Re-exports the workspace model and returns the produced artifact, or
    /// <c>null</c> when the export produced nothing (the caller then falls back or
    /// reports "no source"). Should surface hard failures as exceptions.</summary>
    Task<ModelArtifact?> ReExportAsync(CancellationToken cancellationToken = default);
}
