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
    /// <param name="options">How the render is made: which scene builder, a draft, what becomes
    /// of the frames of a render in two stages.</param>
    /// <param name="progress">Receives progress ticks; may be <c>null</c>.</param>
    /// <param name="ct">Cancels the job (and tears down Blender) cooperatively.</param>
    Task<RenderResult> RenderAsync(
        SceneSpec spec,
        RenderSource source,
        RenderOptions options,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>The render with the default <see cref="RenderOptions"/>: the scene builder is
    /// chosen automatically, and nothing is kept of a render's frames.</summary>
    Task<RenderResult> RenderAsync(
        SceneSpec spec,
        RenderSource source,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default)
        => RenderAsync(spec, source, new RenderOptions(), progress, ct);

    /// <summary>
    /// Renders a film of several shots (<see cref="Storyboard"/>): every shot's frames in a
    /// Blender of its own, into a folder of its own inside the frames folder, then the film from
    /// them, joined as the storyboard says, with its texts over it. A shot whose frames are
    /// there, rendered from the same scene, is not rendered again: with
    /// <see cref="RenderOptions.KeepFrames"/> or a <see cref="RenderOptions.FramesDirectory"/> a
    /// film that was stopped goes on with the shot it stopped in, and a film of which one shot
    /// was changed renders that shot alone. <see cref="RenderOptions.Draft"/> is a draft of
    /// every shot: an animatic of the whole film.
    /// </summary>
    /// <exception cref="ArgumentException">The storyboard is invalid, or cannot be planned for
    /// the model.</exception>
    Task<RenderResult> RenderStoryboardAsync(
        Storyboard storyboard,
        RenderSource source,
        RenderOptions options,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("This render service does not render storyboards.");

    /// <summary>
    /// Export-only mode (FR-10a) of the script generator: convert + generate, writing the
    /// generated Blender Python script and its assets (PLY + scene-meta, or a self-contained
    /// <c>.py</c>) into <paramref name="outputDir"/> for the user to run manually. Does not launch
    /// Blender. A generated script does not show everything a render shows
    /// (<see cref="SceneBuilders.NotDrawnByTheGenerator"/>); <see cref="ExportAsync"/> exports the
    /// scene the render's own builder builds.
    /// </summary>
    /// <param name="spec">The render presentation.</param>
    /// <param name="source">The resolved model + leads to convert.</param>
    /// <param name="outputDir">Destination folder for the script and assets.</param>
    /// <param name="progress">Receives progress ticks; may be <c>null</c>.</param>
    /// <param name="ct">Cancels generation cooperatively.</param>
    /// <param name="interactive">When true, emit a GUI-inspection script (builds the scene, frames
    /// the model, skips the output settings and final render) instead of the headless render script.</param>
    /// <returns>The absolute path of the written Blender Python script.</returns>
    Task<string> ExportScriptAsync(
        SceneSpec spec,
        RenderSource source,
        string outputDir,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default,
        bool interactive = false);

    /// <summary>
    /// Exports the scene for someone to run in Blender themselves, built by the same builder a
    /// render uses: by default the player's folder (a scene plan, the player, the meshes and the
    /// images, a README and scripts that start Blender), or one script with all of it inside
    /// (<see cref="ExportOptions.SingleFile"/>); with <see cref="SceneBuilder.Generator"/> the
    /// generated script of <see cref="ExportScriptAsync"/>. Does not launch Blender.
    /// </summary>
    /// <param name="outputDir">Destination folder. Files of the same names are replaced;
    /// anything else in it stays.</param>
    /// <remarks>An implementation that does not override this exports the generated script,
    /// whatever builder is asked for, and says so in <see cref="RenderExport.Builder"/>.</remarks>
    async Task<RenderExport> ExportAsync(
        SceneSpec spec,
        RenderSource source,
        string outputDir,
        ExportOptions options,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        string script = await ExportScriptAsync(spec, source, outputDir, progress, ct, options.Interactive).ConfigureAwait(false);
        return new RenderExport
        {
            ScriptPath = script,
            Directory = outputDir,
            Builder = SceneBuilder.Generator,
            FrameCount = SceneSpecValidator.FrameCount(spec),
            LeftOut = SceneBuilders.NotDrawnByTheGenerator(spec),
        };
    }
}
