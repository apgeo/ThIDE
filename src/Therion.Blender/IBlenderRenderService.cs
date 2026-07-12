// The module's public seam (BA-B1 scaffold).
//
// One orchestration interface the app shell, CLI, and (later) MCP tool call. It hides the
// internal pipeline stages — source acquisition (BA-B4), parsing into CaveModel (BA-B2),
// geometry + PLY/scene-meta writing (BA-B3), Python emission (BA-B5–B9), and headless
// Blender execution (BA-B10/B11) — behind the two user-facing execution modes of FR-10:
// automated render, and export-only. Direct project reference from the app (D-05); no
// abstraction layer in Therion.Processing.Abstractions until a 2nd consumer appears.
//
// Everything runs off the UI thread; progress arrives via IProgress<RenderProgress> and
// the caller marshals it (NFR-04). Cancellation is cooperative (CancellationToken); the
// runner also tears down the Blender process tree (BA-B10). Implementations land batch by
// batch — the concrete BlenderRenderService is assembled once its stages exist.

using System.Threading;
using System.Threading.Tasks;
using Therion.Blender.Sources;

namespace Therion.Blender;

/// <summary>The model to render and the workspace leads to annotate it with. The caller
/// (UI/CLI) resolves the model itself — workspace artifact / external file / re-export — via
/// the app-side source-acquisition adapters, then hands the result here.</summary>
public sealed record RenderSource(ResolvedModelSource Model, IReadOnlyList<SourceLead> Leads)
{
    public RenderSource(ResolvedModelSource model) : this(model, []) { }
}

/// <summary>
/// Orchestrates the convert → generate → render pipeline for the BLEND module. See
/// <c>.claude/blender-animation/</c> (private) for the design; requirements FR-03/09/10/11.
/// </summary>
public interface IBlenderRenderService
{
    /// <summary>
    /// Automated mode (FR-10b): convert the resolved source, generate the script, and run
    /// Blender headless, reporting progress and honoring cancellation, returning the collected
    /// output products (or a typed failure — never throws for a render failure).
    /// </summary>
    /// <param name="spec">The render presentation (camera/materials/lighting/labels/output).</param>
    /// <param name="source">The resolved model + leads to render.</param>
    /// <param name="progress">Receives progress ticks; may be <c>null</c>.</param>
    /// <param name="ct">Cancels the job (and tears down Blender) cooperatively.</param>
    Task<RenderResult> RenderAsync(
        SceneSpec spec,
        RenderSource source,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export-only mode (FR-10a): convert + generate, writing the Blender Python script and its
    /// assets (PLY + scene-meta, or a self-contained <c>.py</c>) into <paramref name="outputDir"/>
    /// for the user to run manually. Does not launch Blender.
    /// </summary>
    /// <param name="spec">The render presentation.</param>
    /// <param name="source">The resolved model + leads to convert.</param>
    /// <param name="outputDir">Destination folder for the script and assets.</param>
    /// <param name="progress">Receives progress ticks; may be <c>null</c>.</param>
    /// <param name="ct">Cancels generation cooperatively.</param>
    /// <returns>The absolute path of the written Blender Python script.</returns>
    Task<string> ExportScriptAsync(
        SceneSpec spec,
        RenderSource source,
        string outputDir,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default);
}
