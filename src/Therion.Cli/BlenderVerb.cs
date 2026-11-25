// `therion-cli blender` (BA-B14) — render or export a Blender presentation of a cave model,
// headless, reusing the whole Therion.Blender module; or write it as a render bundle (a scene
// plan, its mesh, the player and run scripts in one zip) and optionally render that. A render can
// be a quick draft (--draft), built by either scene builder (--builder), keep its frames
// (--keep-frames) and be written again from them after a text changed (--compose-only); a
// render in two stages that is stopped goes on when the same command is run again. Labels,
// callouts, markers and photos come from a JSON file (--annotations), and the cave can appear
// part by part (--reveal). Kept in its
// own class (not a Program.cs local function) so it is unit-testable with in-memory writers and no
// real Blender.

using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Cli;

public static class BlenderVerb
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

        if (!TryBuildSpec(args, stderr, out var spec)) return 2;

        string? bundle = Option(args, "--bundle");
        bool draft = Flag(args, "--draft"), render = Flag(args, "--render");
        string? renderPlan = Option(args, "--render-plan");
        if (bundle is null && (render || renderPlan is not null))
        {
            stderr.WriteLine("error: --render and --render-plan go with --bundle <file.zip|folder>.");
            return 2;
        }
        // How a render is made: these are for the render itself, not for a bundle or an export.
        bool keepFrames = Flag(args, "--keep-frames"), composeOnly = Flag(args, "--compose-only");
        string? builderName = Option(args, "--builder"), framesDir = Option(args, "--frames");
        bool staged = keepFrames || composeOnly || builderName is not null || framesDir is not null;
        if (staged && (bundle is not null || Option(args, "--export") is not null))
        {
            stderr.WriteLine("error: --builder, --keep-frames, --compose-only and --frames go with a render, not with --bundle or --export.");
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
            return await BundleAsync(model, spec, bundle, draft, language, plans, Option(args, "--out"), Option(args, "--blender"), stdout, stderr)
                .ConfigureAwait(false);
        }

        var renderSource = new RenderSource(
            new ResolvedModelSource { Path = Path.GetFullPath(source), Format = format, Kind = ModelSourceKind.ExternalFile },
            []);

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
                var scriptPath = await service.ExportScriptAsync(spec, renderSource, exportDir, progress).ConfigureAwait(false);
                // An exported script is the script generator's: what only the player draws is not in it.
                if (SceneBuilders.NotDrawnByTheGenerator(spec) is { Count: > 0 } notDrawn)
                    stderr.WriteLine($"warning: the exported script does not show {string.Join(", ", notDrawn)}");
                stdout.WriteLine(scriptPath);
                stderr.WriteLine($"exported script + assets to {exportDir}");
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
            };
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
    private static async Task<int> BundleAsync(
        ResolvedModelSource model, SceneSpec spec, string bundle, bool draft, string language, RenderPlans plans, string? outDir,
        string? blenderOverride, TextWriter stdout, TextWriter stderr)
    {
        bool zip = bundle.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string work = TempFolder("thide-bundle-");
        RenderBundleContent content;
        try
        {
            stderr.WriteLine($"[{RenderPhase.ConvertingGeometry}] Converting model");
            content = RenderBundleWriter.FromModel(model, spec, work, draft) with { Language = language };
            if (zip) RenderBundleWriter.WriteZip(content, bundle);
            else RenderBundleWriter.WriteFolder(content, bundle);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"error: invalid render spec: {ex.Message}");
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
        var toRender = new List<(ScenePlan Plan, string File)>();
        if (plans.HasFlag(RenderPlans.Draft)) toRender.Add((content.Draft!, RenderBundleWriter.DraftPlanFileName));
        if (plans.HasFlag(RenderPlans.Full)) toRender.Add((content.Plan, ScenePlanSerializer.FileName));
        string? destination = outDir ?? (zip ? Path.GetDirectoryName(Path.GetFullPath(bundle)) : null);
        var progress = new Progress<RenderProgress>(p => stderr.WriteLine($"[{p.Phase}] {p.Message}"));
        foreach (var (plan, file) in toRender)
        {
            // The player takes a plan with texts through both of its stages itself.
            var job = new RenderJob(
                Path.Combine(folder, PlayerFiles.EntryFileName), folder, plan.OutputFrameCount,
                PlanOutputs.SpecOf(plan, Path.GetFullPath(Path.Combine(folder, plan.Output.Directory))))
            {
                ScriptArguments = ["--plan", Path.Combine(folder, file)],
                ExtraBytes = plan.HasTexts ? PlanOutputs.FrameBytes(plan) : 0,
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
            if (ReferenceEquals(plan, content.Plan) && content.Captions.Count > 0)
                foreach (string extension in new[] { Subtitles.SrtExtension, Subtitles.VttExtension })
                {
                    string subtitles = Path.Combine(folder, RenderBundleWriter.SubtitlePath(plan, extension).Replace('/', Path.DirectorySeparatorChar));
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

    private static bool TryBuildSpec(IReadOnlyList<string> args, TextWriter stderr, out SceneSpec spec)
    {
        spec = BuiltInPresets.OrbitShowcase().Spec;

        if (Option(args, "--spec") is { } specPath)
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
        return TryAddTexts(args, stderr, ref spec) && TryAddAnnotations(args, stderr, ref spec) && TryAddReveal(args, stderr, ref spec)
            && TryAddHdri(args, stderr, ref spec);
    }

    /// <summary>
    /// The image the scene is lit by, given on the command line over the preset's or the spec's
    /// lighting: <c>--hdri &lt;image.hdr|.exr&gt;</c> makes the image the background and the
    /// light, <c>--hdri-rotation &lt;degrees&gt;</c> turns it about the vertical and
    /// <c>--hdri-strength &lt;n&gt;</c> says how brightly it shines.
    /// </summary>
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
