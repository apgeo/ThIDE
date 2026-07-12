// The concrete render service (G3 capstone) — chains the module's stages into the two FR-10
// modes behind IBlenderRenderService. The caller resolves the model (app-side source
// acquisition, BA-B12) and hands a RenderSource; this converts (BA-B3/B4) → generates the
// script (BA-B5–B9) → for RenderAsync, locates + runs Blender (BA-B10) and collects outputs
// (BA-B11). Everything heavy runs on the thread pool; progress is reported through the shared
// RenderProgress channel and cancellation is cooperative + tears down Blender.

using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;
using Therion.Blender.Sources;

namespace Therion.Blender.Execution;

/// <summary>Orchestrates convert → generate → render for the BLEND module.</summary>
public sealed class BlenderRenderService : IBlenderRenderService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly BlenderLocator _locator;
    private readonly BlenderRunner _runner;
    private readonly string _jobRoot;
    private readonly GeometryOptions _geometry;
    private readonly Func<string?>? _blenderOverride;

    /// <param name="locator">Finds the Blender executable.</param>
    /// <param name="runner">Runs the generated script headless.</param>
    /// <param name="jobRootDirectory">Root under which per-render job folders are created.</param>
    /// <param name="geometry">Conversion geometry options (recenter, tubes, tint).</param>
    /// <param name="blenderOverride">User's Blender path override (Preferences) — read live on each
    /// render, so a path set after startup takes effect without a restart.</param>
    public BlenderRenderService(
        BlenderLocator locator,
        BlenderRunner runner,
        string jobRootDirectory,
        GeometryOptions? geometry = null,
        Func<string?>? blenderOverride = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobRootDirectory);
        _locator = locator;
        _runner = runner;
        _jobRoot = jobRootDirectory;
        _geometry = geometry ?? new GeometryOptions();
        _blenderOverride = blenderOverride;
    }

    public async Task<RenderResult> RenderAsync(
        SceneSpec spec, RenderSource source, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);

        // Locate Blender first so a missing/old install fails fast, before any conversion work.
        var locate = _locator.Locate(_blenderOverride?.Invoke());
        if (!locate.IsUsable)
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = locate.Status == BlenderLocateStatus.TooOld
                    ? RenderFailureKind.BlenderTooOld
                    : RenderFailureKind.BlenderNotFound,
                ErrorMessage = locate.Detail,
            };

        string jobDir = CreateJobDirectory();
        var (scriptPath, frameCount, effectiveSpec) = await PrepareAsync(spec, source, jobDir, progress, ct)
            .ConfigureAwait(false);

        var job = new RenderJob(scriptPath, jobDir, frameCount, effectiveSpec.Output);
        return await _runner.RunAsync(locate.Installation!, job, progress, ct).ConfigureAwait(false);
    }

    public async Task<string> ExportScriptAsync(
        SceneSpec spec, RenderSource source, string outputDir,
        IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        Directory.CreateDirectory(outputDir);
        var (scriptPath, _, _) = await PrepareAsync(spec, source, outputDir, progress, ct).ConfigureAwait(false);
        progress?.Report(new RenderProgress(RenderPhase.Done, "Script exported", 1.0));
        return scriptPath;
    }

    /// <summary>Convert into <paramref name="assetDir"/>, build camera framing, generate the
    /// script, and write <c>render.py</c> there. Returns its path, the frame count, and the
    /// spec with its source/output pointed at the produced assets.</summary>
    private async Task<(string ScriptPath, int FrameCount, SceneSpec Spec)> PrepareAsync(
        SceneSpec spec, RenderSource source, string assetDir, IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.ConvertingGeometry, "Converting model"));

        var options = new ConversionOptions { OutputDirectory = assetDir, Geometry = _geometry, Leads = source.Leads };
        var conversion = await Task.Run(
            () => CaveConversionPipeline.ConvertResolvedFull(source.Model, options), ct).ConfigureAwait(false);

        var effectiveSpec = spec with
        {
            Source = spec.Source with
            {
                PlyPath = conversion.Manifest.ModelPath,
                SceneMetaPath = conversion.Manifest.SceneMetaPath,
            },
            Output = spec.Output with { OutputDirectory = assetDir },
        };
        int frameCount = SceneSpecValidator.FrameCount(effectiveSpec);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Generating script"));

        var framing = CameraFraming.FromGeometry(conversion.Geometry);
        var assets = BuildAssets(effectiveSpec, conversion);
        string script = await Task.Run(
            () => ScriptGenerator.Generate(effectiveSpec, assets, framing, conversion.Meta), ct).ConfigureAwait(false);

        string scriptPath = Path.Combine(assetDir, "render.py");
        await File.WriteAllTextAsync(scriptPath, script, Utf8NoBom, ct).ConfigureAwait(false);
        return (scriptPath, frameCount, effectiveSpec);
    }

    /// <summary>Self-contained assets (D-14): embed the scene-meta JSON and, when requested,
    /// the PLY. Null for the default sidecar mode.</summary>
    private static ScriptAssets? BuildAssets(SceneSpec spec, ConversionResult conversion)
    {
        if (!spec.Source.SelfContained) return null;
        string metaJson = File.ReadAllText(conversion.Manifest.SceneMetaPath);
        byte[]? plyBytes = spec.Source.EmbedMesh ? File.ReadAllBytes(conversion.Manifest.ModelPath) : null;
        return new ScriptAssets { SceneMetaJson = metaJson, PlyBytes = plyBytes };
    }

    private string CreateJobDirectory()
    {
        string jobDir = Path.Combine(_jobRoot, "job-" + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(jobDir);
        return jobDir;
    }
}
