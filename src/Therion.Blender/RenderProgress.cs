// Progress record reported across every phase of the pipeline (BA-B1 scaffold).
//
// The runner (BA-B10) drives a 3-tier progress protocol (D-08): tier 1 = the generated
// script's own `THIDE:` lines (phase + explicit fraction), tier 2 = Blender's native
// `Fra:N` frame regex, tier 3 = an indeterminate spinner. All three collapse into this
// one immutable record so the UI (BA-B12) has a single shape to marshal onto the UI
// thread (NFR-04). Conversion/script-generation (Phase 1–2) reuse it for their own
// coarse phases so a whole job reports through one channel.

namespace Therion.Blender;

/// <summary>Coarse pipeline phase a <see cref="RenderProgress"/> tick belongs to.</summary>
public enum RenderPhase
{
    /// <summary>Locating/exporting the source model artifact (BA-B4).</summary>
    AcquiringSource,
    /// <summary>Parsing <c>.lox</c>/<c>.3d</c> into a <see cref="CaveModel"/> (BA-B2).</summary>
    ParsingModel,
    /// <summary>Geometry stage + writing PLY/scene-meta assets (BA-B3).</summary>
    ConvertingGeometry,
    /// <summary>Emitting the Blender Python script (BA-B5–B9).</summary>
    GeneratingScript,
    /// <summary>Blender is rendering frames (BA-B10/B11).</summary>
    Rendering,
    /// <summary>Collecting/verifying output products (BA-B11).</summary>
    CollectingOutputs,
    /// <summary>Terminal: the job finished (success or handled failure).</summary>
    Done,
}

/// <summary>
/// One immutable progress tick. Optional numeric fields are <c>null</c> when the current
/// tier can't supply them (e.g. tier-3 spinner has no <see cref="Fraction"/>).
/// </summary>
/// <param name="Phase">The coarse phase this tick belongs to.</param>
/// <param name="Message">Short human-facing status (localized at the UI layer, not here).</param>
/// <param name="Fraction">Overall completion 0..1 when known, else <c>null</c> (spinner).</param>
/// <param name="Frame">Current frame number while rendering, else <c>null</c>.</param>
/// <param name="FrameCount">Total frames in the render, else <c>null</c>.</param>
/// <param name="SampleFraction">Per-frame sample progress 0..1 (Cycles), else <c>null</c>.</param>
/// <param name="Device">Render device actually in use (e.g. "OptiX", "CPU"), once known.</param>
public readonly record struct RenderProgress(
    RenderPhase Phase,
    string Message,
    double? Fraction = null,
    int? Frame = null,
    int? FrameCount = null,
    double? SampleFraction = null,
    string? Device = null);
