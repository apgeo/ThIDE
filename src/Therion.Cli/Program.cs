// Implementation Plan Decision #15 — CLI commands: validate, dump-ast, list-stations.
// Uses RustcStyleDiagnosticFormatter for excellent syntax error reports (§10).

using System.Text.Json;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("therion-cli — Therion project tools");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  therion-cli validate <file>          Parse a .thconfig or .th file and print diagnostics.");
    Console.WriteLine("  therion-cli dump-ast <file>          Print the parsed AST as JSON.");
    Console.WriteLine("  therion-cli list-stations <file>     List station references discovered in a .th file.");
    Console.WriteLine("  therion-cli --version                Print the pinned Therion syntax version.");
    return 0;
}

switch (args[0])
{
    case "--version":
        Console.WriteLine(TherionSyntaxVersion.Default);
        return 0;

    case "validate":
        if (args.Length < 2) { Console.Error.WriteLine("error: 'validate' requires a file path."); return 2; }
        return Validate(args[1]);

    case "dump-ast":
        if (args.Length < 2) { Console.Error.WriteLine("error: 'dump-ast' requires a file path."); return 2; }
        return DumpAst(args[1]);

    case "list-stations":
        if (args.Length < 2) { Console.Error.WriteLine("error: 'list-stations' requires a file path."); return 2; }
        return ListStations(args[1]);

    default:
        Console.Error.WriteLine($"error: unknown command '{args[0]}'. Use --help.");
        return 2;
}

static int Validate(string path)
{
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

static int DumpAst(string path)
{
    var (file, _) = ParseAny(path);
    if (file is null) return 2;

    var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
    {
        WriteIndented = true,
    });
    Console.WriteLine(json);
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
