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
    /// <summary>Blender is writing the output from the rendered frames, with the texts over them:
    /// the second stage of a render in two stages.</summary>
    Composing,
    /// <summary>Collecting/verifying output products (BA-B11).</summary>
    CollectingOutputs,
    /// <summary>Terminal: the job finished (success or handled failure).</summary>
    Done,
    /// <summary>Waiting in the queue of renders for the one before it to end: renders run one at
    /// a time.</summary>
    Queued,
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
/// <param name="SecondsPerFrame">How long a frame takes, measured from the frames written so far
/// (<see cref="FrameClock"/>), else <c>null</c>.</param>
/// <param name="SecondsLeft">How long the frames still to render will take at that pace, else
/// <c>null</c>.</param>
/// <param name="Segment">Which segment of a film (shot) is being rendered, from 1, else <c>null</c>.</param>
/// <param name="SegmentCount">How many segments the film has, else <c>null</c>.</param>
public readonly record struct RenderProgress(
    RenderPhase Phase,
    string Message,
    double? Fraction = null,
    int? Frame = null,
    int? FrameCount = null,
    double? SampleFraction = null,
    string? Device = null,
    double? SecondsPerFrame = null,
    double? SecondsLeft = null,
    int? Segment = null,
    int? SegmentCount = null);

/// <summary>
/// The pace of a render from the frames it has written: seconds per frame, and the time the
/// frames still to render will take. The first frame starts the clock (it carries the scene's
/// set-up); frames that were there already (a render that goes on) are not counted as rendered.
/// </summary>
public sealed class FrameClock
{
    private int? _firstDone;
    private TimeSpan _firstAt;
    private int _lastDone;

    /// <summary>A frame was written: <paramref name="done"/> of <paramref name="total"/> frames
    /// are there, <paramref name="elapsed"/> after the job started. Returns the pace, or nulls
    /// until two frames have been seen.</summary>
    public (double? SecondsPerFrame, double? SecondsLeft) Tick(int done, int total, TimeSpan elapsed)
    {
        if (_firstDone is null || done < _lastDone)
        {
            _firstDone = done;
            _firstAt = elapsed;
            _lastDone = done;
            return (null, null);
        }
        // Frames that appear in a jump were there already: they took no time.
        if (done > _lastDone + 1) _firstDone += done - _lastDone - 1;
        _lastDone = done;
        int rendered = done - _firstDone.Value;
        if (rendered <= 0) return (null, null);
        double perFrame = (elapsed - _firstAt).TotalSeconds / rendered;
        return (perFrame, perFrame * Math.Max(0, total - done));
    }
}
