// Render presets (BA-B9, FR-12) — a named, reusable render template. A preset is a
// presentation-only SceneSpec (engine/materials/lighting/camera/labels/animation/output
// kind) wrapped with a name + description; its source path and output directory/base name
// are placeholders that ToRenderSpec fills in for a specific job. Stored as versioned JSON
// (PresetSerializer), the same SceneSpec the CLI/emitter consume.

namespace Therion.Blender.Presets;

/// <summary>A named render template (built-in gallery item or user preset).</summary>
public sealed record RenderPreset
{
    /// <summary>Envelope schema version (the nested <see cref="Spec"/> keeps its own).</summary>
    public int Version { get; init; } = BlenderModule.PresetSchemaVersion;

    /// <summary>Display name (also the basis for the on-disk file slug).</summary>
    public required string Name { get; init; }

    /// <summary>Optional one-line description shown in the gallery.</summary>
    public string? Description { get; init; }

    /// <summary>True for the shipped gallery presets (read-only in the UI).</summary>
    public bool BuiltIn { get; init; }

    /// <summary>The presentation template. Its <see cref="SourceSpec.PlyPath"/> and
    /// <see cref="OutputSpec.OutputDirectory"/>/<see cref="OutputSpec.BaseName"/> are
    /// placeholders — <see cref="ToRenderSpec"/> supplies the real ones.</summary>
    public required SceneSpec Spec { get; init; }

    /// <summary>Produces a runnable spec by grafting this preset's presentation choices onto
    /// a concrete source and output location. The result is what the validator/emitter see.</summary>
    public SceneSpec ToRenderSpec(SourceSpec source, string outputDirectory, string baseName)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Spec with
        {
            Source = source,
            Output = Spec.Output with { OutputDirectory = outputDirectory, BaseName = baseName },
        };
    }
}
