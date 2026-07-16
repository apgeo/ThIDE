namespace Therion.Blender.Sources;

/// <summary>
/// Thrown when no convertible model source can be resolved: a missing/unreadable
/// external file, an unrecognized format, or no workspace artifact with re-export
/// unavailable or unsuccessful. The UI turns this into the friendly "no model to
/// render — build the project first" surface (NFR-05).
/// </summary>
public sealed class ModelSourceNotFoundException(string message) : Exception(message);
