namespace Therion.Blender.Sources;

/// <summary>
/// A survey lead / QM as supplied by the workspace leads register, before it is matched
/// to a 3-D position. The app maps <c>Therion.Semantics.Lead</c> onto this (its
/// <c>Location</c> → <see cref="Station"/>, its kind label → <see cref="Kind"/>, its
/// description → <see cref="Note"/>); <see cref="LeadsEnricher"/> resolves the position
/// from the model and writes it into <c>scene-meta.json</c>.
/// </summary>
/// <param name="Station">The station name the lead is attached to.</param>
/// <param name="Kind">Human-readable lead kind (e.g. "continuation flag", "sketch point").</param>
/// <param name="Note">Optional description/comment.</param>
public sealed record SourceLead(string Station, string Kind, string? Note = null);
