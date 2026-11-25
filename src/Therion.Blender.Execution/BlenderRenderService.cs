// The concrete render service — chains the module's stages into the two modes behind
// IBlenderRenderService. The caller resolves the model (app-side source acquisition) and hands a
// RenderSource; this converts it, has one of the two scene builders describe the scene for
// Blender (the player with a scene plan, or the script generator with a script), locates and runs
// Blender, and collects the outputs. Everything heavy runs on the thread pool; progress is
// reported through the shared RenderProgress channel and cancellation is cooperative and tears
// down Blender.
//
// Which builder builds a scene is SceneBuilders.Choose's answer. The player renders a scene with
// texts over the picture in two stages, each a Blender of its own: the frames, then the output
// from them. Exporting a script is the script generator's, as before.

using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.CaveGeometry;

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

    public Task<RenderResult> RenderAsync(
        SceneSpec spec, RenderSource source, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        => RenderAsync(spec, source, new RenderOptions(), progress, ct);

    public async Task<RenderResult> RenderAsync(
        SceneSpec spec, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ComposeOnly && string.IsNullOrWhiteSpace(options.FramesDirectory))
            throw new ArgumentException("Writing the output from frames needs the folder the frames are in.", nameof(options));

        // Which builder, before anything else: a refusal costs nothing and needs no Blender.
        var choice = SceneBuilders.Choose(spec, options);
        if (choice.Refusal is not null)
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = RenderFailureKind.BuilderRefused,
                ErrorMessage = choice.Refusal,
                NotCarried = choice.NotCarried,
            };

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

        // The job folder is scratch (transport mesh, scene-meta, script or plan and player,
        // job.log); the outputs go to the caller's OutputDirectory when the spec sets one, else
        // into the job folder.
        string jobDir = CreateJobDirectory();
        string outputDir = string.IsNullOrWhiteSpace(spec.Output.OutputDirectory)
            ? jobDir
            : Path.GetFullPath(spec.Output.OutputDirectory);
        bool outputsElsewhere = !string.Equals(Path.GetFullPath(jobDir), outputDir, StringComparison.Ordinal);
        RenderResult result;
        try
        {
            Directory.CreateDirectory(outputDir);
            if (choice.Builder == SceneBuilder.Player)
            {
                result = await RenderWithPlayerAsync(spec, source, options, locate.Installation!, jobDir, outputDir, progress, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                var (scriptPath, frameCount, effectiveSpec) = await PrepareAsync(spec, source, jobDir, progress, ct, outputDirectory: outputDir)
                    .ConfigureAwait(false);
                var job = new RenderJob(scriptPath, jobDir, frameCount, effectiveSpec.Output);
                result = await _runner.RunAsync(locate.Installation!, job, progress, ct).ConfigureAwait(false);
            }

            var warnings = result.Warnings;
            if (BlenderLocator.UntestedVersionWarning(locate.Installation!.Version) is { } untested)
                warnings = [untested, .. warnings];
            result = Copy(result, result.JobLogPath, warnings, choice);
        }
        finally
        {
            DeleteScratch(jobDir);
        }

        // With the outputs elsewhere, a finished job's folder holds nothing but its log: drop the folder
        // instead of leaving one behind per render. A failed or cancelled job keeps its log to look at.
        if (outputsElsewhere && result.Succeeded && TryDeleteDirectory(jobDir))
            return Copy(result, jobLogPath: null, result.Warnings, choice);
        return result;
    }

    /// <summary>
    /// The player's path: convert, lower the spec to a scene plan, write the plan and the player
    /// into the job folder, and play it. A plan with texts, and any render whose frames are asked
    /// for, takes two stages: the frames into the frames folder, then the output from them.
    /// </summary>
    private async Task<RenderResult> RenderWithPlayerAsync(
        SceneSpec spec, RenderSource source, RenderOptions options, BlenderInstallation blender, string jobDir, string outputDir,
        IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.ConvertingGeometry, "Converting model"));
        var (conversion, placed) = await ConvertAsync(spec, source, jobDir, outputDir, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Planning the scene"));
        var plan = ScenePlanBuilder.FromSpec(placed, CameraFraming.FromGeometry(conversion.Geometry), conversion.Meta);
        if (options.Draft) plan = ScenePlanBuilder.Draft(plan);
        // What a generated script would print about the scene, a plan cannot: say it here.
        var notices = ScenePlanBuilder.Notices(placed, conversion.Meta, CameraFraming.FromGeometry(conversion.Geometry));
        // The photos of the annotations go beside the plan, from wherever the spec has them.
        var playerJob = PlayerWriter.WriteJob(plan, jobDir, ScenePlanBuilder.AnnotationFiles(placed));
        var output = PlanOutputs.SpecOf(plan, outputDir);

        bool twoStages = plan.HasTexts || options.KeepFrames || options.ComposeOnly;
        RenderResult result;
        string? framesDir = null;
        if (!twoStages)
        {
            result = await _runner.RunAsync(
                blender, new RenderJob(playerJob.ScriptPath, jobDir, PlanOutputs.StageFrameCount(plan), output), progress, ct).ConfigureAwait(false);
        }
        else
        {
            framesDir = Path.GetFullPath(options.FramesDirectory
                ?? (options.KeepFrames
                    ? Path.Combine(outputDir, plan.Output.BaseName + "-frames")
                    : Path.Combine(jobDir, PlanOutputs.FramesFolderName)));
            RenderResult? frames = null;
            if (!options.ComposeOnly)
            {
                // The first stage: the scene as frames. The preflight counts them and the output
                // that is made from them.
                long outputBytes = DiskPreflight.EstimateBytes(plan.OutputFrameCount, output.Width, output.Height, output.Kind);
                frames = await _runner.RunAsync(
                    blender,
                    new RenderJob(playerJob.ScriptPath, jobDir, PlanOutputs.StageFrameCount(plan), PlanOutputs.FramesOf(plan, framesDir))
                    {
                        ScriptArguments = ["--stage", "frames", "--frames", framesDir],
                        ExtraBytes = outputBytes,
                        LogFileName = FramesLogName,
                    },
                    progress, ct).ConfigureAwait(false);
            }

            if (frames is { Succeeded: false })
            {
                result = frames;
            }
            else
            {
                // The second stage: the output from the frames, with the texts over them.
                string[] arguments = ["--stage", "compose", "--frames", framesDir];
                var composed = await _runner.RunAsync(
                    blender,
                    new RenderJob(playerJob.ScriptPath, jobDir, plan.OutputFrameCount, output)
                    {
                        ScriptArguments = options.KeepFrames || options.ComposeOnly ? [.. arguments, "--keep-frames"] : arguments,
                    },
                    progress, ct).ConfigureAwait(false);
                result = frames is null ? composed : Merge(frames, composed);
            }
            // Frames that are still there: kept, or left by a render that can go on from them.
            if (!Directory.Exists(framesDir) || IsInside(framesDir, jobDir)) framesDir = null;
        }

        var outputs = result.OutputPaths;
        if (result.Succeeded && plan.Output.Kind != PlanOutputKind.Still)
            outputs = outputs.AddRange(WriteSubtitles(spec, outputDir, plan.Output.BaseName));
        return new RenderResult
        {
            Succeeded = result.Succeeded,
            FailureKind = result.FailureKind,
            ErrorMessage = result.ErrorMessage,
            StartFailedPath = result.StartFailedPath,
            OutputPaths = outputs,
            Device = result.Device,
            FrameCount = result.FrameCount,
            JobLogPath = result.JobLogPath,
            Duration = result.Duration,
            Warnings = [.. notices, .. result.Warnings],
            FramesDirectory = framesDir,
        };
    }

    /// <summary>The two stages of one render as one result: the second's verdict and outputs,
    /// the first's device, the time and the warnings of both.</summary>
    private static RenderResult Merge(RenderResult frames, RenderResult composed) => new()
    {
        Succeeded = composed.Succeeded,
        FailureKind = composed.FailureKind,
        ErrorMessage = composed.ErrorMessage,
        StartFailedPath = composed.StartFailedPath,
        OutputPaths = composed.OutputPaths,
        Device = frames.Device ?? composed.Device,
        FrameCount = composed.FrameCount,
        JobLogPath = composed.JobLogPath,
        Duration = frames.Duration + composed.Duration,
        Warnings = [.. frames.Warnings, .. composed.Warnings],
    };

    /// <summary>Writes the spec's captions as subtitles beside the movie, as
    /// <c>&lt;base name&gt;.srt</c> and <c>.vtt</c>, and returns the files written. A caption
    /// file that cannot be written is not worth failing a finished render for.</summary>
    private static IEnumerable<string> WriteSubtitles(SceneSpec spec, string outputDir, string baseName)
    {
        var captions = spec.Labels.Overlays.Captions;
        if (Subtitles.OneAtATime(captions).Count == 0) return [];
        var written = new List<string>(2);
        foreach (var (extension, text) in new[] { (Subtitles.SrtExtension, Subtitles.Srt(captions)), (Subtitles.VttExtension, Subtitles.Vtt(captions)) })
        {
            string path = Path.Combine(outputDir, baseName + extension);
            try
            {
                File.WriteAllText(path, text, Utf8NoBom);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return written;
    }

    private static bool IsInside(string path, string folder)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deletes the job folders under the job root that are older than <paramref name="olderThan"/> and
    /// hold nothing but render scratch and logs (a failed or cancelled run, or one whose
    /// outputs went to <see cref="OutputSpec.OutputDirectory"/>). A folder holding anything else, such
    /// as the outputs of a render without an output directory, is kept. Best-effort; returns the number
    /// of folders deleted.
    /// </summary>
    public int PruneJobFolders(TimeSpan olderThan)
    {
        if (!Directory.Exists(_jobRoot)) return 0;
        var cutoff = DateTime.UtcNow - olderThan;
        int deleted = 0;
        IEnumerable<string> folders;
        try { folders = Directory.EnumerateDirectories(_jobRoot, "job-*").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        foreach (var dir in folders)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) > cutoff) continue;
                // A render that was killed leaves the player and its frames behind as well.
                if (!Directory.EnumerateDirectories(dir).All(d => ScratchFolderNames.Contains(Path.GetFileName(d)))) continue;
                if (!Directory.EnumerateFiles(dir).All(f => DisposableFileNames.Contains(Path.GetFileName(f)))) continue;
                if (TryDeleteDirectory(dir)) deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return deleted;
    }

    public async Task<string> ExportScriptAsync(
        SceneSpec spec, RenderSource source, string outputDir,
        IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        Directory.CreateDirectory(outputDir);
        var purpose = interactive ? ScriptPurpose.Interactive : ScriptPurpose.Render;
        var (scriptPath, _, _) = await PrepareAsync(spec, source, outputDir, progress, ct, purpose).ConfigureAwait(false);
        progress?.Report(new RenderProgress(RenderPhase.Done, "Script exported", 1.0));
        return scriptPath;
    }

    /// <summary>Convert into <paramref name="assetDir"/>, build camera framing, generate the
    /// script, and write <c>render.py</c> there. Returns its path, the frame count, and the
    /// spec with its source pointed at the produced assets and its output at
    /// <paramref name="outputDirectory"/> (the asset folder when null).</summary>
    private async Task<(string ScriptPath, int FrameCount, SceneSpec Spec)> PrepareAsync(
        SceneSpec spec, RenderSource source, string assetDir, IProgress<RenderProgress>? progress, CancellationToken ct,
        ScriptPurpose purpose = ScriptPurpose.Render, string? outputDirectory = null)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.ConvertingGeometry, "Converting model"));
        var (conversion, effectiveSpec) = await ConvertAsync(spec, source, assetDir, outputDirectory ?? assetDir, ct).ConfigureAwait(false);
        int frameCount = SceneSpecValidator.FrameCount(effectiveSpec);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Generating script"));

        var framing = CameraFraming.FromGeometry(conversion.Geometry);
        var assets = BuildAssets(effectiveSpec, conversion);
        string script = await Task.Run(
            () => ScriptGenerator.Generate(effectiveSpec, assets, framing, conversion.Meta, purpose), ct).ConfigureAwait(false);

        string scriptPath = Path.Combine(assetDir, "render.py");
        await File.WriteAllTextAsync(scriptPath, script, Utf8NoBom, ct).ConfigureAwait(false);
        return (scriptPath, frameCount, effectiveSpec);
    }

    /// <summary>Converts the model into <paramref name="assetDir"/> (with its terrain when the
    /// spec draws one) and returns the spec with its source pointed at what was written and its
    /// output at <paramref name="outputDirectory"/>.</summary>
    private async Task<(ConversionResult Conversion, SceneSpec Spec)> ConvertAsync(
        SceneSpec spec, RenderSource source, string assetDir, string outputDirectory, CancellationToken ct)
    {
        var options = new ConversionOptions
        {
            OutputDirectory = assetDir,
            // The reveal values of the spec's reveal travel on the vertices of the mesh.
            Geometry = RevealPlanner.GeometryFor(spec, _geometry, source.Model.Path),
            Leads = source.Leads,
            IncludeSurface = spec.Surface.Mode != SurfaceMode.Off,
        };
        var conversion = await Task.Run(
            () => CaveConversionPipeline.ConvertResolvedFull(source.Model, options), ct).ConfigureAwait(false);
        return (conversion, spec with
        {
            Source = spec.Source with
            {
                PlyPath = conversion.Manifest.ModelPath,
                SceneMetaPath = conversion.Manifest.SceneMetaPath,
                SurfacePlyPath = conversion.Manifest.SurfacePath,
                SurfaceImagePath = conversion.Manifest.SurfaceImagePath,
            },
            Output = spec.Output with { OutputDirectory = outputDirectory },
        });
    }

    /// <summary>Self-contained assets (D-14): embed the scene-meta JSON and, when requested,
    /// the PLY with the terrain and its image. Null for the default sidecar mode.</summary>
    private static ScriptAssets? BuildAssets(SceneSpec spec, ConversionResult conversion)
    {
        if (!spec.Source.SelfContained) return null;
        string metaJson = File.ReadAllText(conversion.Manifest.SceneMetaPath);
        if (!spec.Source.EmbedMesh) return new ScriptAssets { SceneMetaJson = metaJson };
        bool terrain = spec.Surface.Mode != SurfaceMode.Off;
        return new ScriptAssets
        {
            SceneMetaJson = metaJson,
            PlyBytes = File.ReadAllBytes(conversion.Manifest.ModelPath),
            SurfacePlyBytes = terrain && conversion.Manifest.SurfacePath is { } surface ? File.ReadAllBytes(surface) : null,
            SurfaceImageBytes = terrain && conversion.Manifest.SurfaceImagePath is { } image ? File.ReadAllBytes(image) : null,
        };
    }

    /// <summary>Removes the per-render scratch (transport mesh, scene-meta, script or plan and
    /// player, the frames of a render in two stages that were kept in the job folder) once the
    /// run is over; the logs and any outputs written into the job folder stay. Best-effort — a
    /// file still locked or already gone is left alone.</summary>
    private static void DeleteScratch(string jobDir)
    {
        foreach (var name in ScratchFileNames)
        {
            try { File.Delete(Path.Combine(jobDir, name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var name in ScratchFolderNames)
        {
            string folder = Path.Combine(jobDir, name);
            if (Directory.Exists(folder)) TryDeleteDirectory(folder);
        }
    }

    /// <summary>The log of the first of two stages; the second writes <c>job.log</c>.</summary>
    private const string FramesLogName = "job-frames.log";

    // ConversionOptions' default file names (the terrain and its image too), the script
    // PrepareAsync writes, and the plan the player plays.
    private static readonly string[] ScratchFileNames =
        ["model.ply", "scene-meta.json", "render.py", "surface.ply", "surface.png", "surface.jpg", ScenePlanSerializer.FileName];

    // The player's package, the frames of a render in two stages whose frames nobody asked for,
    // and the copies of the annotations' photos.
    private static readonly string[] ScratchFolderNames = [PlayerFiles.PackageName, PlanOutputs.FramesFolderName, AnnotationPlanner.PhotosFolder];

    // What a job folder may hold and still be deleted by PruneJobFolders: scratch and the runner's logs.
    private static readonly HashSet<string> DisposableFileNames = new([.. ScratchFileNames, "job.log", FramesLogName], StringComparer.Ordinal);

    private static bool TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static RenderResult Copy(RenderResult r, string? jobLogPath, IReadOnlyList<string> warnings, SceneBuilderChoice choice) => new()
    {
        Succeeded = r.Succeeded,
        FailureKind = r.FailureKind,
        ErrorMessage = r.ErrorMessage,
        StartFailedPath = r.StartFailedPath,
        OutputPaths = r.OutputPaths,
        Device = r.Device,
        FrameCount = r.FrameCount,
        JobLogPath = jobLogPath,
        Duration = r.Duration,
        Warnings = warnings,
        Builder = choice.Builder,
        NotCarried = choice.NotCarried,
        LeftOut = choice.LeftOut,
        FramesDirectory = r.FramesDirectory,
    };

    private string CreateJobDirectory()
    {
        string jobDir = Path.Combine(_jobRoot, "job-" + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(jobDir);
        return jobDir;
    }
}
