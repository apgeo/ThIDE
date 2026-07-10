namespace Therion.Blender.Parsing;

/// <summary>
/// Thrown when a cave model file is malformed, truncated, or in an unsupported format
/// version. Parsers throw this rather than returning partial data — corrupt input must
/// fail cleanly, never hang or over-allocate (all counts and offsets are validated
/// against the actual number of bytes present before anything is allocated).
/// </summary>
public sealed class CaveFileFormatException(string message) : Exception(message);
