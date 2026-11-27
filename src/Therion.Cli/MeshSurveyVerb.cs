// `therion-cli mesh-survey` — a survey derived from a 3-D model (STL, PLY, OBJ, glb, XYZ) that has
// none: `inspect` (size, guesses, holes, loops, memory), `check-leg` (does a straight leg between two
// points stay in the passage), `run` (the derived .th and its report). Output is numbers and verdicts;
// the derived survey goes to the files.

using System.Globalization;
using Therion.Cave3D;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.Workspace.Import;

namespace Therion.Cli;

public static class MeshSurveyVerb
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public const string Usage =
        "mesh-survey inspect <model> [input options]\n" +
        "mesh-survey check-leg <model> --from x,y,z --to x,y,z [--clearance m] [--source-frame] [input options]\n" +
        "mesh-survey run <model> --out <cave.th> [--overwrite] [--preset name] [--options file.json] [--guide file]\n" +
        "            [--start x,y,z] [--only-given] [--on-blocked error|warn|drop]\n" +
        "            [--preferred m] [--min m] [--max m] [--max-vertical m] [--strategy preferred|fewest|uniform|features]\n" +
        "            [--clearance m] [--anchor axial|floor|ceiling|wall] [--max-depth n] [--max-laterals n] [--budget m]\n" +
        "            [--coverage 0..1] [--min-feature m] [--loops close|ifcheap|never] [--no-side-stations]\n" +
        "            [--all-longer-than m]\n" +
        "            [--splays none|lrud|ring|sphere|adaptive|walluniform[:count]] [--chambers hub|axis|perimeter|keep]\n" +
        "            [--voxel m] [--min-width m] [--pillar m] [--noise] [--seed n] [--naming sequential|branch]\n" +
        "            [--stations-per-survey n] [--cartesian] [--debug]\n" +
        "  input options: [--units auto|m|cm|mm|ft] [--up auto|z|y] [--north deg] [--cs CS]";

    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        if (args.Count < 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            stderr.WriteLine("usage: therion-cli " + Usage.Replace("\n", "\n       therion-cli ", StringComparison.Ordinal));
            return 2;
        }
        string sub = args[1];
        string model = args[2];
        if (!File.Exists(model)) { stderr.WriteLine($"error: no such file: {model}"); return 1; }
        var rest = args.Skip(3).ToList();
        try
        {
            return sub switch
            {
                "inspect" => Inspect(model, rest, stdout, stderr),
                "check-leg" => CheckLeg(model, rest, stdout, stderr),
                "run" => RunPlan(model, rest, stdout, stderr, ct),
                _ => Fail(stderr, $"unknown subcommand '{sub}' (inspect, check-leg, run).", 2),
            };
        }
        catch (UsageException ex) { return Fail(stderr, ex.Message, 2); }
        catch (GuideRejectedException ex) { return Fail(stderr, ex.Message, 1); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Therion.Cave3D.Parsing.CaveFileFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(stderr, ex.Message, 1);
        }
    }

    private sealed class UsageException(string message) : Exception(message);

    private static int Fail(TextWriter stderr, string message, int code)
    {
        stderr.WriteLine("error: " + message);
        return code;
    }

    // ---------------------------------------------------------------- argument helpers

    private sealed class Args(List<string> list)
    {
        private readonly List<string> _list = list;
        private readonly HashSet<int> _used = [];

        public bool Flag(string name)
        {
            int i = _list.IndexOf(name);
            if (i < 0) return false;
            _used.Add(i);
            return true;
        }

        public string? Value(string name)
        {
            int i = _list.IndexOf(name);
            if (i < 0) return null;
            if (i + 1 >= _list.Count) throw new UsageException($"{name} needs a value.");
            _used.Add(i);
            _used.Add(i + 1);
            return _list[i + 1];
        }

        public double? Number(string name)
        {
            var v = Value(name);
            if (v is null) return null;
            if (!double.TryParse(v, NumberStyles.Float, C, out double d)) throw new UsageException($"{name} expects a number, not '{v}'.");
            return d;
        }

        public int? Integer(string name)
        {
            var v = Value(name);
            if (v is null) return null;
            if (!int.TryParse(v, NumberStyles.Integer, C, out int d)) throw new UsageException($"{name} expects a whole number, not '{v}'.");
            return d;
        }

        public CaveVector3? Point(string name)
        {
            var v = Value(name);
            if (v is null) return null;
            var parts = v.Split(',');
            if (parts.Length != 3 || !parts.All(p => double.TryParse(p, NumberStyles.Float, C, out _)))
                throw new UsageException($"{name} expects x,y,z (metres), not '{v}'.");
            return new CaveVector3(double.Parse(parts[0], C), double.Parse(parts[1], C), double.Parse(parts[2], C));
        }

        public T? Enum<T>(string name) where T : struct, System.Enum
        {
            var v = Value(name);
            if (v is null) return null;
            if (System.Enum.TryParse<T>(v.Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out var e)) return e;
            throw new UsageException($"{name}: '{v}' is not one of {string.Join(", ", System.Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");
        }

        public void EnsureAllUsed()
        {
            for (int i = 0; i < _list.Count; i++)
                if (!_used.Contains(i)) throw new UsageException($"unknown option '{_list[i]}'.");
        }
    }

    private static InputOptions Input(Args a, InputOptions input)
    {
        var units = a.Value("--units");
        if (units is not null)
            input = input with
            {
                Units = units.ToLowerInvariant() switch
                {
                    "auto" => ModelLengthUnit.Auto,
                    "m" or "metres" or "meters" => ModelLengthUnit.Metres,
                    "cm" => ModelLengthUnit.Centimetres,
                    "mm" => ModelLengthUnit.Millimetres,
                    "ft" or "feet" => ModelLengthUnit.Feet,
                    _ => throw new UsageException($"--units: '{units}' is not auto, m, cm, mm or ft."),
                },
            };
        if (a.Enum<ModelUpAxisChoice>("--up") is { } up) input = input with { UpAxis = up };
        if (a.Number("--north") is { } north) input = input with { NorthRotation = north };
        if (a.Value("--cs") is { } cs) input = input with { CoordinateSystem = cs };
        return input;
    }

    // ---------------------------------------------------------------- inspect

    private static int Inspect(string model, List<string> rest, TextWriter stdout, TextWriter stderr)
    {
        var a = new Args(rest);
        var input = Input(a, new InputOptions());
        a.EnsureAllUsed();
        var session = MeshSurveySession.Open(model, input);
        var r = session.Inspect(new MeshSurveyOptions { Input = input });
        var n = r.Normalization;
        stdout.WriteLine($"model        {Path.GetFileName(model)} ({(r.IsPointCloud ? $"{r.Points:N0} points" : $"{r.Triangles:N0} triangles, {r.Points:N0} vertices")})");
        stdout.WriteLine($"units        {n.Units.ToString().ToLowerInvariant()}{(n.UnitsGuessed ? " (guessed)" : "")}; up axis {n.UpAxis.ToString().ToLowerInvariant()}; north {F(n.NorthRotation, 1)}°");
        stdout.WriteLine($"size         {F(r.Size.X, 1)} × {F(r.Size.Y, 1)} × {F(r.Size.Z, 1)} m (east × north × up)");
        if (!r.IsPointCloud)
        {
            stdout.WriteLine($"surface      {F(r.SurfaceArea, 1)} m²; enclosed volume ≈ {F(r.EnclosedVolume, 1)} m³");
            stdout.WriteLine($"holes        {r.Holes}{(r.Holes > 0 ? $" (largest {F(r.LargestHoleArea, 2)} m², the likely entrance)" : "")}");
            stdout.WriteLine($"loops        {(r.ClosedSurfaceLoops is { } l ? l.ToString(C) : "unknown until the void is built (the mesh has holes)")}");
            stdout.WriteLine($"welding      {n.VerticesIn:N0} → {n.VerticesAfterWeld:N0} vertices; {n.DegenerateTrianglesDropped:N0} degenerate triangles dropped; {n.TrianglesFlipped:N0} flipped");
        }
        stdout.WriteLine($"void         voxel {F(r.VoxelSize, 3)} m, ≈ {r.EstimatedVoxels:N0} voxels, ≈ {r.EstimatedBytes / (1024.0 * 1024):N0} MiB");
        foreach (var w in n.Warnings.Concat(r.Warnings)) stdout.WriteLine("warning      " + w);
        return 0;
    }

    // ---------------------------------------------------------------- check-leg

    private static int CheckLeg(string model, List<string> rest, TextWriter stdout, TextWriter stderr)
    {
        var a = new Args(rest);
        var input = Input(a, new InputOptions());
        var from = a.Point("--from") ?? throw new UsageException("check-leg needs --from x,y,z.");
        var to = a.Point("--to") ?? throw new UsageException("check-leg needs --to x,y,z.");
        double clearance = a.Number("--clearance") ?? new LegOptions().Clearance;
        bool source = a.Flag("--source-frame");
        a.EnsureAllUsed();
        var session = MeshSurveySession.Open(model, input);
        if (source) { from = session.Model.SourceToModel(from); to = session.Model.SourceToModel(to); }
        var check = session.CheckLeg(from, to, new LegCheckOptions { Clearance = clearance });
        stdout.WriteLine($"{check.Status.ToString().ToLowerInvariant()}: {check.Describe()}");
        stdout.WriteLine($"length {F(check.Length, 2)} m; least clearance {(double.IsInfinity(check.MinClearance) ? "> 2 m" : F(check.MinClearance, 2) + " m")} at {F(check.MinClearanceAt, 2)} m");
        return check.IsUsable ? 0 : 1;
    }

    // ---------------------------------------------------------------- run

    private static int RunPlan(string model, List<string> rest, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var a = new Args(rest);
        var preset = a.Value("--preset");
        var options = preset is null ? new MeshSurveyOptions() : MeshSurveyPresets.Get(preset)
            ?? throw new UsageException($"--preset: '{preset}' is not one of {string.Join(", ", MeshSurveyPresets.Names)}.");
        if (a.Value("--options") is { } optionsFile)
        {
            if (preset is not null) throw new UsageException("give --preset or --options, not both (an options file is a whole set of settings).");
            options = MeshSurveyJson.ReadOptions(File.ReadAllText(optionsFile));
        }
        options = options with { Input = Input(a, options.Input) };
        string output = a.Value("--out") ?? throw new UsageException("run needs --out <cave.th>.");
        bool overwrite = a.Flag("--overwrite");
        bool debug = a.Flag("--debug");

        var legs = options.Legs;
        if (a.Number("--preferred") is { } p) legs = legs with { Preferred = p, Max = Math.Max(legs.Max, p) };
        if (a.Number("--min") is { } min) legs = legs with { Min = min };
        if (a.Number("--max") is { } max) legs = legs with { Max = max };
        if (a.Number("--max-vertical") is { } mv) legs = legs with { MaxVertical = mv };
        if (a.Enum<LegStrategy>("--strategy") is { } strategy) legs = legs with { Strategy = strategy };
        if (a.Number("--clearance") is { } clearance) legs = legs with { Clearance = clearance };
        options = options with { Legs = legs };
        if (a.Enum<AnchorMode>("--anchor") is { } anchor) options = options with { Anchor = options.Anchor with { Mode = anchor } };
        var lat = options.Laterals;
        if (a.Integer("--max-depth") is { } depth) lat = lat with { MaxDepth = depth };
        if (a.Integer("--max-laterals") is { } count) lat = lat with { MaxCount = count };
        if (a.Number("--budget") is { } budget) lat = lat with { MaxTotalLength = budget };
        if (a.Enum<LoopPolicy>("--loops") is { } loops) lat = lat with { Loops = loops };
        if (a.Flag("--no-side-stations")) lat = lat with { SideStations = false };
        if (a.Number("--all-longer-than") is { } longer) lat = lat with { SurveyAllLongerThan = longer };
        options = options with { Laterals = lat };
        if (a.Number("--coverage") is { } target) options = options with { Coverage = options.Coverage with { Target = target } };
        if (a.Number("--min-feature") is { } feature) options = options with { Coverage = options.Coverage with { MinFeature = feature } };
        if (a.Value("--splays") is { } splays)
        {
            var parts = splays.Split(':');
            if (!Enum.TryParse<SplayMode>(parts[0], true, out var mode)) throw new UsageException($"--splays: '{parts[0]}' is not a splay mode.");
            var so = options.Splays with { Mode = mode };
            if (parts.Length > 1)
            {
                if (!int.TryParse(parts[1], NumberStyles.Integer, C, out int n)) throw new UsageException($"--splays: '{parts[1]}' is not a count.");
                so = so with { Count = n };
            }
            options = options with { Splays = so };
        }
        if (a.Enum<ChamberPolicy>("--chambers") is { } chambers) options = options with { Skeleton = options.Skeleton with { ChamberPolicy = chambers } };
        if (a.Number("--voxel") is { } voxel) options = options with { Void = options.Void with { VoxelSize = voxel } };
        if (a.Number("--min-width") is { } width) options = options with { Void = options.Void with { MinPassageWidth = width } };
        if (a.Number("--pillar") is { } pillar) options = options with { Void = options.Void with { MinPillarSize = pillar } };
        if (a.Flag("--noise")) options = options with { Readings = options.Readings with { Noise = options.Readings.Noise ?? new NoiseOptions() } };
        if (a.Flag("--cartesian")) options = options with { Readings = options.Readings with { Style = ReadingStyle.Cartesian } };
        if (a.Integer("--seed") is { } seed) options = options with { Seed = seed };
        if (a.Enum<StationNaming>("--naming") is { } naming) options = options with { Output = options.Output with { Naming = naming } };
        if (a.Integer("--stations-per-survey") is { } per) options = options with { Output = options.Output with { StationsPerSurvey = per } };

        SurveyGuide guide = SurveyGuide.None;
        IReadOnlyList<string> guideWarnings = [];
        if (a.Value("--guide") is { } guideFile) (guide, guideWarnings) = MeshSurveyImport.ReadGuide(guideFile);
        if (a.Point("--start") is { } start) guide = guide with { Start = new GuidePoint { Name = "start", Position = start } };
        if (a.Flag("--only-given")) guide = guide with { Mode = GuideMode.OnlyGiven };
        if (a.Enum<GuideBlockedPolicy>("--on-blocked") is { } blocked) guide = guide with { OnBlocked = blocked };
        a.EnsureAllUsed();

        var issues = options.Validate();
        if (issues.Count > 0) throw new UsageException(string.Join(" ", issues));
        if (!Path.GetExtension(output).Equals(".th", StringComparison.OrdinalIgnoreCase)) throw new UsageException("--out must be a .th file.");
        if (File.Exists(output) && !overwrite) return Fail(stderr, $"{output} exists; pass --overwrite to replace it.", 1);
        foreach (var w in guideWarnings) stderr.WriteLine("warning: " + w);

        var result = MeshSurveyImport.Run(model, options, guide, null, ct, guideWarnings);
        File.WriteAllText(output, result.Therion);
        File.WriteAllText(MeshSurveyImport.ReportPath(output), result.ReportJson);
        var written = new List<string> { output, MeshSurveyImport.ReportPath(output) };
        if (debug) written.AddRange(MeshSurveyImport.WriteDebugLayers(result, output));

        var plan = result.Survey.Report.Plan;
        stdout.WriteLine($"stations     {plan.Stations}; legs {plan.Legs} ({F(plan.Length, 1)} m; mainline {F(plan.MainlineLength, 1)} m)");
        stdout.WriteLine($"laterals     {plan.Laterals}; loop closures {plan.LoopClosures}; side stations {plan.SideStations}; parts {plan.Components}");
        stdout.WriteLine($"legs         {F(plan.LegMin, 2)} … {F(plan.LegMax, 2)} m, median {F(plan.LegMedian, 2)} m");
        stdout.WriteLine($"coverage     {F(plan.CoverageReached * 100, 1)} % of the significant {(plan.CoverageMeasure == CoverageMeasure.Volume ? "void" : "wall")} (target {F(plan.CoverageTarget * 100, 0)} %); stopped: {plan.StopReason}");
        stdout.WriteLine($"splays       {result.Survey.Splays.Count}");
        if (plan.BlockedLegs > 0 || plan.TightLegs > 0) stdout.WriteLine($"checks       {plan.BlockedLegs} blocked, {plan.TightLegs} tight legs (see the report)");
        if (plan.Relaxations.Count > 0) stdout.WriteLine($"relaxed      {plan.Relaxations.Count} places (see the report)");
        foreach (var w in plan.Warnings) stdout.WriteLine("warning      " + w);
        foreach (var f in written) stdout.WriteLine("wrote        " + f);
        return 0;
    }

    private static string F(double v, int decimals) => v.ToString("F" + decimals.ToString(C), C);
}
