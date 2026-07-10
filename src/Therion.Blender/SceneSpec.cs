// The full, JSON-serializable description of a render job (BA-B1 scaffold placeholder).
//
// SceneSpec is the single source of truth the whole module orbits: the UI (BA-B12) binds
// to it, presets (BA-B9) persist it, the emitter (BA-B5–B9) turns it into a Blender Python
// script, and the CLI (BA-B14) / future MCP tool consume it as JSON. It is versioned by
// BlenderModule.SceneSpecSchemaVersion so old preset files migrate forward (BA-B5/B9).
//
// BA-B5 (★, Fable 5) fleshes this out: source selection, mesh/materials, camera template
// + parameters, lighting rig, labels/overlays, engine + GPU, output product settings.
// Placeholder at scaffold time so the seam compiles; determinism matters (NFR-03) so the
// eventual object model stays plain data (no behavior) for golden-file testability.

namespace Therion.Blender;

/// <summary>
/// Complete, serializable description of one render job. Placeholder — the real fields
/// (source, camera, materials, lighting, labels, engine, output) land in BA-B5.
/// </summary>
public sealed class SceneSpec
{
    /// <summary>Schema version for forward migration; defaults to the current module version.</summary>
    public int Version { get; init; } = BlenderModule.SceneSpecSchemaVersion;
}
