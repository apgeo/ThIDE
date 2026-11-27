// `therion-cli flatten` — a whole project (every file its configuration sources) as one .th file holding
// one survey with one centreline: the engine the app's "Export as a single .th file" uses.

using System.Globalization;
using System.Text;
using Therion.Core;
using Therion.Workspace;
using Therion.Workspace.Editing;
using Therion.Workspace.Flatten;

namespace Therion.Cli;

public static class FlattenVerb
{
    public const string Usage =
        "  therion-cli flatten <thconfig|file.th|folder> [--out <file.th>] [--force] [--standalone] [--survey <name>] [--title <text>]\n" +
        "                     [--naming keep|survey|sequential] [--prefix <text>] [--separator <text>]\n" +
        "                     [--splays <percent>] [--max-splays <n>] [--no-declination] [--location <lat>,<lon>]\n" +
        "                     [--no-source-comments] [--no-shot-comments] [--names <file.csv>] [--thconfig <file>]\n" +
        "                                         Merge the whole project into one survey with one centreline: equated stations\n" +
        "                                         become one, names are kept (prefixed with their survey where two clash), renamed\n" +
        "                                         with their survey (--naming survey) or numbered (--naming sequential), --prefix goes\n" +
        "                                         in front of every name. --splays keeps that share of each station's splays and\n" +
        "                                         --max-splays at most that many, the ones that cover the passage best. Each shot's\n" +
        "                                         magnetic declination (stated, or of its survey's date, IGRF) goes into its compass\n" +
        "                                         reading; --location gives the cave's position when its fixed points are in a\n" +
        "                                         coordinate system ThIDE cannot place. --standalone adds the fixed points, the\n" +
        "                                         entrances and other station comments, dates and team, so no other file is needed.\n" +
        "                                         --names writes the old and new name of every station; --thconfig writes a\n" +
        "                                         configuration that compiles the file. No drawings (.th2) are carried. Never\n" +
        "                                         overwrites without --force.";

    /// <summary>Returns 0 written, 1 written but with errors (or not written), 2 usage error.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? source = null, output = null, names = null, thconfig = null;
        bool force = false;
        var options = new FlattenOptions { GeneratedAt = DateTimeOffset.Now };
        for (int i = 1; i < args.Count; i++)
        {
            string? Value() => i + 1 < args.Count ? args[++i] : null;
            switch (args[i])
            {
                case "--out": output = Value(); break;
                case "--force": force = true; break;
                case "--standalone": options = options with { Standalone = true }; break;
                case "--survey": options = options with { SurveyName = Value() }; break;
                case "--title": options = options with { Title = Value() }; break;
                case "--prefix": options = options with { Prefix = Value() ?? "" }; break;
                case "--separator": options = options with { Separator = Value() ?? "_" }; break;
                case "--naming":
                    switch (Value()?.ToLowerInvariant())
                    {
                        case "keep": options = options with { Naming = FlattenStationNaming.Keep }; break;
                        case "survey" or "survey-prefix": options = options with { Naming = FlattenStationNaming.SurveyPrefix }; break;
                        case "sequential" or "numbers": options = options with { Naming = FlattenStationNaming.Sequential }; break;
                        default: stderr.WriteLine("error: --naming is keep, survey or sequential."); return 2;
                    }
                    break;
                case "--splays":
                    if (!double.TryParse(Value()?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) || pct is < 0 or > 100)
                    {
                        stderr.WriteLine("error: --splays expects the share of splays to keep, 0–100.");
                        return 2;
                    }
                    options = options with { SplayKeepPercent = pct };
                    break;
                case "--max-splays":
                    if (!int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var max))
                    {
                        stderr.WriteLine("error: --max-splays expects a whole number.");
                        return 2;
                    }
                    options = options with { MaxSplaysPerStation = max };
                    break;
                case "--no-declination": options = options with { ApplyDeclination = false }; break;
                case "--location":
                    var parts = (Value() ?? "").Split(',');
                    if (parts.Length != 2
                        || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                        || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                        || lat is < -90 or > 90 || lon is < -180 or > 180)
                    {
                        stderr.WriteLine("error: --location expects <latitude>,<longitude> in degrees (e.g. 45.42,22.85).");
                        return 2;
                    }
                    options = options with { DeclinationLocation = (lat, lon) };
                    break;
                case "--no-source-comments": options = options with { SourceComments = false }; break;
                case "--no-shot-comments": options = options with { ShotComments = false }; break;
                case "--names": names = Value(); break;
                case "--thconfig": thconfig = Value(); break;
                default:
                    if (source is null && !args[i].StartsWith("--", StringComparison.Ordinal)) source = args[i];
                    else { stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
                    break;
            }
        }
        if (source is null) { stderr.WriteLine("error: 'flatten' requires a thconfig, a .th file or a project folder."); return 2; }
        if (output is not null && !output.EndsWith(".th", StringComparison.OrdinalIgnoreCase)) { stderr.WriteLine("error: --out must name a .th file."); return 2; }
        if (thconfig is not null && output is null) { stderr.WriteLine("error: --thconfig needs --out (the configuration sources that file)."); return 2; }

        string entry;
        try { entry = await WorkspaceLoader.ResolveEntryPointAsync(source).ConfigureAwait(false); }
        catch (WorkspaceLoadException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            foreach (var c in ex.Candidates) stderr.WriteLine("  " + c);
            return 2;
        }

        foreach (var path in new[] { output, names, thconfig })
            if (path is not null && File.Exists(path) && !force)
            {
                stderr.WriteLine($"error: {path} already exists; use --force to replace it.");
                return 1;
            }

        var result = SurveyFlattener.Flatten(entry, options);
        foreach (var d in result.Diagnostics)
            stderr.WriteLine($"{Severity(d.Severity)}: {d.Message}{(d.Span.IsEmpty ? "" : $" ({d.Span.FilePath}:{d.Span.Start.Line})")}");

        var s = result.Statistics;
        if (output is null) stdout.Write(result.Text);
        else
        {
            try
            {
                Write(output, result.Text, force);
                if (names is not null) Write(names, result.StationNamesCsv(), force);
                if (thconfig is not null) Write(thconfig, Config(output, thconfig, options, result), force);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return 1;
            }
            stderr.WriteLine($"wrote {output}: {s.Shots} shot(s), {s.SplaysKept} splay(s){(s.SplaysDropped > 0 ? $" ({s.SplaysDropped} left out)" : "")}, "
                + $"{s.Stations} station(s) from {s.Centrelines} centreline(s) in {s.Files} file(s).");
        }
        return result.HasErrors ? 1 : 0;
    }

    private static string Severity(DiagnosticSeverity s) => s switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "note",
    };

    private static void Write(string path, string text, bool force)
    {
        var full = Path.GetFullPath(path);
        if (force && File.Exists(full)) SourceFileIo.WriteOver(full, new UTF8Encoding(false).GetBytes(text));
        else SourceFileIo.Create(full, text);
    }

    /// <summary>A configuration that compiles the merged file to a 3D model, with the project's output coordinate system.</summary>
    private static string Config(string output, string thconfig, FlattenOptions options, FlattenResult result)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(thconfig)) ?? ".";
        var rel = Path.GetRelativePath(dir, Path.GetFullPath(output)).Replace('\\', '/');
        var stem = Path.GetFileNameWithoutExtension(output);
        var sb = new StringBuilder();
        sb.Append("encoding utf-8\n");
        sb.Append("# Compiles ").Append(Path.GetFileName(output)).Append(", written by therion-cli flatten.\n");
        sb.Append("source ").Append(rel.Contains(' ') ? "\"" + rel + "\"" : rel).Append('\n');
        if (options.Standalone && result.ConfigCs is { } cs) sb.Append("cs ").Append(cs).Append('\n');
        sb.Append("export model -o ").Append(stem).Append(".lox\n");
        return sb.ToString();
    }
}
