// `therion-cli import` — a Survex, Compass, GPX or shot-table (CSV/TSV, PocketTopo/DistoX text) file
// to Therion source, headless: the importers the app and the import_survey tool use.

using System.Globalization;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Import;

namespace Therion.Cli;

public static class ImportVerb
{
    /// <summary>
    /// <c>import &lt;source&gt; [--format f] [--survey name] [--out file.th] [--columns "…"] [--start st]
    /// [--length-unit m|ft] [--angle-unit deg|grad] [--merge N]</c>. Returns 0 ok, 1 the import or the
    /// write failed, 2 usage error.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? source = null, format = null, survey = null, output = null;
        var shots = new ShotTableOptions();
        for (int i = 1; i < args.Count; i++)
        {
            string? Value() => i + 1 < args.Count ? args[++i] : null;
            switch (args[i])
            {
                case "--format": format = Value(); break;
                case "--survey": survey = Value(); break;
                case "--out": output = Value(); break;
                case "--start": shots = shots with { StartStation = Value() ?? "0" }; break;
                case "--columns":
                    var cols = Value();
                    if (cols is null || ShotTableReader.ParseColumns(cols) is not { } parsed)
                    {
                        stderr.WriteLine("error: --columns expects data keywords such as \"from to length compass clino\" (with length, compass and clino).");
                        return 2;
                    }
                    shots = shots with { Columns = parsed };
                    break;
                case "--length-unit":
                    switch (Value()?.ToLowerInvariant())
                    {
                        case "m" or "meters" or "metres": shots = shots with { LengthUnit = ShotLengthUnit.Meters }; break;
                        case "ft" or "feet": shots = shots with { LengthUnit = ShotLengthUnit.Feet }; break;
                        default: stderr.WriteLine("error: --length-unit is m or ft."); return 2;
                    }
                    break;
                case "--angle-unit":
                    switch (Value()?.ToLowerInvariant())
                    {
                        case "deg" or "degrees": shots = shots with { AngleUnit = ShotAngleUnit.Degrees }; break;
                        case "grad" or "grads": shots = shots with { AngleUnit = ShotAngleUnit.Grads }; break;
                        default: stderr.WriteLine("error: --angle-unit is deg or grad."); return 2;
                    }
                    break;
                case "--merge":
                    if (!int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var merge) || merge is 1 or > 10)
                    {
                        stderr.WriteLine("error: --merge expects 0 (off) or the number of repeated shots that make a leg (2–10).");
                        return 2;
                    }
                    shots = shots with { MergeRepeats = merge };
                    break;
                default:
                    if (source is null && !args[i].StartsWith("--", StringComparison.Ordinal)) source = args[i];
                    else { stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
                    break;
            }
        }
        if (source is null) { stderr.WriteLine("error: 'import' requires a source file."); return 2; }
        if (!File.Exists(source)) { stderr.WriteLine($"error: file not found: {source}"); return 2; }
        if (!SurveyImport.TryResolveFormat(format, source, out var resolved, out var formatError)) { stderr.WriteLine("error: " + formatError); return 2; }
        if (survey is not null && !ScaffoldValueRules.IsSurveyName(survey)) { stderr.WriteLine($"error: '{survey}' is not a valid survey name."); return 2; }
        if (output is not null && !output.EndsWith(".th", StringComparison.OrdinalIgnoreCase)) { stderr.WriteLine("error: --out must name a .th file."); return 2; }
        if (output is not null && File.Exists(output)) { stderr.WriteLine($"error: {output} already exists; the import never overwrites."); return 1; }

        var text = EncodingResolver.ReadAllText(Path.GetFullPath(source));
        survey ??= resolved == SurveyImportFormat.Gpx ? "gps" : ScaffoldValueRules.SafeSurveyName(Path.GetFileNameWithoutExtension(source));
        if (resolved == SurveyImportFormat.Gpx && GpxImporter.Parse(text).Count == 0)
        {
            stderr.WriteLine($"error: '{source}' holds no waypoints — it is empty, or it is not gpx.");
            return 1;
        }

        string therion;
        var issues = new List<ShotIssue>();
        try { therion = SurveyImport.Translate(resolved, text, survey, shots, issues); }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: '{source}' is not readable as {resolved.ToString().ToLowerInvariant()}: {ex.Message}");
            return 1;
        }
        foreach (var issue in issues) stderr.WriteLine("warning: " + ShotTableReader.Describe(issue));
        if (string.IsNullOrWhiteSpace(therion)) { stderr.WriteLine($"error: '{source}' produced no survey data."); return 1; }

        if (output is null) { stdout.Write(therion); return 0; }
        try
        {
            SourceFileIo.Create(Path.GetFullPath(output), therion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: {output}: {ex.Message}");
            return 1;
        }
        stderr.WriteLine($"wrote {output} — add 'input {Path.GetFileName(output)}' to the parent survey to include it.");
        return 0;
    }
}
