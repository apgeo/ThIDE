// BLEND module marker + schema-version anchors (BA-B1 scaffold).
//
// These consts are the stable version anchors for the two JSON documents the module
// emits — scene-meta.json (geometry stage, BA-B3, doc 02) and the SceneSpec preset
// format (emitter, BA-B5, doc 03). Bump them when a breaking field change lands; the
// (de)serializers key their migration on these numbers. Kept here so tests and both
// producers/consumers reference a single source of truth.

namespace Therion.Blender;

/// <summary>
/// Module-wide constants for the Blender-animation (BLEND) feature. See
/// <c>.claude/blender-animation/</c> for the design docs (private).
/// </summary>
public static class BlenderModule
{
    /// <summary>Schema version of <c>scene-meta.json</c> (geometry stage, BA-B3).</summary>
    public const int SceneMetaSchemaVersion = 1;

    /// <summary>Schema version of the <see cref="SceneSpec"/> preset JSON (emitter, BA-B5).</summary>
    public const int SceneSpecSchemaVersion = 1;
}
