// `therion-cli blender` (BA-B14) — render or export a Blender presentation of a cave model,
// headless, reusing the whole Therion.Blender module; or write it as a render bundle (a scene
// plan, its mesh, the player and run scripts in one zip) and optionally render that. A render can
// be a quick draft (--draft), built by either scene builder (--builder), keep its frames
// (--keep-frames) and be written again from them after a text changed (--compose-only); a
// render in two stages that is stopped goes on when the same command is run again. Labels,
// callouts, markers and photos come from a JSON file (--annotations), and the cave can appear
// part by part (--reveal). The leads a preset marks are the stations the model flags as
// continuations, or those of a JSON file (--leads). A film of several shots comes from a
// storyboard file (--storyboard): rendered whole, drafted whole, written as a bundle, or one shot
// of it alone (--shot); a tour of the cave's branches (--tour, BlenderVerb.Tour.cs) is a
// storyboard made from the model. Kept in its
// own class (not a Program.cs local function) so it is unit-testable with in-memory writers and no
// real Blender.

using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Cli;

public static partial class BlenderVerb
{
    /// <summary>
    /// Runs the verb. <paramref name="args"/> is the full CLI argv (args[0] == "blender").
    /// Produced file paths go to <paramref name="stdout"/>; progress/errors to
    /// <paramref name="stderr"/>. Returns 0 (ok) / 1 (render failure) / 2 (usage/spec error).
    /// </summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            stderr.WriteLine("error: 'blender' requires a model file (.lox or .3d).");
            return 2;
        }

        string source = args[1];
        if (!File.Exists(source))
        {
            stderr.WriteLine($"error: file not found: {source}");
            return 2;
        }

        var format = source.EndsWith(".3d", StringComparison.OrdinalIgnoreCase) ? CaveSourceFormat.Survex3d
                   : source.EndsWith(".lox", StringComparison.OrdinalIgnoreCase) ? CaveSourceFormat.Lox
                   : CaveSourceFormat.Unknown;
        if (format == CaveSourceFormat.Unknown)
        {
            stderr.WriteLine($"error: unrecognized model format for '{source}' (expected .lox or .3d).");
            return 2;
        }

        // Options that only say something beside another: refused alone rather than dropped.
        bool bundled = Option(args, "--bundle") is not null, exported = Option(args, "--export") is not null;
        if (Flag(args, "--readme-language") && !bundled && !exported)
        {
            stderr.WriteLine("error: --readme-language is the language of a bundle's README: it goes with --bundle or --export.");
            return 2;
        }
        if (Flag(args, "--estimate") && (bundled || exported))
        {
            stderr.WriteLine("error: --estimate estimates a render: it does not go with --bundle or --export.");
            return 2;
        }
        if (Flag(args, "--one-file") && !exported)
        {
            stderr.WriteLine("error: --one-file writes an export as one script: it goes with --export.");
            return 2;
        }

        // A film of several shots (--storyboard), unless one shot of it is asked for (--shot):
        // that one is a render spec like any other from here on.
        if (!TryReadStoryboard(args, stderr, out var storyboard, out int? shot)) return 2;
        // A tour of the cave's branches (--tour) is a storyboard made from the model.
        if (!TryPlanTour(args, source, storyboard is not null, stdout, stderr, out var tour, out bool written)) return 2;
        if (written) return 0;
        if (tour is not null) storyboard = tour.Storyboard;
        if (storyboard is not null && shot is null)
            return await StoryboardAsync(args, source, format, storyboard, stdout, stderr, tour).ConfigureAwait(false);

        if (!TryBuildSpec(args, stderr, storyboard, shot, out var spec)) return 2;
        if (!TryTimeRoute(source, stderr, ref spec)) return 2;
        if (!TryReadLeads(args, source, spec, stderr, out var leads)) return 2;

        string? bundle = Option(args, "--bundle");
        bool draft = Flag(args, "--draft"), render = Flag(args, "--render");
        string? renderPlan = Option(args, "--render-plan");
        if (bundle is null && (render || renderPlan is not null))
        {
            stderr.WriteLine("error: --render and --render-plan go with --bundle <file.zip|folder>.");
            return 2;
        }
        // How a render is made: these are for the render itself, not for a bundle or an export;
        // the builder is also the export's.
        bool keepFrames = Flag(args, "--keep-frames"), composeOnly = Flag(args, "--compose-only");
        string? builderName = Option(args, "--builder"), framesDir = Option(args, "--frames");
        bool staged = keepFrames || composeOnly || framesDir is not null;
        if (staged && (bundle is not null || Option(args, "--export") is not null))
        {
            stderr.WriteLine("error: --keep-frames, --compose-only and --frames go with a render, not with --bundle or --export.");
            return 2;
        }
        if (builderName is not null && bundle is not null)
        {
            stderr.WriteLine("error: --builder goes with a render or with --export; a bundle is the player's.");
            return 2;
        }
        if (draft && bundle is null && Option(args, "--export") is not null)
        {
            stderr.WriteLine("error: --draft goes with a render or with --bundle, not with --export.");
            return 2;
        }
        var builder = SceneBuilder.Auto;
        if (builderName is not null && !Enum.TryParse(builderName, ignoreCase: true, out builder))
        {
            stderr.WriteLine($"error: --builder takes auto, player or generator, not '{builderName}'.");
            return 2;
        }
        if (!TryRenderPlans(renderPlan, draft, render, stderr, out var plans)) return 2;
        if (!TryParts(args, stderr, out var parts)) return 2;
        FrameRange? range = null;
        if (Option(args, "--frame-range") is { } rangeText)
        {
            range = FrameRange.TryParse(rangeText);
            if (range is null)
            {
                stderr.WriteLine($"error: --frame-range takes two frame numbers from 1, such as 1-120, not '{rangeText}'.");
                return 2;
            }
            if (framesDir is null || composeOnly || keepFrames || bundle is not null || Option(args, "--export") is not null)
            {
                stderr.WriteLine("error: --frame-range renders frames into the folder --frames names, and nothing else: "
                    + "it does not go with --compose-only, --keep-frames, --bundle or --export. Write the output after with --compose-only.");
                return 2;
            }
        }
        if (bundle is not null)
        {
            if (bundle.StartsWith("--", StringComparison.Ordinal) || Option(args, "--export") is not null)
            {
                stderr.WriteLine("error: --bundle takes a .zip file or a folder, and does not go with --export.");
                return 2;
            }
            var model = new ResolvedModelSource { Path = Path.GetFullPath(source), Format = format, Kind = ModelSourceKind.ExternalFile };
            string language = Option(args, "--readme-language") ?? "en";
            if (!RenderBundleWriter.ReadmeLanguages.Contains(language, StringComparer.Ordinal))
            {
                stderr.WriteLine($"error: --readme-language takes one of: {string.Join(", ", RenderBundleWriter.ReadmeLanguages)}.");
                return 2;
            }
            return await BundleAsync(
                    work => RenderBundleWriter.FromModel(model, spec, work, draft, leads: leads, parts: BlenderRenderService.PartsOf(parts)), bundle, draft, language, plans,
                    Option(args, "--out"), Option(args, "--blender"), stdout, stderr)
                .ConfigureAwait(false);
        }

        var renderSource = new RenderSource(
            new ResolvedModelSource { Path = Path.GetFullPath(source), Format = format, Kind = ModelSourceKind.ExternalFile },
            leads);

        var locator = new BlenderLocator(new ProcessBlenderProbe());
        var runner = new BlenderRunner(new RealBlenderProcessLauncher());
        string? blenderOverride = Option(args, "--blender");
        string outDir = Option(args, "--out") ?? Directory.GetCurrentDirectory();
        var service = new BlenderRenderService(locator, runner, outDir, geometry: null, blenderOverride: () => blenderOverride);
        var progress = new Progress<RenderProgress>(p => stderr.WriteLine($"[{p.Phase}] {p.Message}"));

        try
        {
            if (Option(args, "--export") is { } exportDir)
            {
                // What Export script… writes in the panel: the scene a render shows, by the builder
                // it uses (the player's folder, or with --one-file one script that carries it), or
                // the script generator's script with --builder generator.
                string readme = Option(args, "--readme-language") ?? "en";
                if (!RenderBundleWriter.ReadmeLanguages.Contains(readme, StringComparer.Ordinal))
                {
                    stderr.WriteLine($"error: --readme-language takes one of: {string.Join(", ", RenderBundleWriter.ReadmeLanguages)}.");
                    return 2;
                }
                var export = await service.ExportAsync(
                        spec, renderSource, exportDir, new ExportOptions { Builder = builder, SingleFile = Flag(args, "--one-file"), Language = readme }, progress)
                    .ConfigureAwait(false);
                foreach (string notice in export.Notices) stderr.WriteLine($"warning: {notice}");
                if (export.LeftOut.Count > 0)
                    stderr.WriteLine($"warning: the exported script does not show {string.Join(", ", export.LeftOut)}");
                stdout.WriteLine(export.ScriptPath);
                string what = export.Builder == SceneBuilder.Generator ? "the script generator's script + assets"
                    : export.SingleFile ? "one script with the whole scene" : "the player's folder";
                stderr.WriteLine($"exported {what} to {exportDir}");
                return 0;
            }

            // The frames of a render in two stages have a place of their own beside the outputs,
            // so that the same command, run again, goes on with them or writes the output from them.
            var options = new RenderOptions
            {
                Builder = builder,
                Draft = draft,
                KeepFrames = keepFrames,
                ComposeOnly = composeOnly,
                FramesDirectory = Path.GetFullPath(framesDir ?? Path.Combine(outDir, spec.Output.BaseName + (draft ? "-draft" : "") + "-frames")),
                FrameRange = range,
                Parts = parts,
            };
            if (Flag(args, "--estimate"))
            {
                var estimate = await service.EstimateAsync(spec, renderSource, options).ConfigureAwait(false);
                stdout.WriteLine(estimate.Describe());
                if (estimate.TooManyTriangles) stderr.WriteLine($"warning: more than {RenderEstimate.WarnTriangles / 1_000_000} million triangles in one scene");
                return 0;
            }
            var result = await service.RenderAsync(spec, renderSource, options, progress).ConfigureAwait(false);
            foreach (string warning in result.Warnings) stderr.WriteLine($"warning: {warning}");
            if (result.Builder == SceneBuilder.Generator && result.NotCarried.Count > 0)
                stderr.WriteLine($"built by the script generator: the player does not carry {string.Join(", ", result.NotCarried)}");
            if (result.LeftOut.Count > 0)
                stderr.WriteLine($"warning: the {(result.Builder == SceneBuilder.Generator ? "script generator" : "player")} does not show {string.Join(", ", result.LeftOut)}");
            if (result.Succeeded)
            {
                foreach (var path in result.OutputPaths) stdout.WriteLine(path);
                string what = composeOnly ? "wrote" : draft ? "rendered a draft of" : "rendered";
                if (range is not null) what = $"rendered frames {range} of";
                stderr.WriteLine($"{what} {result.FrameCount} frame(s){(result.Device is null ? "" : " on " + result.Device)} in {result.Duration.TotalSeconds:0.0}s");
                if (result.FramesDirectory is not null) stderr.WriteLine($"the frames are in {result.FramesDirectory}");
                return 0;
            }

            if (result.FailureKind == RenderFailureKind.BuilderRefused)
            {
                stderr.WriteLine($"error: {result.ErrorMessage}");
                return 2;
            }
            stderr.WriteLine($"error: render failed ({result.FailureKind}): {result.ErrorMessage}");
            if (result.FramesDirectory is not null)
                stderr.WriteLine($"the frames rendered so far are in {result.FramesDirectory}; run the same command again to go on from them");
            return 1;
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"error: invalid render spec: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or ModelSourceNotFoundException or CaveFileFormatException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Which plans of a bundle <c>--render</c> renders, in order.</summary>
    [Flags]
    private enum RenderPlans
    {
        None = 0,
        Draft = 1,
        Full = 2,
    }

    /// <summary>
    /// What <c>--render</c> renders: <c>--render-plan full|draft|both</c>, or by default the draft
    /// when the bundle has one and the full plan otherwise. A bundle made with <c>--draft</c> is
    /// usually made for a quick look at the camera move before the long render, and the full
    /// render stays one option away; both renders the draft first.
    /// </summary>
    private static bool TryRenderPlans(string? choice, bool draft, bool render, TextWriter stderr, out RenderPlans plans)
    {
        plans = RenderPlans.None;
        if (choice is not null && !render)
        {
            stderr.WriteLine("error: --render-plan goes with --render.");
            return false;
        }
        if (!render) return true;
        plans = choice?.ToLowerInvariant() switch
        {
            null => draft ? RenderPlans.Draft : RenderPlans.Full,
            "full" => RenderPlans.Full,
            "draft" => RenderPlans.Draft,
            "both" => RenderPlans.Draft | RenderPlans.Full,
            _ => RenderPlans.None,
        };
        if (plans == RenderPlans.None)
        {
            stderr.WriteLine($"error: --render-plan takes full, draft or both, not '{choice}'.");
            return false;
        }
        if (plans.HasFlag(RenderPlans.Draft) && !draft)
        {
            stderr.WriteLine($"error: --render-plan {choice} renders the draft, which a bundle has only with --draft.");
            return false;
        }
        return true;
    }

    /// <summary>Writes a render bundle (a zip, or a folder when the path does not end in .zip)
    /// and renders the <paramref name="plans"/> asked for: a zip is unpacked into a temporary
    /// folder first, and what they write is moved to <paramref name="outDir"/>, or beside the
    /// zip.</summary>
    /// <param name="make">Converts the model into the folder it is given and returns the
    /// bundle's content: one scene, or a film of several.</param>
    private static async Task<int> BundleAsync(
        Func<string, RenderBundleContent> make, string bundle, bool draft, string language,
        RenderPlans plans, string? outDir,
        string? blenderOverride, TextWriter stdout, TextWriter stderr)
    {
        bool zip = bundle.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string work = TempFolder("thide-bundle-");
        RenderBundleContent content;
        try
        {
            stderr.WriteLine($"[{RenderPhase.ConvertingGeometry}] Converting model");
            content = make(work) with { Language = language };
            if (zip) RenderBundleWriter.WriteZip(content, bundle);
            else RenderBundleWriter.WriteFolder(content, bundle);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"error: invalid {(ex.ParamName == "storyboard" ? "storyboard" : "render spec")}: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CaveFileFormatException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 1;
        }
        finally
        {
            TryDelete(work);
        }

        foreach (string lost in content.NotCarried)
            stderr.WriteLine($"warning: the bundle leaves out '{lost}': a scene plan cannot say it yet.");
        foreach (string notice in content.Notices) stderr.WriteLine($"warning: {notice}");
        stdout.WriteLine(Path.GetFullPath(bundle));
        stderr.WriteLine($"wrote a render bundle{(draft ? " with a draft" : "")} to {bundle}");
        if (plans == RenderPlans.None) return 0;

        var located = new BlenderLocator(new ProcessBlenderProbe()).Locate(blenderOverride);
        if (!located.IsUsable)
        {
            stderr.WriteLine($"error: no usable Blender: {located.Detail}");
            return 1;
        }
        var blender = located.Installation!;
        if (BlenderLocator.UntestedVersionWarning(blender.Version) is { } untested) stderr.WriteLine($"warning: {untested}");

        // A zip unpacks into one folder named after it.
        string? unpacked = zip ? TempFolder("thide-bundle-render-") : null;
        string folder = unpacked is null ? bundle : Path.Combine(unpacked, RenderBundleWriter.FolderName(bundle));
        if (unpacked is not null)
        {
            try { System.IO.Compression.ZipFile.ExtractToDirectory(bundle, unpacked); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: the bundle cannot be unpacked: {ex.Message}");
                TryDelete(unpacked);
                return 1;
            }
        }
        // What a plan of the bundle writes, as the runner collects it: a scene's, or a film's,
        // whose plan is the movie's and takes the player through every segment.
        string In(string directory) => Path.GetFullPath(Path.Combine(folder, directory));
        (string File, int Frames, OutputSpec Output, long FrameBytes, bool Full) Of(ScenePlan plan, string file, bool full)
            => (file, plan.OutputFrameCount, PlanOutputs.SpecOf(plan, In(plan.Output.Directory)), plan.HasTexts ? PlanOutputs.FrameBytes(plan) : 0, full);
        (string File, int Frames, OutputSpec Output, long FrameBytes, bool Full) OfFilm(StoryboardPlan film, string file, bool full)
            => (file, film.Movie.OutputFrameCount, PlanOutputs.SpecOf(film, In(film.Movie.Output.Directory)), PlanOutputs.FrameBytes(film), full);
        var toRender = new List<(string File, int Frames, OutputSpec Output, long FrameBytes, bool Full)>();
        if (plans.HasFlag(RenderPlans.Draft))
            toRender.Add(content.Film is null
                ? Of(content.Draft!, RenderBundleWriter.DraftPlanFileName, full: false)
                : OfFilm(content.FilmDraft!, RenderBundleWriter.DraftPlanFileName, full: false));
        if (plans.HasFlag(RenderPlans.Full))
            toRender.Add(content.Film is null
                ? Of(content.Plan, ScenePlanSerializer.FileName, full: true)
                : OfFilm(content.Film, ScenePlanSerializer.FileName, full: true));
        string? destination = outDir ?? (zip ? Path.GetDirectoryName(Path.GetFullPath(bundle)) : null);
        var progress = new Progress<RenderProgress>(p => stderr.WriteLine($"[{p.Phase}] {p.Message}"));
        foreach (var (file, frames, output, frameBytes, full) in toRender)
        {
            // The player takes a plan with texts, and a film, through its stages itself.
            var job = new RenderJob(Path.Combine(folder, PlayerFiles.EntryFileName), folder, frames, output)
            {
                ScriptArguments = ["--plan", Path.Combine(folder, file)],
                ExtraBytes = frameBytes,
            };
            var result = await new BlenderRunner(new RealBlenderProcessLauncher()).RunAsync(blender, job, progress).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                stderr.WriteLine($"error: render of {file} failed ({result.FailureKind}): {result.ErrorMessage}");
                if (zip) stderr.WriteLine($"the unpacked bundle and its job.log are in {folder}");
                return 1;
            }
            foreach (string warning in result.Warnings) stderr.WriteLine($"warning: {warning}");

            var outputs = result.OutputPaths.ToList();
            // The captions' subtitles lie where the movie was written; they go where it goes.
            if (full && content.Captions.Count > 0)
                foreach (string extension in new[] { Subtitles.SrtExtension, Subtitles.VttExtension })
                {
                    string subtitles = Path.Combine(output.OutputDirectory, output.BaseName + extension);
                    if (File.Exists(subtitles)) outputs.Add(subtitles);
                }
            if (destination is not null)
            {
                Directory.CreateDirectory(destination);
                for (int i = 0; i < outputs.Count; i++)
                {
                    string target = Path.Combine(destination, Path.GetFileName(outputs[i]));
                    File.Move(outputs[i], target, overwrite: true);
                    outputs[i] = target;
                }
            }
            foreach (string path in outputs) stdout.WriteLine(path);
            stderr.WriteLine($"rendered {file}: {result.FrameCount} frame(s) on {result.Device} in {result.Duration.TotalSeconds:0.0}s");
        }
        if (unpacked is not null) TryDelete(unpacked);
        return 0;
    }

    private static string TempFolder(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The options that say what one scene shows: with a storyboard, the storyboard says it.</summary>
    private static readonly string[] SceneOptions =
    [
        "--title", "--credits", "--end-card", "--end-card-seconds", "--caption", "--annotations", "--reveal", "--reveal-from",
        "--reveal-dates", "--reveal-seconds", "--hdri", "--hdri-rotation", "--hdri-strength", "--lamp-reach", "--route", "--route-seconds",
    ];

    /// <summary>
    /// The storyboard of <c>--storyboard &lt;file.json&gt;</c>, and the shot of it that
    /// <c>--shot &lt;n&gt;</c> asks for alone (the first is 1). Both null without the option.
    /// </summary>
    private static bool TryReadStoryboard(IReadOnlyList<string> args, TextWriter stderr, out Storyboard? storyboard, out int? shot)
    {
        storyboard = null;
        shot = null;
        string? path = Option(args, "--storyboard"), number = Option(args, "--shot"), composite = Option(args, "--composite");
        if (composite is null && Flag(args, "--composite"))
        {
            stderr.WriteLine($"error: --composite takes a composite preset's file (<name>{PresetStore.CompositeExtension}) or the name of a built-in one: {CompositeNames()}.");
            return false;
        }
        if (path is null && composite is null)
        {
            if (number is null && !Flag(args, "--shot")) return true;
            stderr.WriteLine("error: --shot goes with a storyboard (--storyboard <file.json>) or a composite (--composite <file|name>).");
            return false;
        }
        string option = composite is null ? "--storyboard" : "--composite";
        if (path is not null && composite is not null)
        {
            stderr.WriteLine("error: --storyboard and --composite are two films: give one of them.");
            return false;
        }
        if (Option(args, "--spec") is not null || Option(args, "--preset") is not null)
        {
            stderr.WriteLine($"error: {option} does not go with --spec or --preset: a storyboard's shots say what they show.");
            return false;
        }
        if (composite is not null)
        {
            if (!TryReadComposite(composite, stderr, out storyboard)) return false;
        }
        else
        {
            if (!File.Exists(path))
            {
                stderr.WriteLine($"error: storyboard file not found: {path}");
                return false;
            }
            try { storyboard = StoryboardSerializer.ReadFile(path!); }
            catch (StoryboardFormatException ex)
            {
                stderr.WriteLine($"error: bad storyboard: {ex.Message}");
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return false;
            }
            // A photo and an HDRI image are named relative to the storyboard's file, as an
            // annotations file names them.
            storyboard = StoryboardFiles.Rooted(storyboard, Path.GetDirectoryName(Path.GetFullPath(path!))!);
        }
        // A storyboard says its own language; a composite takes the one it is given.
        if (composite is not null && Option(args, "--tour-language") is { } language)
        {
            if (!TourBuilder.Languages.Contains(language, StringComparer.Ordinal))
            {
                stderr.WriteLine($"error: --tour-language takes one of: {string.Join(", ", TourBuilder.Languages)}.");
                storyboard = null;
                return false;
            }
            storyboard = storyboard! with { Language = language };
        }

        if (number is not null || Flag(args, "--shot"))
        {
            if (!int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
                || n < 1 || n > storyboard.Shots.Count)
            {
                stderr.WriteLine($"error: --shot takes the number of a shot, 1 to {storyboard.Shots.Count}, not '{number}'.");
                return false;
            }
            shot = n;
        }
        var errors = StoryboardValidator.Validate(storyboard, film: shot is null);
        if (errors.Count == 0) return true;
        stderr.WriteLine("error: invalid storyboard:");
        foreach (var error in errors) stderr.WriteLine($"  {error.Path}: {error.Message}");
        storyboard = null;
        return false;
    }

    private static string CompositeNames() => string.Join(", ", BuiltInComposites.All.Select(c => $"\"{c.Name}\""));

    /// <summary>
    /// The film of <c>--composite</c>: a composite preset's file (one saved from ThIDE's panel, or
    /// written by hand), or a built-in composite by its name in any case. A file wins over a name.
    /// </summary>
    private static bool TryReadComposite(string fileOrName, TextWriter stderr, out Storyboard? storyboard)
    {
        storyboard = null;
        if (File.Exists(fileOrName))
        {
            try { storyboard = CompositePresetSerializer.ReadFile(fileOrName).Storyboard; }
            catch (PresetFormatException ex)
            {
                stderr.WriteLine($"error: bad composite preset: {ex.Message}");
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return false;
            }
            // As a storyboard's file: the files it names are relative to it.
            storyboard = StoryboardFiles.Rooted(storyboard, Path.GetDirectoryName(Path.GetFullPath(fileOrName))!);
            return true;
        }
        if (BuiltInComposites.ByName(fileOrName) is { } builtIn)
        {
            storyboard = builtIn.Storyboard;
            return true;
        }
        bool looksLikeFile = fileOrName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                             || fileOrName.Contains(Path.DirectorySeparatorChar) || fileOrName.Contains(Path.AltDirectorySeparatorChar);
        stderr.WriteLine(looksLikeFile
            ? $"error: composite preset file not found: {fileOrName}"
            : $"error: unknown composite preset '{fileOrName}'. Built in: {CompositeNames()}; or give the file of one (<name>{PresetStore.CompositeExtension}).");
        return false;
    }

    /// <summary>
    /// A whole storyboard: rendered as a film (every shot's frames, then the film from them),
    /// as a draft of every shot with <c>--draft</c>, or written as a bundle with <c>--bundle</c>.
    /// </summary>
    private static async Task<int> StoryboardAsync(
        IReadOnlyList<string> args, string source, CaveSourceFormat format, Storyboard storyboard, TextWriter stdout, TextWriter stderr,
        Tour? tour = null)
    {
        if (SceneOptions.FirstOrDefault(o => Flag(args, o)) is { } said)
        {
            stderr.WriteLine($"error: {said} does not go with a whole storyboard: say it in the storyboard, or render one shot (--shot <n>).");
            return 2;
        }
        if (Option(args, "--export") is not null)
        {
            stderr.WriteLine("error: --export writes one scene's script; a storyboard is written as a bundle (--bundle <file.zip|folder>).");
            return 2;
        }
        string? builderName = Option(args, "--builder");
        if (builderName is not null && !builderName.Equals("auto", StringComparison.OrdinalIgnoreCase) && !builderName.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            stderr.WriteLine("error: a storyboard is rendered by the player (--builder auto or player).");
            return 2;
        }

        // The leads are read when any shot marks them. A tour has read them, and marks its own.
        bool marksLeads = Enumerable.Range(0, storyboard.Shots.Count).Any(i => StoryboardPlanner.ShotSpec(storyboard, i).Labels.Leads.Show);
        IReadOnlyList<SourceLead> leads = [];
        if (tour is null && !TryReadLeads(args, source, new SceneSpec { Labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = marksLeads } } }, stderr, out leads))
            return 2;

        var model = new ResolvedModelSource { Path = Path.GetFullPath(source), Format = format, Kind = ModelSourceKind.ExternalFile };
        string? bundle = Option(args, "--bundle");
        bool draft = Flag(args, "--draft"), render = Flag(args, "--render");
        bool keepFrames = Flag(args, "--keep-frames"), composeOnly = Flag(args, "--compose-only");
        string? framesDir = Option(args, "--frames"), renderPlan = Option(args, "--render-plan");
        if (bundle is null && (render || renderPlan is not null))
        {
            stderr.WriteLine("error: --render and --render-plan go with --bundle <file.zip|folder>.");
            return 2;
        }
        if (Option(args, "--frame-range") is not null)
        {
            stderr.WriteLine("error: --frame-range is a range of one scene's frames: render one shot of the storyboard (--shot <n>).");
            return 2;
        }
        if (!TryParts(args, stderr, out var parts)) return 2;
        if (bundle is not null)
        {
            if (keepFrames || composeOnly || framesDir is not null || builderName is not null)
            {
                stderr.WriteLine("error: --builder, --keep-frames, --compose-only and --frames go with a render, not with --bundle.");
                return 2;
            }
            if (bundle.StartsWith("--", StringComparison.Ordinal))
            {
                stderr.WriteLine("error: --bundle takes a .zip file or a folder.");
                return 2;
            }
            string language = Option(args, "--readme-language") ?? "en";
            if (!RenderBundleWriter.ReadmeLanguages.Contains(language, StringComparer.Ordinal))
            {
                stderr.WriteLine($"error: --readme-language takes one of: {string.Join(", ", RenderBundleWriter.ReadmeLanguages)}.");
                return 2;
            }
            if (!TryRenderPlans(renderPlan, draft, render, stderr, out var plans)) return 2;
            return await BundleAsync(
                    work =>
                    {
                        var content = RenderBundleWriter.FromStoryboard(model, storyboard, work, draft, leads: leads, parts: BlenderRenderService.PartsOf(parts));
                        // What a tour chose, left out and cut short is said in the bundle too.
                        return tour is null ? content : content with { ReadmeSections = [.. content.ReadmeSections, TourReadme(tour, language)] };
                    },
                    bundle, draft, language, plans,
                    Option(args, "--out"), Option(args, "--blender"), stdout, stderr)
                .ConfigureAwait(false);
        }

        string? blenderOverride = Option(args, "--blender");
        string outDir = Option(args, "--out") ?? Directory.GetCurrentDirectory();
        var service = new BlenderRenderService(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), outDir,
            geometry: null, blenderOverride: () => blenderOverride);
        var progress = new Progress<RenderProgress>(p => stderr.WriteLine($"[{p.Phase}] {p.Message}"));
        var options = new RenderOptions
        {
            Draft = draft,
            KeepFrames = keepFrames,
            ComposeOnly = composeOnly,
            // The frames of every shot have a place of their own beside the outputs, so that the
            // same command, run again, goes on with the shot it stopped in.
            FramesDirectory = Path.GetFullPath(framesDir ?? Path.Combine(outDir, storyboard.Output.BaseName + (draft ? "-draft" : "") + "-frames")),
            Parts = parts,
        };
        try
        {
            if (Flag(args, "--estimate"))
            {
                var estimate = await service.EstimateStoryboardAsync(storyboard, new RenderSource(model, leads), options).ConfigureAwait(false);
                stdout.WriteLine(estimate.Describe());
                if (estimate.TooManyTriangles) stderr.WriteLine($"warning: more than {RenderEstimate.WarnTriangles / 1_000_000} million triangles in one scene");
                return 0;
            }
            var result = await service.RenderStoryboardAsync(storyboard, new RenderSource(model, leads), options, progress).ConfigureAwait(false);
            foreach (string warning in result.Warnings) stderr.WriteLine($"warning: {warning}");
            if (result.Succeeded)
            {
                foreach (var path in result.OutputPaths) stdout.WriteLine(path);
                string what = composeOnly ? "wrote" : draft ? "rendered a draft of" : "rendered";
                stderr.WriteLine($"{what} a film of {storyboard.Shots.Count} shot(s), {result.FrameCount} frame(s){(result.Device is null ? "" : " on " + result.Device)} in {result.Duration.TotalSeconds:0.0}s");
                if (result.FramesDirectory is not null) stderr.WriteLine($"the frames are in {result.FramesDirectory}");
                return 0;
            }
            if (result.FailureKind == RenderFailureKind.BuilderRefused)
            {
                stderr.WriteLine($"error: {result.ErrorMessage}");
                return 2;
            }
            stderr.WriteLine($"error: render failed ({result.FailureKind}): {result.ErrorMessage}");
            if (result.FramesDirectory is not null)
                stderr.WriteLine($"the frames rendered so far are in {result.FramesDirectory}; run the same command again to go on from them");
            return 1;
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"error: invalid storyboard: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or ModelSourceNotFoundException or CaveFileFormatException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static bool TryBuildSpec(IReadOnlyList<string> args, TextWriter stderr, Storyboard? storyboard, int? shot, out SceneSpec spec)
    {
        spec = BuiltInPresets.OrbitShowcase().Spec;

        if (storyboard is not null && shot is { } number)
        {
            // One shot of a storyboard, as a scene of its own.
            spec = StoryboardPlanner.ShotSpec(storyboard, number - 1);
        }
        else if (Option(args, "--spec") is { } specPath)
        {
            if (!File.Exists(specPath)) { stderr.WriteLine($"error: spec file not found: {specPath}"); return false; }
            try { spec = SceneSpecSerializer.ReadFile(specPath); }
            catch (SceneSpecFormatException ex) { stderr.WriteLine($"error: bad spec: {ex.Message}"); return false; }
        }
        else if (Option(args, "--preset") is { } presetName)
        {
            var preset = BuiltInPresets.ByName(presetName);
            if (preset is null)
            {
                stderr.WriteLine($"error: unknown preset '{presetName}'. Available: {string.Join(", ", BuiltInPresets.All.Select(p => $"\"{p.Name}\""))}.");
                return false;
            }
            spec = preset.Spec;
        }
        if (!(TryAddTexts(args, stderr, ref spec) && TryAddAnnotations(args, stderr, ref spec) && TryAddReveal(args, stderr, ref spec)
              && TryAddHdri(args, stderr, ref spec) && TryAddRoute(args, stderr, ref spec)))
            return false;
        // --lamp-reach lights the scene by the lamp on the camera, turned up with the distance it looks over.
        if (Flag(args, "--lamp-reach"))
        {
            if (Option(args, "--hdri") is not null)
            {
                stderr.WriteLine("error: --lamp-reach lights the scene by the lamp on the camera; it does not go with --hdri.");
                return false;
            }
            spec = spec with { Lighting = spec.Lighting with { Rig = LightingRig.Headlamp, LampReach = true } };
        }
        return true;
    }

    /// <summary>
    /// The leads a render marks. With <c>--leads &lt;file.json&gt;</c> (once for each file) they
    /// are the stations the files list; without it, the stations the model itself flags as
    /// continuations, which a <c>.lox</c> file carries. The model is read for them only when the
    /// spec shows lead markers.
    /// </summary>
    private static bool TryReadLeads(IReadOnlyList<string> args, string source, SceneSpec spec, TextWriter stderr, out IReadOnlyList<SourceLead> leads)
    {
        leads = [];
        var files = Options(args, "--leads").ToList();
        if (files.Count > 0)
        {
            var listed = new List<SourceLead>();
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    stderr.WriteLine($"error: leads file not found: {file}");
                    return false;
                }
                try { listed.AddRange(ModelLeads.ReadFile(file)); }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
                {
                    stderr.WriteLine($"error: bad leads file '{file}': {ex.Message}");
                    return false;
                }
            }
            leads = listed;
            if (!spec.Labels.Leads.Show)
                stderr.WriteLine("warning: --leads lists leads, but the preset or spec shows no lead markers (labels.leads.show).");
            return true;
        }
        if (!spec.Labels.Leads.Show) return true;
        // A model that cannot be read is reported by the conversion, with its own message.
        try { leads = ModelLeads.FromModel(CaveModelReader.ReadFile(source)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CaveFileFormatException) { }
        return true;
    }

    /// <summary>
    /// The image the scene is lit by, given on the command line over the preset's or the spec's
    /// lighting: <c>--hdri &lt;image.hdr|.exr&gt;</c> makes the image the background and the
    /// light, <c>--hdri-rotation &lt;degrees&gt;</c> turns it about the vertical and
    /// <c>--hdri-strength &lt;n&gt;</c> says how brightly it shines.
    /// </summary>
    /// <summary>
    /// <c>--route &lt;from&gt;[:&lt;via&gt;…]:&lt;to&gt;</c> flies the camera along the passages
    /// from one station to another, stopping at those between (<c>entrance</c> and
    /// <c>farthest</c> name stations the model gives). The route sets the clip's length, unless
    /// <c>--route-seconds &lt;n&gt;</c> gives one to fit it into.
    /// </summary>
    private static bool TryAddRoute(IReadOnlyList<string> args, TextWriter stderr, ref SceneSpec spec)
    {
        string? stops = Option(args, "--route"), seconds = Option(args, "--route-seconds");
        if (stops is null)
        {
            if (seconds is null) return true;
            stderr.WriteLine("error: --route-seconds goes with --route <from>:<to>.");
            return false;
        }
        var names = stops.Split(':');
        if (stops.StartsWith("--", StringComparison.Ordinal) || names.Length < 2 || names.Any(string.IsNullOrWhiteSpace))
        {
            stderr.WriteLine($"error: --route takes stations as <from>:<to> or <from>:<via>:…:<to> (\"entrance:farthest\", \"e1:s40\"), not '{stops}'.");
            return false;
        }
        var route = (spec.Camera.Route ?? new RouteParams()) with
        {
            From = names[0], To = names[^1], Via = names[1..^1], Stations = [], Timing = RouteTiming.Natural,
        };
        var animation = spec.Animation;
        if (seconds is not null)
        {
            if (!double.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double length)
                || !(length > 0) || !double.IsFinite(length))
            {
                stderr.WriteLine($"error: --route-seconds takes a number of seconds (\"45\"), not '{seconds}'.");
                return false;
            }
            route = route with { Timing = RouteTiming.Fit };
            animation = animation with { DurationSeconds = length };
        }
        spec = spec with { Camera = spec.Camera with { Template = CameraTemplate.Route, Route = route }, Animation = animation };
        return true;
    }

    /// <summary>A route that sets the clip's length is measured on the model here, before the
    /// render or the bundle is planned; a station it cannot find is an error.</summary>
    private static bool TryTimeRoute(string source, TextWriter stderr, ref SceneSpec spec)
    {
        if (spec.Camera.Template != CameraTemplate.Route || (spec.Camera.Route ?? new RouteParams()).Timing != RouteTiming.Natural)
            return true;
        try
        {
            var geometry = Therion.CaveGeometry.GeometryStage.Build(CaveModelReader.ReadFile(source));
            spec = CameraRoutes.WithNaturalDuration(spec, CameraFraming.FromGeometry(geometry));
            return true;
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"error: {ex.Message.Split(" (Parameter", 2)[0]}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CaveFileFormatException)
        {
            stderr.WriteLine($"error: cannot read '{source}' for its route: {ex.Message}");
            return false;
        }
    }

    private static bool TryAddHdri(IReadOnlyList<string> args, TextWriter stderr, ref SceneSpec spec)
    {
        string? image = Option(args, "--hdri"), rotation = Option(args, "--hdri-rotation"), strength = Option(args, "--hdri-strength");
        if (image is null && rotation is null && strength is null) return true;

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var lighting = spec.Lighting;
        if (image is not null)
        {
            if (!File.Exists(image))
            {
                stderr.WriteLine($"error: file not found: {image}");
                return false;
            }
            lighting = lighting with { Rig = LightingRig.HdriFile, HdriPath = Path.GetFullPath(image) };
        }
        else if (lighting.Rig != LightingRig.HdriFile)
        {
            stderr.WriteLine("error: --hdri-rotation and --hdri-strength go with an image the scene is lit by (--hdri <image.hdr|.exr>).");
            return false;
        }
        if (rotation is not null)
        {
            if (!double.TryParse(rotation, System.Globalization.NumberStyles.Float, culture, out double degrees) || !double.IsFinite(degrees))
            {
                stderr.WriteLine($"error: --hdri-rotation takes a number of degrees (\"90\", \"-45\"), not '{rotation}'.");
                return false;
            }
            lighting = lighting with { HdriRotation = degrees };
        }
        if (strength is not null)
        {
            if (!double.TryParse(strength, System.Globalization.NumberStyles.Float, culture, out double factor) || !double.IsFinite(factor))
            {
                stderr.WriteLine($"error: --hdri-strength takes a number (\"1\" is the image as it is, \"2\" twice as bright), not '{strength}'.");
                return false;
            }
            lighting = lighting with { Strength = factor };
        }
        spec = spec with { Lighting = lighting };
        return true;
    }

    /// <summary>
    /// The reveal that is given on the command line, over the preset's or the spec's:
    /// <c>--reveal date|order|distance</c> shows the cave part by part, by the dates of its
    /// survey, by the survey's order or by the distance from an entrance;
    /// <c>--reveal-from &lt;station&gt;</c> measures the distance from a station,
    /// <c>--reveal-dates &lt;file.3d&gt;</c> names the file that has the dates, and
    /// <c>--reveal-seconds &lt;start&gt;-&lt;end&gt;</c> says when it runs.
    /// </summary>
    private static bool TryAddReveal(IReadOnlyList<string> args, TextWriter stderr, ref SceneSpec spec)
    {
        string? by = Option(args, "--reveal"), from = Option(args, "--reveal-from"), dates = Option(args, "--reveal-dates"),
                seconds = Option(args, "--reveal-seconds");
        if (by is null && from is null && dates is null && seconds is null) return true;

        var reveal = spec.Reveal ?? new RevealSpec();
        if (by is not null)
        {
            if (!Enum.TryParse(by, ignoreCase: true, out RevealBy source) || !Enum.IsDefined(source) || int.TryParse(by, out _))
            {
                stderr.WriteLine($"error: --reveal takes date, order or distance, not '{by}'.");
                return false;
            }
            // What went with another kind of reveal does not go with this one.
            reveal = reveal with { By = source, From = source == RevealBy.Distance ? reveal.From : null, DatesPath = source == RevealBy.Date ? reveal.DatesPath : null };
        }
        else if (spec.Reveal is null)
        {
            stderr.WriteLine("error: --reveal-from, --reveal-dates and --reveal-seconds go with a reveal (--reveal date|order|distance).");
            return false;
        }
        if (from is not null)
        {
            if (reveal.By != RevealBy.Distance)
            {
                stderr.WriteLine("error: --reveal-from names the station a reveal by distance is measured from (--reveal distance).");
                return false;
            }
            reveal = reveal with { From = from };
        }
        if (dates is not null)
        {
            if (reveal.By != RevealBy.Date)
            {
                stderr.WriteLine("error: --reveal-dates names the file with the survey dates of a reveal by date (--reveal date).");
                return false;
            }
            if (!File.Exists(dates))
            {
                stderr.WriteLine($"error: file not found: {dates}");
                return false;
            }
            reveal = reveal with { DatesPath = Path.GetFullPath(dates) };
        }
        if (seconds is not null)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            int dash = seconds.IndexOf('-', StringComparison.Ordinal);
            if (dash <= 0
                || !double.TryParse(seconds[..dash], System.Globalization.NumberStyles.Float, culture, out double start)
                || !double.TryParse(seconds[(dash + 1)..], System.Globalization.NumberStyles.Float, culture, out double end))
            {
                stderr.WriteLine($"error: --reveal-seconds takes \"<start>-<end>\" in seconds (\"1-9.5\"), not '{seconds}'.");
                return false;
            }
            reveal = reveal with { Start = start, End = end };
        }
        spec = spec with { Reveal = reveal };
        return true;
    }

    /// <summary>
    /// The annotations of every <c>--annotations &lt;file.json&gt;</c>, added to the preset's or
    /// the spec's: labels, callouts, markers and photos, each at a station or a point. A photo's
    /// image is named relative to the file it is listed in.
    /// </summary>
    private static bool TryAddAnnotations(IReadOnlyList<string> args, TextWriter stderr, ref SceneSpec spec)
    {
        var added = new List<AnnotationSpec>();
        foreach (string file in Options(args, "--annotations"))
        {
            if (!File.Exists(file))
            {
                stderr.WriteLine($"error: annotations file not found: {file}");
                return false;
            }
            try { added.AddRange(AnnotationsFile.ReadFile(file)); }
            catch (SceneSpecFormatException ex)
            {
                stderr.WriteLine($"error: bad annotations file '{file}': {ex.Message}");
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return false;
            }
        }
        if (added.Count > 0) spec = spec with { Annotations = [.. spec.Annotations, .. added] };
        return true;
    }

    /// <summary>
    /// The texts over the picture that are given on the command line, added to the preset's or
    /// the spec's: <c>--title</c>, <c>--credits</c>, <c>--end-card</c> (with
    /// <c>--end-card-seconds</c>) and any number of <c>--caption "&lt;start&gt;-&lt;end&gt;:&lt;text&gt;"</c>.
    /// </summary>
    private static bool TryAddTexts(IReadOnlyList<string> args, TextWriter stderr, ref SceneSpec spec)
    {
        var overlays = spec.Labels.Overlays;
        if (Option(args, "--title") is { } title) overlays = overlays with { Title = title };
        if (Option(args, "--credits") is { } credits) overlays = overlays with { Credits = credits };
        if (Option(args, "--end-card") is { } card) overlays = overlays with { EndCard = (overlays.EndCard ?? new EndCardSpec()) with { Text = card } };
        if (Option(args, "--end-card-seconds") is { } seconds)
        {
            if (overlays.EndCard is null
                || !double.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double time))
            {
                stderr.WriteLine("error: --end-card-seconds takes a number of seconds, and goes with an end card (--end-card <text>).");
                return false;
            }
            overlays = overlays with { EndCard = overlays.EndCard with { Seconds = time } };
        }
        var captions = new List<CaptionSpec>();
        foreach (string line in Options(args, "--caption"))
        {
            if (!Subtitles.TryParseCaption(line, out var caption))
            {
                stderr.WriteLine($"error: --caption takes \"<start>-<end>:<text>\" with the times in seconds (\"2.5-6:Sala Mare\"), not '{line}'.");
                return false;
            }
            captions.Add(caption);
        }
        if (captions.Count > 0) overlays = overlays with { Captions = [.. overlays.Captions, .. captions] };
        if (!ReferenceEquals(overlays, spec.Labels.Overlays))
            spec = spec with { Labels = spec.Labels with { Overlays = overlays } };
        return true;
    }

    /// <summary>The values of every occurrence of an option.</summary>
    private static IEnumerable<string> Options(IReadOnlyList<string> args, string name)
    {
        for (int i = 1; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                yield return args[i + 1];
    }

    /// <summary><c>--parts auto|on|off</c>: the walls in parts, for a large cave by default.</summary>
    private static bool TryParts(IReadOnlyList<string> args, TextWriter stderr, out WallPartsMode parts)
    {
        parts = WallPartsMode.Auto;
        if (Option(args, "--parts") is not { } text) return true;
        if (Enum.TryParse(text, ignoreCase: true, out parts) && Enum.IsDefined(parts) && !int.TryParse(text, out _)) return true;
        stderr.WriteLine($"error: --parts takes auto, on or off, not '{text}'.");
        return false;
    }

    private static bool Flag(IReadOnlyList<string> args, string name)
    {
        for (int i = 2; i < args.Count; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string? Option(IReadOnlyList<string> args, string name)
    {
        for (int i = 1; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
