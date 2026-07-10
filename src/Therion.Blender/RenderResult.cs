// Outcome of a render job (BA-B1 scaffold placeholder).
//
// Returned by IBlenderRenderService.RenderAsync. BA-B11 fills this in: the collected
// output products (video / frame sequence / stills), the render device actually used,
// wall-clock timings, and the path to job.log for failure surfacing (BA-B13). Kept
// minimal at scaffold time so the seam compiles.

using System.Collections.Immutable;

namespace Therion.Blender;

/// <summary>
/// Result of a render job. Placeholder — output products, device, and timings land in
/// BA-B11 (see file header).
/// </summary>
public sealed class RenderResult
{
    /// <summary>Whether the job produced its expected outputs.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Paths of the produced output files (video/frames/stills); empty until BA-B11.</summary>
    public ImmutableArray<string> OutputPaths { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Render device actually used (e.g. "OptiX", "CPU"), once BA-B10 reports it.</summary>
    public string? Device { get; init; }
}
