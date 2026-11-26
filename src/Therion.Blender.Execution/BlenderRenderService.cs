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
// from them. An export writes the scene of the same builder for someone to run themselves: the
// player's folder as a render bundle holds it, or one script; the script generator's script when
// that builder is asked for.

using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.CaveGeometry;

namespace Therion.Blender.Execution;

/// <summary>Orchestrates convert → generate → render for the BLEND module.</summary>
public sealed class BlenderRenderService : IBlenderRenderService, IRenderEstimator
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
        if (options.FrameRange is not null && (string.IsNullOrWhiteSpace(options.FramesDirectory) || options.ComposeOnly || options.KeepFrames))
            throw new ArgumentException(
                "A range of frames is rendered into a frames folder of its own, and the output is written from it after (compose only).",
                nameof(options));

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

    public async Task<RenderResult> RenderStoryboardAsync(
        Storyboard storyboard, RenderSource source, RenderOptions options, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ComposeOnly && string.IsNullOrWhiteSpace(options.FramesDirectory))
            throw new ArgumentException("Writing the output from frames needs the folder the frames are in.", nameof(options));
        if (options.FrameRange is not null)
            throw new ArgumentException("A range of frames is of one scene: render one shot of the film alone.", nameof(options));
        var errors = StoryboardValidator.Validate(storyboard);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The storyboard is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")), nameof(storyboard));
        if (options.Builder == SceneBuilder.Generator)
            return new RenderResult
            {
                Succeeded = false,
                FailureKind = RenderFailureKind.BuilderRefused,
                ErrorMessage = "A storyboard is rendered by the player: the script generator renders one scene.",
            };

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
        string outputDir = string.IsNullOrWhiteSpace(storyboard.Output.OutputDirectory)
            ? jobDir
            : Path.GetFullPath(storyboard.Output.OutputDirectory);
        bool outputsElsewhere = !string.Equals(Path.GetFullPath(jobDir), outputDir, StringComparison.Ordinal);
        var choice = new SceneBuilderChoice(SceneBuilder.Player, [], []);
        RenderResult result;
        try
        {
            Directory.CreateDirectory(outputDir);
            result = await RenderFilmAsync(storyboard, source, options, locate.Installation!, jobDir, outputDir, progress, ct).ConfigureAwait(false);
            var warnings = result.Warnings;
            if (BlenderLocator.UntestedVersionWarning(locate.Installation!.Version) is { } untested)
                warnings = [untested, .. warnings];
            result = Copy(result, result.JobLogPath, warnings, choice);
        }
        finally
        {
            DeleteScratch(jobDir);
        }
        if (outputsElsewhere && result.Succeeded && TryDeleteDirectory(jobDir))
            return Copy(result, jobLogPath: null, result.Warnings, choice);
        return result;
    }

    /// <summary>
    /// A film: convert, lower the storyboard to the plans of its segments and of the movie, write
    /// them and the player into the job folder, render every segment's frames into a folder of
    /// its own inside the frames folder, then write the film from them. A segment whose frames
    /// are there is passed over by the player itself.
    /// </summary>
    private async Task<RenderResult> RenderFilmAsync(
        Storyboard storyboard, RenderSource source, RenderOptions options, BlenderInstallation blender, string jobDir, string outputDir,
        IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.ConvertingGeometry, "Converting model"));
        var needs = StoryboardPlanner.ConversionSpec(storyboard);
        var (conversion, placed) = await ConvertAsync(needs, source, jobDir, outputDir, ct, options.Parts).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Planning the film"));
        // A composite's symbolic routes become this cave's stations, from the geometry just read.
        var expanded = StoryboardExpander.Expand(storyboard, conversion.Geometry, source.Leads);
        var film = StoryboardPlanner.Lower(
            expanded.Storyboard with { Output = storyboard.Output with { OutputDirectory = outputDir } },
            placed.Source, CameraFraming.FromGeometry(conversion.Geometry), conversion.Meta);
        if (expanded.Notices.Count > 0) film = film with { Notices = [.. expanded.Notices, .. film.Notices] };
        if (options.Draft) film = film.Draft();
        var job = PlayerWriter.WriteJob(film, jobDir, film.Files);
        var movie = film.Movie;
        var output = PlanOutputs.SpecOf(film, outputDir);
        var estimate = Estimate(film.Segments, movie.OutputFrameCount, jobDir);
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Estimate: " + estimate.Describe()));
        var paced = progress is null ? null : new Paced(progress, movie.Segments.Sum(s => s.FrameCount));

        string framesDir = Path.GetFullPath(options.FramesDirectory
            ?? (options.KeepFrames
                ? Path.Combine(outputDir, movie.Output.BaseName + "-frames")
                : Path.Combine(jobDir, PlanOutputs.FramesFolderName)));
        var stages = new List<RenderResult>();
        if (!options.ComposeOnly)
        {
            int total = movie.Segments.Sum(s => s.FrameCount), before = 0;
            for (int i = 0; i < movie.Segments.Count; i++)
            {
                var segment = movie.Segments[i];
                string frames = Path.Combine(framesDir, segment.Frames.Replace('/', Path.DirectorySeparatorChar));
                string shot = $"Shot {i + 1} of {movie.Segments.Count}" + (segment.Name is { } name ? $" ({name})" : "");
                int done = before, index = i;
                paced?.Starts(done);
                // Each shot's progress as a part of the whole film's.
                var part = paced is null ? null : new SyncRelay(tick => paced.Report(tick with
                {
                    Message = $"{shot}: {tick.Message}",
                    Fraction = tick is { Frame: { } frame, Phase: RenderPhase.Rendering } ? Math.Clamp((done + frame) / (double)total, 0, 1) : null,
                    Segment = index + 1,
                    SegmentCount = movie.Segments.Count,
                }));
                var rendered = await _runner.RunAsync(
                    blender,
                    new RenderJob(job.ScriptPath, jobDir, segment.FrameCount, PlanOutputs.FramesOf(film.Segments[i], frames))
                    {
                        ScriptArguments = ["--plan", Path.Combine(jobDir, segment.Plan), "--stage", "frames", "--frames", frames],
                        // The first one's preflight counts the film that is made from the frames.
                        ExtraBytes = i == 0 ? DiskPreflight.EstimateBytes(movie.OutputFrameCount, output.Width, output.Height, output.Kind) : 0,
                        LogFileName = SegmentLogName(segment.Id),
                    },
                    part, ct).ConfigureAwait(false);
                stages.Add(rendered);
                if (!rendered.Succeeded) break;
                before += segment.FrameCount;
            }
        }

        RenderResult result;
        if (stages.Count > 0 && !stages[^1].Succeeded)
        {
            result = stages[^1];
        }
        else
        {
            // The second stage plays the movie's plan, which is the one beside the script.
            string[] arguments = ["--stage", "compose", "--frames", framesDir];
            var composed = await _runner.RunAsync(
                blender,
                new RenderJob(job.ScriptPath, jobDir, movie.OutputFrameCount, output)
                {
                    ScriptArguments = options.KeepFrames || options.ComposeOnly ? [.. arguments, "--keep-frames"] : arguments,
                },
                progress, ct).ConfigureAwait(false);
            result = composed;
        }
        string? kept = Directory.Exists(framesDir) && !IsInside(framesDir, jobDir) ? framesDir : null;

        var outputs = result.OutputPaths;
        if (result.Succeeded) outputs = outputs.AddRange(WriteSubtitles(film.Captions, outputDir, movie.Output.BaseName));
        return new RenderResult
        {
            Succeeded = result.Succeeded,
            FailureKind = result.FailureKind,
            ErrorMessage = stages.Count > 0 && !stages[^1].Succeeded
                ? $"Shot {stages.Count} of {movie.Segments.Count}: {result.ErrorMessage}"
                : result.ErrorMessage,
            StartFailedPath = result.StartFailedPath,
            OutputPaths = outputs,
            Device = stages.Select(s => s.Device).FirstOrDefault(d => d is not null) ?? result.Device,
            FrameCount = result.FrameCount,
            JobLogPath = result.JobLogPath,
            Duration = stages.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration) + (ReferenceEquals(result, stages.LastOrDefault()) ? TimeSpan.Zero : result.Duration),
            Warnings = [.. film.Notices, .. TriangleWarning(estimate), .. stages.Where(s => !ReferenceEquals(s, result)).SelectMany(s => s.Warnings).Distinct(StringComparer.Ordinal), .. result.Warnings],
            FramesDirectory = kept,
        };
    }

    /// <summary>
    /// What a render of <paramref name="spec"/> will cost (<see cref="RenderEstimate"/>), without
    /// rendering: the model is converted and the scene planned in a job folder that is removed
    /// after. Needs no Blender.
    /// </summary>
    /// <exception cref="ArgumentException">The spec is invalid, or cannot be planned for the model.</exception>
    public async Task<RenderEstimate> EstimateAsync(SceneSpec spec, RenderSource source, RenderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        string jobDir = CreateJobDirectory();
        try
        {
            var (plan, _, _, _) = await WritePlayerJobAsync(spec, source, options.Draft, jobDir, jobDir, null, ct, options.Parts).ConfigureAwait(false);
            return Estimate([plan], plan.OutputFrameCount, jobDir);
        }
        finally
        {
            TryDeleteDirectory(jobDir);
        }
    }

    /// <summary>What a render of the film <paramref name="storyboard"/> will cost, as
    /// <see cref="EstimateAsync"/> says of a scene.</summary>
    /// <exception cref="ArgumentException">The storyboard is invalid, or cannot be planned for the model.</exception>
    public async Task<RenderEstimate> EstimateStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var errors = StoryboardValidator.Validate(storyboard);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The storyboard is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")), nameof(storyboard));
        string jobDir = CreateJobDirectory();
        try
        {
            var (conversion, placed) = await ConvertAsync(StoryboardPlanner.ConversionSpec(storyboard), source, jobDir, jobDir, ct, options.Parts)
                .ConfigureAwait(false);
            var expanded = StoryboardExpander.Expand(storyboard, conversion.Geometry, source.Leads).Storyboard;
            var film = StoryboardPlanner.Lower(expanded with { Output = storyboard.Output with { OutputDirectory = jobDir } },
                placed.Source, CameraFraming.FromGeometry(conversion.Geometry), conversion.Meta);
            if (options.Draft) film = film.Draft();
            return Estimate(film.Segments, film.Movie.OutputFrameCount, jobDir);
        }
        finally
        {
            TryDeleteDirectory(jobDir);
        }
    }

    /// <summary>Passes progress on as it comes, on the thread it comes on.</summary>
    private sealed class SyncRelay(Action<RenderProgress> report) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => report(value);
    }

    /// <summary>Passes progress on with the pace of the frames (<see cref="FrameClock"/>) on
    /// every frame that is rendered: seconds per frame and the time left, of
    /// <paramref name="total"/> frames.</summary>
    internal sealed class Paced(IProgress<RenderProgress> inner, int total) : IProgress<RenderProgress>
    {
        private readonly FrameClock _clock = new();
        private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
        private int _before;

        /// <summary>The frames of the stages before this one: a segment's frame 1 is frame
        /// <paramref name="before"/> + 1 of the whole.</summary>
        public void Starts(int before) => _before = before;

        public void Report(RenderProgress value)
        {
            if (value is { Phase: RenderPhase.Rendering, Frame: { } frame })
            {
                var (perFrame, left) = _clock.Tick(_before + frame, total, _watch.Elapsed);
                value = value with { SecondsPerFrame = perFrame, SecondsLeft = left };
            }
            inner.Report(value);
        }
    }

    /// <summary>The estimate of <paramref name="scenes"/>, whose meshes lie in <paramref name="folder"/>.</summary>
    private static RenderEstimate Estimate(IReadOnlyList<ScenePlan> scenes, int outputFrames, string folder)
        => RenderEstimate.Of(scenes, outputFrames, path => RenderEstimate.PlyFaces(Path.Combine(folder, path)));

    /// <summary>The warning a scene with very many triangles gets.</summary>
    private static IEnumerable<string> TriangleWarning(RenderEstimate estimate)
    {
        if (estimate.TooManyTriangles)
            yield return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"The scene has {estimate.Triangles / 1e6:0.#} million triangles (more than {RenderEstimate.WarnTriangles / 1e6:0} million): "
                + $"Blender may need about {estimate.MemoryBytes / 1e9:0.#} GB of memory, and a GPU may not hold it.");
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
        var (plan, playerJob, notices, placed) = await WritePlayerJobAsync(spec, source, options.Draft, jobDir, outputDir, progress, ct, options.Parts)
            .ConfigureAwait(false);
        var output = PlanOutputs.SpecOf(plan, outputDir);
        var estimate = Estimate([plan], plan.OutputFrameCount, jobDir);
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Estimate: " + estimate.Describe()));
        notices = [.. notices, .. TriangleWarning(estimate)];
        var paced = progress is null ? null : new Paced(progress, options.FrameRange?.Last ?? PlanOutputs.StageFrameCount(plan));
        if (options.FrameRange is { } range)
            return await RenderRangeAsync(plan, playerJob, range, options.FramesDirectory!, blender, jobDir, notices, paced, ct).ConfigureAwait(false);

        bool twoStages = plan.HasTexts || options.KeepFrames || options.ComposeOnly;
        RenderResult result;
        string? framesDir = null;
        if (!twoStages)
        {
            result = await _runner.RunAsync(
                blender, new RenderJob(playerJob.ScriptPath, jobDir, PlanOutputs.StageFrameCount(plan), output), paced, ct).ConfigureAwait(false);
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
                    paced, ct).ConfigureAwait(false);
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
            outputs = outputs.AddRange(WriteSubtitles(placed.Labels.Overlays.Captions, outputDir, plan.Output.BaseName));
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

    /// <summary>The first stage of a scene for a range of its frames alone, into
    /// <paramref name="framesDirectory"/>; no output is written (<see cref="RenderOptions.FrameRange"/>).</summary>
    private async Task<RenderResult> RenderRangeAsync(
        ScenePlan plan, PlayerJob playerJob, FrameRange range, string framesDirectory, BlenderInstallation blender, string jobDir,
        IReadOnlyList<string> notices, IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        int count = PlanOutputs.StageFrameCount(plan);
        if (range.Last > count)
            throw new ArgumentException($"The scene has {count} frame(s); the range {range} is not a range of them.", "options");
        string framesDir = Path.GetFullPath(framesDirectory);
        var frames = await _runner.RunAsync(
            blender,
            new RenderJob(playerJob.ScriptPath, jobDir, range.Count, PlanOutputs.FramesOf(plan, framesDir))
            {
                ScriptArguments = ["--stage", "frames", "--frames", framesDir, "--frame-range", range.ToString()],
                LogFileName = FramesLogName,
            },
            progress, ct).ConfigureAwait(false);
        return new RenderResult
        {
            Succeeded = frames.Succeeded,
            FailureKind = frames.FailureKind,
            ErrorMessage = frames.ErrorMessage,
            StartFailedPath = frames.StartFailedPath,
            OutputPaths = frames.OutputPaths,
            Device = frames.Device,
            FrameCount = frames.FrameCount,
            JobLogPath = frames.JobLogPath,
            Duration = frames.Duration,
            Warnings = [.. notices, .. frames.Warnings],
            FramesDirectory = framesDir,
        };
    }

    /// <summary>
    /// Converts the model into <paramref name="jobDir"/>, lowers the spec to a scene plan and
    /// writes the plan and the player there: the job folder a render plays, and an export to
    /// look at opens in Blender's window. Returns the plan, the job, what the render will show
    /// less of than the spec asks for, and the spec as it was planned.
    /// </summary>
    private async Task<(ScenePlan Plan, PlayerJob Job, IReadOnlyList<string> Notices, SceneSpec Placed)> WritePlayerJobAsync(
        SceneSpec spec, RenderSource source, bool draft, string jobDir, string outputDir,
        IProgress<RenderProgress>? progress, CancellationToken ct, WallPartsMode parts = WallPartsMode.Off)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.ConvertingGeometry, "Converting model"));
        var (conversion, placed) = await ConvertAsync(spec, source, jobDir, outputDir, ct, parts).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Planning the scene"));
        var plan = ScenePlanBuilder.FromSpec(placed, CameraFraming.FromGeometry(conversion.Geometry), conversion.Meta);
        if (draft) plan = ScenePlanBuilder.Draft(plan);
        // What a generated script would print about the scene, a plan cannot: say it here.
        var notices = ScenePlanBuilder.Notices(placed, conversion.Meta, CameraFraming.FromGeometry(conversion.Geometry));
        // The photos of the annotations and the image of an HDRI light rig go beside the plan,
        // from wherever the spec has them.
        var playerJob = PlayerWriter.WriteJob(plan, jobDir, ScenePlanBuilder.Files(placed));
        return (plan, playerJob, notices, placed);
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

    /// <summary>Writes the captions as subtitles beside the movie, as
    /// <c>&lt;base name&gt;.srt</c> and <c>.vtt</c>, and returns the files written. A caption
    /// file that cannot be written is not worth failing a finished render for.</summary>
    private static IEnumerable<string> WriteSubtitles(IReadOnlyList<CaptionSpec> captions, string outputDir, string baseName)
    {
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
                if (!Directory.EnumerateFiles(dir).All(f => IsDisposable(Path.GetFileName(f)))) continue;
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

    /// <summary>The name of the one script of an export in one file, and of a generated script.</summary>
    public const string ExportScriptName = "render.py";

    public async Task<RenderExport> ExportAsync(
        SceneSpec spec, RenderSource source, string outputDir, ExportOptions options,
        IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        outputDir = Path.GetFullPath(outputDir);

        if (options.Builder == SceneBuilder.Generator)
        {
            // The script generator's export, as it was. One file is its self-contained script
            // with the meshes inside.
            var generated = options.SingleFile && !options.Interactive
                ? spec with { Source = spec.Source with { SelfContained = true, EmbedMesh = true } }
                : spec;
            string script = await ExportScriptAsync(generated, source, outputDir, progress, ct, options.Interactive).ConfigureAwait(false);
            return new RenderExport
            {
                ScriptPath = script,
                Directory = outputDir,
                Builder = SceneBuilder.Generator,
                SingleFile = generated.Source is { SelfContained: true, EmbedMesh: true },
                FrameCount = SceneSpecValidator.FrameCount(spec),
                LeftOut = SceneBuilders.NotDrawnByTheGenerator(spec),
            };
        }

        // The player refuses a spec it does not carry, as it does for a render.
        if (SceneBuilders.Choose(spec, new RenderOptions { Builder = SceneBuilder.Player }).Refusal is { } refusal)
            throw new ArgumentException(refusal, nameof(spec));

        Directory.CreateDirectory(outputDir);
        RenderExport export;
        if (options.Interactive)
        {
            // The job folder of a render, for Blender's window: what a render of the scene from
            // there writes goes where the spec says, or stays in the folder.
            string renders = string.IsNullOrWhiteSpace(spec.Output.OutputDirectory)
                ? Path.Combine(outputDir, RenderBundleWriter.OutputDirectory)
                : Path.GetFullPath(spec.Output.OutputDirectory);
            var (plan, job, notices, _) = await WritePlayerJobAsync(spec, source, draft: false, outputDir, renders, progress, ct)
                .ConfigureAwait(false);
            export = new RenderExport
            {
                ScriptPath = job.ScriptPath,
                Directory = outputDir,
                ScriptArguments = ["--build-only"],
                Files = FilesUnder(outputDir),
                FrameCount = plan.OutputFrameCount,
                TextsInSecondStage = plan.HasTexts,
                Notices = notices,
            };
        }
        else
        {
            export = await Task.Run(() => ExportPlayer(spec, source, outputDir, options, progress, ct), ct).ConfigureAwait(false);
        }
        progress?.Report(new RenderProgress(RenderPhase.Done, "Scene exported", 1.0));
        return export;
    }

    /// <summary>The player's export to render: the folder of a render bundle, or one script
    /// that carries what the folder holds. The model is converted into a scratch folder first,
    /// so the export's folder gets nothing but the export.</summary>
    private RenderExport ExportPlayer(
        SceneSpec spec, RenderSource source, string outputDir, ExportOptions options, IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        string work = CreateJobDirectory();
        try
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new RenderProgress(RenderPhase.ConvertingGeometry, "Converting model"));
            var content = RenderBundleWriter.FromModel(source.Model, spec, work, draft: false, _geometry, source.Leads) with
            {
                Language = options.Language,
            };
            ct.ThrowIfCancellationRequested();
            progress?.Report(new RenderProgress(RenderPhase.GeneratingScript, "Writing the scene"));
            IReadOnlyList<string> files;
            if (options.SingleFile)
            {
                var carried = content.Files.ToDictionary(f => f.Key, f => File.ReadAllBytes(f.Value), StringComparer.Ordinal);
                File.WriteAllText(Path.Combine(outputDir, ExportScriptName), PlayerWriter.SingleFile(content.Plan, carried), Utf8NoBom);
                files = [ExportScriptName];
            }
            else
            {
                files = RenderBundleWriter.WriteFolder(content, outputDir);
            }
            return new RenderExport
            {
                ScriptPath = Path.Combine(outputDir, options.SingleFile ? ExportScriptName : PlayerFiles.EntryFileName),
                Directory = outputDir,
                SingleFile = options.SingleFile,
                Files = files,
                FrameCount = content.Plan.OutputFrameCount,
                TextsInSecondStage = content.Plan.HasTexts,
                Notices = content.Notices,
            };
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private static IReadOnlyList<string> FilesUnder(string folder)
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

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
    /// spec draws one) and returns the spec with its source pointed at what was written, its
    /// output at <paramref name="outputDirectory"/>, and the placeholders of its texts filled in
    /// from the model (<see cref="TextPlaceholders"/>), for whichever builder draws them.</summary>
    private async Task<(ConversionResult Conversion, SceneSpec Spec)> ConvertAsync(
        SceneSpec spec, RenderSource source, string assetDir, string outputDirectory, CancellationToken ct,
        WallPartsMode parts = WallPartsMode.Off)
    {
        var options = new ConversionOptions
        {
            Parts = PartsOf(parts),
            OutputDirectory = assetDir,
            // The reveal values of the spec's reveal travel on the vertices of the mesh.
            Geometry = RevealPlanner.GeometryFor(spec, _geometry, source.Model.Path),
            Leads = source.Leads,
            IncludeSurface = spec.Surface.Mode != SurfaceMode.Off,
        };
        var conversion = await Task.Run(
            () => CaveConversionPipeline.ConvertResolvedFull(source.Model, options), ct).ConfigureAwait(false);
        return (conversion, TextPlaceholders.Resolve(spec, conversion.Meta) with
        {
            Source = spec.Source with
            {
                PlyPath = conversion.Manifest.ModelPath,
                SceneMetaPath = conversion.Manifest.SceneMetaPath,
                SurfacePlyPath = conversion.Manifest.SurfacePath,
                SurfaceImagePath = conversion.Manifest.SurfaceImagePath,
                Parts = conversion.Manifest.Parts,
            },
            Output = spec.Output with { OutputDirectory = outputDirectory },
        });
    }

    /// <summary>What the conversion is asked for walls in parts.</summary>
    public static bool? PartsOf(WallPartsMode mode) => mode switch
    {
        WallPartsMode.On => true,
        WallPartsMode.Off => false,
        _ => null,
    };

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
        IEnumerable<string> segmentPlans;
        try
        {
            segmentPlans = Directory.EnumerateFiles(jobDir, SegmentPlanPattern)
                .Concat(Directory.EnumerateFiles(jobDir, WallPartPattern))
                .Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { segmentPlans = []; }
        foreach (var name in ScratchFileNames.Concat(segmentPlans))
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

    /// <summary>The log of the first stage of one segment of a film.</summary>
    private static string SegmentLogName(string id) => $"job-frames.{id}.log";

    /// <summary>The plans of a film's segments in a job folder (<see cref="MoviePlan.SegmentFileName"/>).</summary>
    private const string SegmentPlanPattern = "scene-plan.*.json";

    /// <summary>The walls in parts in a job folder (<see cref="WallPartFile.FileName"/>).</summary>
    private const string WallPartPattern = "walls.*.ply";

    /// <summary>Whether a file of a job folder is scratch or a log: what the folder may hold and
    /// still be deleted by <see cref="PruneJobFolders"/>.</summary>
    private static bool IsDisposable(string name)
        => DisposableFileNames.Contains(name)
           || (name.StartsWith("job-frames.", StringComparison.Ordinal) && name.EndsWith(".log", StringComparison.Ordinal))
           || (name.StartsWith("scene-plan.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
           || (name.StartsWith("walls.", StringComparison.Ordinal) && name.EndsWith(".ply", StringComparison.Ordinal));

    // ConversionOptions' default file names (the terrain and its image too), the script
    // PrepareAsync writes, and the plan the player plays.
    private static readonly string[] ScratchFileNames =
        ["model.ply", "scene-meta.json", "render.py", "surface.ply", "surface.png", "surface.jpg", ScenePlanSerializer.FileName];

    // The player's package, the frames of a render in two stages whose frames nobody asked for,
    // and the copies of the annotations' photos and of the image of an HDRI light rig.
    private static readonly string[] ScratchFolderNames =
        [PlayerFiles.PackageName, PlanOutputs.FramesFolderName, AnnotationPlanner.PhotosFolder, ScenePlanBuilder.EnvironmentFolder];

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
