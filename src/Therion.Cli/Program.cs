// EXT-01 — headless CLI parity. Commands reuse the same parser / semantic / workspace engines as
// the GUI so CI can validate, lint, format, stat, graph dependencies and export without a desktop.

using System.Text.Json;
using Therion.Core;
using Therion.Semantics;
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
    case "format":        return RequireArg(args, a => Format(a, args));
    case "lint":          return await RequireArgAsync(args, Lint);
    case "stats":         return await RequireArgAsync(args, Stats);
    case "deps":          return await RequireArgAsync(args, a => Deps(a, args));
    case "gis":           return await RequireArgAsync(args, a => Gis(a, args));

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
    Console.WriteLine("  therion-cli format <file> [--write]    Pretty-print via TherionWriter (stdout, or in place).");
    Console.WriteLine("  therion-cli stats <file|thconfig>      Print project statistics (length, stations, …).");
    Console.WriteLine("  therion-cli deps <file|thconfig> [--dot]   Print the include/dependency graph.");
    Console.WriteLine("  therion-cli gis <file|thconfig> [--format kml|geojson|gpx|csv] [--out <path>]");
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
    // unresolved stations, …) surface from the CLI too (EXT-01 parity).
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

// EXT-01: project-wide lint — per-file semantic diagnostics + cross-file correctness analysis
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

// EXT-01: format a single file by re-emitting its AST through TherionWriter.
static int Format(string path, string[] args)
{
    var (file, _) = ParseAny(path);
    if (file is null) return 2;
    var written = new TherionWriter().Write(file);
    if (HasFlag(args, "--write"))
    {
        File.WriteAllText(Path.GetFullPath(path), written);
        Console.Error.WriteLine($"formatted {path}");
    }
    else Console.Write(written);
    return 0;
}

// EXT-01: project statistics (length, station/shot/survey counts, vertical range, entrances).
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

// EXT-01: include/dependency graph (thconfig → source → input). Text or Graphviz DOT.
static async System.Threading.Tasks.Task<int> Deps(string path, string[] args)
{
    var ws = await LoadWorkspaceAsync(path);
    if (ws is null) return 2;

    if (HasFlag(args, "--dot"))
    {
        Console.WriteLine("digraph deps {");
        foreach (var (from, to) in ws.FileGraphEdges)
            Console.WriteLine($"  \"{Path.GetFileName(from)}\" -> \"{Path.GetFileName(to)}\";");
        Console.WriteLine("}");
    }
    else
    {
        foreach (var (from, to) in ws.FileGraphEdges)
            Console.WriteLine($"{Path.GetFileName(from)} -> {Path.GetFileName(to)}");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"{ws.FileGraphEdges.Length} edge(s).");
    }
    return 0;
}

// EXT-01: export entrances / fixed points to KML / GeoJSON / GPX / CSV.
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
static async System.Threading.Tasks.Task<WorkspaceSemanticModel?> LoadWorkspaceAsync(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"error: file not found: {path}"); return null; }
    try
    {
        var ws = new TherionWorkspace();
        await ws.LoadAsync(Path.GetFullPath(path));
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
