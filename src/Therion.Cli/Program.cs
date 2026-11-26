// headless CLI parity. Commands reuse the same parser / semantic / workspace engines as
// the GUI so CI can validate, lint, format, stat, graph dependencies and export without a desktop.

using System.Text.Json;
using Therion.Core;
using Therion.Semantics;
using Therion.Structural;
using Therion.Syntax;
using Therion.Workspace;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

switch (args[0])
{
    case "--version":
        Console.WriteLine(TherionSyntaxVersion.Default);
        return 0;

    case "validate":      return RequireArg(args, Validate);
    case "dump-ast":      return RequireArg(args, DumpAst);
    case "list-stations": return RequireArg(args, ListStations);
    case "format":        return Therion.Cli.FormatVerb.Run(args, Console.Out, Console.Error);
    case "lint":          return await RequireArgAsync(args, Lint);
    case "stats":         return await RequireArgAsync(args, Stats);
    case "deps":          return await RequireArgAsync(args, a => Deps(a, args));
    case "gis":           return await RequireArgAsync(args, a => Gis(a, args));
    case "structural":    return RequireArg(args, a => Structural(a, args));
    case "blender":       return await Therion.Cli.BlenderVerb.RunAsync(args, Console.Out, Console.Error);
    case "new":           return Therion.Cli.NewVerb.Run(args, Console.Out, Console.Error);
    case "import":        return Therion.Cli.ImportVerb.Run(args, Console.Out, Console.Error);
    case "report":        return await Therion.Cli.ReportVerb.RunAsync(args, Console.Out, Console.Error);
    case "build":
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        return await Therion.Cli.BuildVerb.RunAsync(args, Console.Out, Console.Error, ct: cancel.Token);
    }

    default:
        Console.Error.WriteLine($"error: unknown command '{args[0]}'. Use --help.");
        return 2;
}

static void PrintHelp()
{
    Console.WriteLine("therion-cli — Therion project tools");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  therion-cli validate <file>            Parse a file and print diagnostics.");
    Console.WriteLine("  therion-cli lint <file|thconfig>       Project-wide semantic + correctness checks.");
    Console.WriteLine("  therion-cli format <file> [--indent N|tab]   Print the file re-indented (the editor's Format Document).");
    Console.WriteLine("  therion-cli format <file|folder>… --write|--check [--indent N|tab]");
    Console.WriteLine("                                         Re-indent in place, or list files that would change (exit 1 on drift).");
    Console.WriteLine("  therion-cli stats <file|thconfig>      Print project statistics (length, stations, …).");
    Console.WriteLine("  therion-cli deps <file|thconfig> [--dot]   Print the include/dependency graph.");
    Console.WriteLine("  therion-cli gis <file|thconfig> [--format kml|geojson|gpx|csv] [--out <path>]");
    Console.WriteLine("  therion-cli structural <file.th> [--keyword geo] [--declination <deg>|survey] [--format table|csv]");
    Console.WriteLine("                                         Fit geological planes (strike/dip) from 'geo' shots.");
    Console.WriteLine("  therion-cli blender <model.lox|.3d> [--preset \"<name>\" | --spec <spec.json>] [--out <dir>] [--export <dir>] [--blender <path>]");
    Console.WriteLine("                     [--draft] [--builder auto|player|generator] [--keep-frames] [--compose-only] [--frames <dir>]");
    Console.WriteLine("                     [--title <text>] [--credits <text>] [--end-card <text> [--end-card-seconds <n>]] [--caption \"<start>-<end>:<text>\"]…");
    Console.WriteLine("                     [--annotations <file.json>]… [--reveal date|order|distance [--reveal-from <station>] [--reveal-dates <file.3d>]");
    Console.WriteLine("                     [--reveal-seconds <start>-<end>]] [--hdri <image.hdr|.exr> [--hdri-rotation <degrees>] [--hdri-strength <n>]]");
    Console.WriteLine("                     [--leads <file.json>]… [--lamp-reach] [--route <from>[:<via>…]:<to> [--route-seconds <n>]]");
    Console.WriteLine("                                         Render (or --export) a Blender presentation of a 3D model, headless. --draft renders a quick");
    Console.WriteLine("                                         preview instead. A render with texts over the picture takes two stages: its frames, then the");
    Console.WriteLine("                                         movie; run again after it was stopped, it goes on. --compose-only writes the movie again from");
    Console.WriteLine("                                         the frames (kept with --keep-frames) after a text changed.");
    Console.WriteLine("                                         --annotations adds labels, callouts, markers and photos from a JSON list, each at a station");
    Console.WriteLine("                                         or a point, for a time or while the camera is near; it goes with --bundle too.");
    Console.WriteLine("                                         --reveal shows the cave part by part while the clip runs: by the dates of its survey (with a");
    Console.WriteLine("                                         counter of the years; the dates are those of a .3d: the model itself, one of the same name");
    Console.WriteLine("                                         beside a .lox, or --reveal-dates), by the survey's order, or by the distance from its");
    Console.WriteLine("                                         entrances (or from --reveal-from). It goes with --bundle too; only the player draws it.");
    Console.WriteLine("                                         --hdri lights the scene by an image all round it (a Radiance .hdr or an OpenEXR .exr file), which");
    Console.WriteLine("                                         is the background too; --hdri-rotation turns it about the vertical (anticlockwise seen from");
    Console.WriteLine("                                         above) and --hdri-strength makes it brighter or dimmer. A bundle carries the image.");
    Console.WriteLine("                                         The lead markers of a preset or spec stand on the stations the model flags as continuations");
    Console.WriteLine("                                         (a .lox file has them); --leads names a JSON list of stations to mark instead, each a name or");
    Console.WriteLine("                                         {\"station\", \"note\"}. --lamp-reach lights the scene by the lamp on the camera and turns it up");
    Console.WriteLine("                                         with the distance the camera looks over, so a cave seen from far away is not dark.");
    Console.WriteLine("                                         --route flies the camera along the passages from one station to another (\"entrance\" and");
    Console.WriteLine("                                         \"farthest\" are the model's own), stopping at the stations between; its speed follows the size");
    Console.WriteLine("                                         of the passage and the route sets the clip's length, or --route-seconds gives one to fit.");
    Console.WriteLine("  therion-cli blender <model.lox|.3d> [--preset \"<name>\" | --spec <spec.json>] --bundle <file.zip|folder> [--draft]");
    Console.WriteLine("                     [--render [--render-plan full|draft|both]] [--out <dir>] [--blender <path>] [--readme-language en|ro]");
    Console.WriteLine("                                         Write a render bundle (scene plan, mesh, player, run scripts) to render with any Blender 4.2+;");
    Console.WriteLine("                                         --draft adds a quick draft plan, --render renders the bundle here: its draft when it has one,");
    Console.WriteLine("                                         else the full plan, or what --render-plan names. --readme-language writes the bundle's");
    Console.WriteLine("                                         README in Romanian (ro) instead of English.");
    Console.WriteLine("  therion-cli blender <model.lox|.3d> --storyboard <file.json> [--shot <n>] [--draft] [--out <dir>] [--keep-frames] [--compose-only]");
    Console.WriteLine("                     [--frames <dir>] [--bundle <file.zip|folder> [--render [--render-plan full|draft|both]]] [--blender <path>]");
    Console.WriteLine("                                         Render a film of several shots from a storyboard: every shot's frames, each in a folder of");
    Console.WriteLine("                                         its own, then the film from them, joined with cuts, dips and dissolves, with its texts over");
    Console.WriteLine("                                         it. Run again after it was stopped, it goes on with the shot it stopped in; a shot that was");
    Console.WriteLine("                                         changed is rendered again alone. --draft drafts every shot: an animatic of the film.");
    Console.WriteLine("                                         --shot renders one shot alone, as any scene is rendered. --bundle writes the film as a");
    Console.WriteLine("                                         render bundle: the plan of every shot and of the movie, with one mesh for all of them.");
    Console.WriteLine("  therion-cli blender <model.lox|.3d> --tour [--tour-file <tour.json>] [--trunk auto|longest|farthest|deepest|through|via]");
    Console.WriteLine("                     [--tour-start <station>] [--tour-via <station>]… [--tour-end <station>] [--tour-seconds <n>] [--branch-seconds <n>]");
    Console.WriteLine("                     [--branch-mode cut|return|peek|skip] [--branch <id>=<mode>]… [--peek-short] [--min-length <m>] [--max-branches <n>]");
    Console.WriteLine("                     [--no-lead-branches] [--include <id|station|survey>]… [--exclude <…>]… [--tour-depth <n>] [--tour-language en|ro]");
    Console.WriteLine("                     [--preset \"<name>\"] [--leads <file.json>] [--write-storyboard <file.json>] [--draft] [--bundle <file.zip|folder>]");
    Console.WriteLine("                                         A tour of the cave's branches: the main passage from the entrance to the farthest station,");
    Console.WriteLine("                                         with the side passages worth a visit flown into at their junctions (B1, B2 … in the table");
    Console.WriteLine("                                         it prints), within 180 s for the film and 20 s for each. It is made as a storyboard and");
    Console.WriteLine("                                         rendered, drafted or bundled like one; --write-storyboard keeps the storyboard, and alone");
    Console.WriteLine("                                         writes it without rendering, to edit by hand and render with --storyboard.");
    Console.WriteLine("  therion-cli build <thconfig|folder> [--all] [--therion <path>] [--quiet] [--no-log] [--json] [--stop-on-failure]");
    Console.WriteLine("                                         Compile with Therion and print a summary; exit 0 only if all succeed.");
    Console.WriteLine("  therion-cli new --list                 List project templates (built-in and imported).");
    Console.WriteLine("  therion-cli new <id|folder> --out <dir> [--var key=value]…");
    Console.WriteLine("                                         Create a project from a template.");
    Console.WriteLine("  therion-cli import <file.svx|.dat|.gpx|.csv|.tsv> [--out <new.th>] [--format compass|survex|gpx|csv] [--survey <name>]");
    Console.WriteLine("                     [--columns \"from to length compass clino\"] [--start <station>] [--length-unit m|ft] [--angle-unit deg|grad] [--merge N]");
    Console.WriteLine("                                         Convert another program's survey (or a shot table) to Therion; never overwrites.");
    Console.WriteLine("  therion-cli report <file|thconfig|folder> [--out <report.html>] [--title <name>]");
    Console.WriteLine("                                         Write the HTML survey report (stdout without --out).");
    Console.WriteLine("  therion-cli dump-ast <file>            Print the parsed AST as JSON.");
    Console.WriteLine("  therion-cli list-stations <file>       List station references in a .th file.");
    Console.WriteLine("  therion-cli --version                  Print the pinned Therion syntax version.");
}

static int RequireArg(string[] args, Func<string, int> run)
{
    if (args.Length < 2) { Console.Error.WriteLine($"error: '{args[0]}' requires a file path."); return 2; }
    return run(args[1]);
}

static async System.Threading.Tasks.Task<int> RequireArgAsync(string[] args, Func<string, System.Threading.Tasks.Task<int>> run)
{
    if (args.Length < 2) { Console.Error.WriteLine($"error: '{args[0]}' requires a file path."); return 2; }
    return await run(args[1]);
}

static int Validate(string path)
{
    // .xvi has its own AST (not a TherionFile); validate it directly.
    if (Path.GetExtension(Path.GetFullPath(path)).ToLowerInvariant() == ".xvi")
    {
        if (!File.Exists(path)) { Console.Error.WriteLine($"error: file not found: {path}"); return 2; }
        var xtext = EncodingResolver.ReadAllText(Path.GetFullPath(path));
        var xr = new XviParser().Parse(Path.GetFullPath(path), xtext);
        var xfmt = new RustcStyleDiagnosticFormatter();
        foreach (var d in xr.Diagnostics) Console.Write(xfmt.Format(d));
        Console.WriteLine();
        Console.WriteLine($"{xr.Value!.Stations.Length} station(s), {xr.Value.Shots.Length} shot(s), " +
            $"{xr.Diagnostics.Length} diagnostic(s).");
        foreach (var d in xr.Diagnostics)
            if (d.Severity == DiagnosticSeverity.Error) return 1;
        return 0;
    }

    var (file, diagnostics) = ParseAny(path);
    if (file is null) return 2;

    // For .th files also run the semantic binder so cross-cutting checks (data-row arity,
    // unresolved stations, …) surface from the CLI too (parity).
    var all = diagnostics;
    var ext = Path.GetExtension(Path.GetFullPath(path)).ToLowerInvariant();
    if (ext is ".th")
    {
        var model = new SemanticBinder().Bind(file);
        all = diagnostics.AddRange(model.Diagnostics);
    }

    var formatter = new RustcStyleDiagnosticFormatter();
    foreach (var d in all)
        Console.Write(formatter.Format(d));

    Console.WriteLine();
    Console.WriteLine($"{file.Children.Length} top-level node(s), {all.Length} diagnostic(s).");

    foreach (var d in all)
        if (d.Severity == DiagnosticSeverity.Error) return 1;
    return 0;
}

// project-wide lint — per-file semantic diagnostics + cross-file correctness analysis
// (loop closure, blunders, fore/back, collisions, dangling refs) over the whole include graph.
static async System.Threading.Tasks.Task<int> Lint(string path)
{
    var ws = await LoadWorkspaceAsync(path);
    if (ws is null) return 2;

    var diags = System.Collections.Immutable.ImmutableArray.CreateBuilder<Diagnostic>();
    foreach (var model in ws.PerFile.Values) diags.AddRange(model.Diagnostics);
    try { diags.AddRange(ProjectDiagnostics.Analyze(ws, null, File.Exists)); } catch { /* best-effort */ }

    var formatter = new RustcStyleDiagnosticFormatter();
    int errors = 0, warnings = 0;
    foreach (var d in diags)
    {
        Console.Write(formatter.Format(d));
        if (d.Severity == DiagnosticSeverity.Error) errors++;
        else if (d.Severity == DiagnosticSeverity.Warning) warnings++;
    }
    Console.WriteLine();
    Console.WriteLine($"{ws.PerFile.Count} file(s) · {errors} error(s) · {warnings} warning(s).");
    return errors > 0 ? 1 : 0;
}

// project statistics (length, station/shot/survey counts, vertical range, entrances).
static async System.Threading.Tasks.Task<int> Stats(string path)
{
    var ws = await LoadWorkspaceAsync(path);
    if (ws is null) return 2;
    var t = ProjectStatistics.ComputeTotals(ws);
    Console.WriteLine($"Surveys:        {t.SurveyCount}");
    Console.WriteLine($"Stations:       {t.StationCount}");
    Console.WriteLine($"Shots:          {t.ShotCount}");
    Console.WriteLine($"Total length:   {t.TotalLength:0.0} m");
    Console.WriteLine($"Vertical range: {t.VerticalRange:0.0} m");
    Console.WriteLine($"Entrances:      {t.EntranceCount}");
    Console.WriteLine($"Fixed points:   {t.FixedCount}");
    return 0;
}

// include/dependency graph (thconfig → source → input). Text or Graphviz DOT.
static async System.Threading.Tasks.Task<int> Deps(string path, string[] args)
{
    var ws = await LoadWorkspaceAsync(path);
    if (ws is null) return 2;

    if (HasFlag(args, "--dot"))
    {
        Console.WriteLine("digraph deps {");
        foreach (var (from, to, _) in ws.FileGraphEdges)
            Console.WriteLine($"  \"{Path.GetFileName(from)}\" -> \"{Path.GetFileName(to)}\";");
        Console.WriteLine("}");
    }
    else
    {
        foreach (var (from, to, _) in ws.FileGraphEdges)
            Console.WriteLine($"{Path.GetFileName(from)} -> {Path.GetFileName(to)}");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"{ws.FileGraphEdges.Length} edge(s).");
    }
    return 0;
}

// export entrances / fixed points to KML / GeoJSON / GPX / CSV.
static async System.Threading.Tasks.Task<int> Gis(string path, string[] args)
{
    var ws = await LoadWorkspaceAsync(path);
    if (ws is null) return 2;

    var fmt = (GetOption(args, "--format") ?? "csv").ToLowerInvariant() switch
    {
        "kml" => GisFormat.Kml,
        "geojson" => GisFormat.GeoJson,
        "gpx" => GisFormat.Gpx,
        _ => GisFormat.Csv,
    };
    var text = GisExport.Export(ws, fmt);
    var outPath = GetOption(args, "--out");
    if (!string.IsNullOrEmpty(outPath)) { File.WriteAllText(outPath, text); Console.Error.WriteLine($"wrote {outPath}"); }
    else Console.Write(text);
    return 0;
}

static int DumpAst(string path)
{
    var (file, _) = ParseAny(path);
    if (file is null) return 2;
    Console.WriteLine(JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static int ListStations(string path)
{
    var (file, _) = ParseAny(path);
    if (file is null) return 2;

    var stations = new SortedSet<string>(StringComparer.Ordinal);
    Walk(file, stations);

    foreach (var s in stations) Console.WriteLine(s);
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{stations.Count} station(s).");
    return 0;

    static void Walk(TherionNode node, SortedSet<string> acc)
    {
        switch (node)
        {
            case StationFix fix:
                if (!string.IsNullOrEmpty(fix.Station)) acc.Add(fix.Station);
                break;
            case EquateCommand eq:
                foreach (var s in eq.Stations) acc.Add(s);
                break;
        }

        if (node is TherionFile f)
            foreach (var c in f.Children) Walk(c, acc);
        else if (node is BlockCommand b)
            foreach (var c in b.Children) Walk(c, acc);
    }
}

// Loads a project (a thconfig or a .th) into a workspace model, following the include graph.
// The File.Exists guard stays here rather than leaning on WorkspaceLoader's own resolution: the CLI
// takes a file, never a folder, and this keeps its two distinct error messages.
static async System.Threading.Tasks.Task<WorkspaceSemanticModel?> LoadWorkspaceAsync(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"error: file not found: {path}"); return null; }
    try
    {
        var ws = await WorkspaceLoader.OpenAsync(path);
        return ws.BuildSemanticModel();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: failed to load '{path}': {ex.Message}");
        return null;
    }
}

static bool HasFlag(string[] args, string flag) =>
    Array.Exists(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

// fit geological planes (strike/dip) from structural shots, headless (proves the
// Therion.Structural core is UI-independent).
static int Structural(string path, string[] args)
{
    var ext = Path.GetExtension(Path.GetFullPath(path)).ToLowerInvariant();
    if (ext != ".th") { Console.Error.WriteLine("error: 'structural' expects a .th file."); return 2; }

    var (file, _) = ParseAny(path);
    if (file is null) return 2;

    var model = new SemanticBinder().Bind(file);

    var detection = new DetectionOptions();
    if (GetOption(args, "--keyword") is { Length: > 0 } kw)
        detection = detection with { NameKeywords = System.Collections.Immutable.ImmutableArray.Create(kw) };

    var declination = new DeclinationOptions();
    var declInputs = default(DeclinationInputs);
    if (GetOption(args, "--declination") is { Length: > 0 } ds)
    {
        if (string.Equals(ds, "survey", StringComparison.OrdinalIgnoreCase))
        {
            declination = new DeclinationOptions { Source = DeclinationSource.SurveyDeclared };
            declInputs = new DeclinationInputs(SurveyDeclaredDegrees: model.Declination);
        }
        else if (double.TryParse(ds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd))
        {
            declination = new DeclinationOptions { Source = DeclinationSource.Manual, ManualDegrees = dd };
        }
    }

    var result = StructuralAnalysis.Analyze(model,
        new StructuralOptions { Detection = detection, Declination = declination, DeclinationInputs = declInputs });

    if (result.Planes.Length == 0)
    {
        Console.Error.WriteLine("No structural measurements detected (try --keyword <name>).");
        return 0;
    }

    var ci = System.Globalization.CultureInfo.InvariantCulture;
    string F(double v) => v.ToString("0.0", ci);
    var fmt = (GetOption(args, "--format") ?? "table").ToLowerInvariant();

    if (fmt == "csv")
    {
        Console.WriteLine("plane,dip,strike,dip_direction,points,rms,valid");
        for (int i = 0; i < result.Planes.Length; i++)
        {
            var p = result.Planes[i];
            Console.WriteLine($"\"{result.Batches[i].Name}\",{F(p.Dip)},{F(p.Strike)},{F(p.DipDirection)}," +
                $"{p.PointCount},{p.RmsResidual.ToString("0.###", ci)},{p.IsValid}");
        }
    }
    else
    {
        Console.WriteLine($"{"Plane",-22} {"Dip",6} {"Strike",7} {"DipDir",7} {"Pts",4} {"RMS",8}");
        for (int i = 0; i < result.Planes.Length; i++)
        {
            var p = result.Planes[i];
            var name = Trunc(result.Batches[i].Name, 22);
            Console.WriteLine(p.IsValid
                ? $"{name,-22} {F(p.Dip),6} {F(p.Strike),7} {F(p.DipDirection),7} {p.PointCount,4} {p.RmsResidual.ToString("0.###", ci),8}"
                : $"{name,-22}  ({p.ErrorReason})");
        }
        if (result.Declination.Delta != 0) Console.Error.WriteLine($"declination {F(result.Declination.Delta)}° applied (true north).");
        Console.Error.WriteLine($"{result.Planes.Length} plane(s).");
    }
    return 0;

    static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}

static string? GetOption(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static (TherionFile? File, System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics)
    ParseAny(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"error: file not found: {path}");
        return (null, System.Collections.Immutable.ImmutableArray<Diagnostic>.Empty);
    }

    var fullPath = Path.GetFullPath(path);
    var text = EncodingResolver.ReadAllText(fullPath);
    var ext = Path.GetExtension(fullPath).ToLowerInvariant();

    if (ext is ".thconfig" or ".thc" or ".thl")
    {
        // .thl = a layout-only file, written in thconfig (layout) syntax.
        var r = new ThconfigParser().Parse(fullPath, text);
        return (r.Value, r.Diagnostics);
    }
    else if (ext is ".th2")
    {
        var r = new Th2Parser().Parse(fullPath, text);
        return (r.Value, r.Diagnostics);
    }
    else
    {
        var r = new ThParser().Parse(fullPath, text);
        return (r.Value, r.Diagnostics);
    }
}
