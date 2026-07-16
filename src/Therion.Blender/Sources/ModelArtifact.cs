namespace Therion.Blender.Sources;

/// <summary>
/// A discovered model file the module can convert. The app's artifact provider maps
/// Therion's <c>OutputArtifact</c> (from a build's working directory) onto this slim
/// record so the library stays dependency-free (D-20).
/// </summary>
/// <param name="Path">Absolute path to the <c>.lox</c>/<c>.3d</c> file.</param>
/// <param name="Format">Which format it is.</param>
/// <param name="SizeBytes">File size, for the freshness/preflight display.</param>
/// <param name="LastWriteUtc">Last-write time, the freshness signal.</param>
public sealed record ModelArtifact(string Path, CaveSourceFormat Format, long SizeBytes, DateTimeOffset LastWriteUtc);
