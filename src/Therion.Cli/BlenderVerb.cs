// `therion-cli blender` (BA-B14) — render or export a Blender presentation of a cave model,
// headless, reusing the whole Therion.Blender module; or write it as a render bundle (a scene
// plan, its mesh, the player and run scripts in one zip) and optionally render that. Kept in its
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
        if (bundle is null && (draft || render || renderPlan is not null))
        {
            stderr.WriteLine("error: --draft, --render and --render-plan go with --bundle <file.zip|folder>.");
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
            return await BundleAsync(model, spec, bundle, draft, plans, Option(args, "--out"), Option(args, "--blender"), stdout, stderr)
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
                stdout.WriteLine(scriptPath);
                stderr.WriteLine($"exported script + assets to {exportDir}");
                return 0;
            }

            var result = await service.RenderAsync(spec, renderSource, progress).ConfigureAwait(false);
            if (result.Succeeded)
            {
                foreach (var path in result.OutputPaths) stdout.WriteLine(path);
                stderr.WriteLine($"rendered {result.FrameCount} frame(s) on {result.Device} in {result.Duration.TotalSeconds:0.0}s");
                return 0;
            }

            stderr.WriteLine($"error: render failed ({result.FailureKind}): {result.ErrorMessage}");
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
        ResolvedModelSource model, SceneSpec spec, string bundle, bool draft, RenderPlans plans, string? outDir,
        string? blenderOverride, TextWriter stdout, TextWriter stderr)
    {
        bool zip = bundle.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string work = TempFolder("thide-bundle-");
        RenderBundleContent content;
        try
        {
            stderr.WriteLine($"[{RenderPhase.ConvertingGeometry}] Converting model");
            content = RenderBundleWriter.FromModel(model, spec, work, draft);
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
            var job = new RenderJob(
                Path.Combine(folder, PlayerFiles.EntryFileName), folder, plan.Timeline.FrameCount,
                OutputOf(plan, Path.GetFullPath(Path.Combine(folder, plan.Output.Directory))))
            {
                ScriptArguments = ["--plan", Path.Combine(folder, file)],
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

    /// <summary>The output of a plan as the runner collects it.</summary>
    private static OutputSpec OutputOf(ScenePlan plan, string directory) => new()
    {
        Kind = plan.Output.Kind switch
        {
            PlanOutputKind.Video => OutputKind.Video,
            PlanOutputKind.Frames => OutputKind.FrameSequence,
            _ => OutputKind.Still,
        },
        Container = plan.Output.Container switch
        {
            PlanContainer.Mkv => VideoContainer.Mkv,
            PlanContainer.Webm => VideoContainer.WebM,
            _ => VideoContainer.Mp4,
        },
        Width = plan.Look.Width,
        Height = plan.Look.Height,
        BaseName = plan.Output.BaseName,
        OutputDirectory = directory,
    };

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
            return true;
        }

        if (Option(args, "--preset") is { } presetName)
        {
            var preset = BuiltInPresets.ByName(presetName);
            if (preset is null)
            {
                stderr.WriteLine($"error: unknown preset '{presetName}'. Available: {string.Join(", ", BuiltInPresets.All.Select(p => $"\"{p.Name}\""))}.");
                return false;
            }
            spec = preset.Spec;
        }
        return true;
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
