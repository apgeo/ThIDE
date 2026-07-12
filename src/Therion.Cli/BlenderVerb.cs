// `therion-cli blender` (BA-B14) — render or export a Blender presentation of a cave model,
// headless, reusing the whole Therion.Blender module. Kept in its own class (not a Program.cs
// local function) so it is unit-testable with in-memory writers and no real Blender.

using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Parsing;
using Therion.Blender.Presets;
using Therion.Blender.Sources;

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

        var renderSource = new RenderSource(
            new ResolvedModelSource { Path = Path.GetFullPath(source), Format = format, Kind = ModelSourceKind.ExternalFile },
            []);

        var locator = new BlenderLocator(new ProcessBlenderProbe());
        var runner = new BlenderRunner(new RealBlenderProcessLauncher());
        string? blenderOverride = Option(args, "--blender");
        string outDir = Option(args, "--out") ?? Directory.GetCurrentDirectory();
        var service = new BlenderRenderService(locator, runner, outDir, geometry: null, blenderOverridePath: blenderOverride);
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

    private static string? Option(IReadOnlyList<string> args, string name)
    {
        for (int i = 1; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
