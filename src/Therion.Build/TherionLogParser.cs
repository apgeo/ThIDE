// Structured reader for a whole `therion.log` file.
//
// Complements HeuristicTherionOutputParser, which classifies ONE line of live compiler
// output into a diagnostic. This one takes the persisted log as a whole and lifts every
// value Therion reports — versions, the CRS/geomag block, the embedded cavern (Survex)
// statistics, the loop-error table, the written outputs, and the diagnostics — into a
// typed summary. It is deliberately tolerant: a failed run's log stops mid-way, so every
// field is optional and unrecognised lines are simply ignored.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Therion.Build;

/// <summary>One <c>error</c>/<c>warning</c>/<c>hint</c> reported in the log.</summary>
public sealed record TherionLogDiagnostic(
    TherionLogSeverity Severity,
    string Message,
    string? File,
    int? Line,
    string? Symbol,
    string RawLine);

public enum TherionLogSeverity { Info, Hint, Warning, Error }

/// <summary>A geomagnetic declination Therion computed for one date, in degrees.</summary>
public sealed record TherionLogDeclination(string Date, double Degrees);

/// <summary>One row of the <c>loop errors</c> table.</summary>
public sealed record TherionLogLoopError(
    double RelativeErrorPercent,
    double AbsoluteError,
    double TotalLength,
    int StationCount,
    double ErrorX,
    double ErrorY,
    double ErrorZ,
    string Stations,
    string RawLine);

/// <summary>One PROJ transformation from the <c>CRS transformations chosen by PROJ</c> block.</summary>
public sealed record TherionLogCrsTransformation(
    string From,
    string To,
    bool InAreaOfUse,
    string Transformation,
    string Definition,
    string Accuracy,
    string RawLine);

/// <summary>A file Therion wrote (<c>writing rez\cave.lox ... done</c>).</summary>
public sealed record TherionLogOutputFile(string Path, bool Completed);

/// <summary>A pipeline step (<c>preprocessing database ... done</c>); <see cref="Completed"/>
/// is false for the step a failed run died in.</summary>
public sealed record TherionLogStage(string Name, bool Completed);

/// <summary>A measured extent plus the stations that bound it (<c>Vertical range = …</c>).</summary>
public sealed record TherionLogRange(double Length, string FromStation, double FromValue, string ToStation, double ToValue);

/// <summary>Overall verdict for the run the log describes.</summary>
public enum TherionLogOutcome
{
    /// <summary>Ran to completion (a <c>compilation time</c> line was reached).</summary>
    Success,
    /// <summary>Completed, but Therion reported at least one warning or hint.</summary>
    SuccessWithWarnings,
    /// <summary>Therion reported an error, or the run stopped before finishing.</summary>
    Failed,
}

/// <summary>Everything <see cref="TherionLogParser"/> could lift out of a <c>therion.log</c>.</summary>
public sealed class TherionLogSummary
{
    // --- versions / inputs -------------------------------------------------
    public string? TherionVersion { get; init; }
    public string? TherionReleaseDate { get; init; }
    /// <summary>The raw "- using …" lines, e.g. "Proj 9.4.1, compiled against 9.4.1".</summary>
    public IReadOnlyList<string> Libraries { get; init; } = Array.Empty<string>();
    public string? ProjVersion { get; init; }
    public string? ProjCompiledAgainst { get; init; }
    public string? SurvexVersion { get; init; }
    public string? InitializationFile { get; init; }
    public string? ConfigurationFile { get; init; }
    public string? Encoding { get; init; }

    // --- geo ---------------------------------------------------------------
    public string? OutputCoordinateSystem { get; init; }
    public double? MeridianConvergence { get; init; }
    public IReadOnlyList<TherionLogDeclination> Declinations { get; init; } = Array.Empty<TherionLogDeclination>();
    public string? AreaOfUse { get; init; }
    public IReadOnlyList<TherionLogCrsTransformation> CrsTransformations { get; init; } = Array.Empty<TherionLogCrsTransformation>();

    // --- survey statistics (from the embedded cavern log) -------------------
    public int? StationCount { get; init; }
    public int? ShotCount { get; init; }
    public int? LoopCount { get; init; }
    public int? ConnectedComponents { get; init; }
    /// <summary>True when cavern had to invent a fixed point ("Survey has no control points").</summary>
    public bool HasNoControlPoints { get; init; }
    public double? TotalLength { get; init; }
    public double? TotalLengthAdjusted { get; init; }
    public double? PlanLength { get; init; }
    public double? VerticalLength { get; init; }
    public TherionLogRange? VerticalRange { get; init; }
    public TherionLogRange? NorthSouthRange { get; init; }
    public TherionLogRange? EastWestRange { get; init; }
    public double? AverageLoopErrorPercent { get; init; }
    public IReadOnlyList<TherionLogLoopError> LoopErrors { get; init; } = Array.Empty<TherionLogLoopError>();

    // --- run ---------------------------------------------------------------
    public IReadOnlyList<TherionLogStage> Stages { get; init; } = Array.Empty<TherionLogStage>();
    public IReadOnlyList<TherionLogOutputFile> OutputFiles { get; init; } = Array.Empty<TherionLogOutputFile>();
    public int? CompilationTimeSeconds { get; init; }
    /// <summary>True when the log ends with Therion's "Press ENTER to exit!" abort prompt.</summary>
    public bool Aborted { get; init; }
    public IReadOnlyList<TherionLogDiagnostic> Diagnostics { get; init; } = Array.Empty<TherionLogDiagnostic>();

    public IEnumerable<TherionLogDiagnostic> Errors => Diagnostics.Where(d => d.Severity == TherionLogSeverity.Error);
    public IEnumerable<TherionLogDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == TherionLogSeverity.Warning);

    public TherionLogOutcome Outcome =>
        Errors.Any() || Aborted || CompilationTimeSeconds is null ? TherionLogOutcome.Failed
        : Diagnostics.Any(d => d.Severity is TherionLogSeverity.Warning or TherionLogSeverity.Hint)
            ? TherionLogOutcome.SuccessWithWarnings
            : TherionLogOutcome.Success;

    /// <summary>
    /// The last stage that never printed "done" — the step a failed run died in. A successful
    /// run can have one too (Therion omits the "done" after an empty cavern log), so only treat
    /// it as the failure point when <see cref="Outcome"/> is <see cref="TherionLogOutcome.Failed"/>.
    /// </summary>
    public string? IncompleteStage => Stages.LastOrDefault(s => !s.Completed)?.Name;
}

public static class TherionLogParser
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Older releases print the version alone, without the "(release date)" suffix.
    private static readonly Regex HeaderRx = new(@"^therion\s+(?<ver>\S+)(?:\s+\((?<date>[^)]+)\))?\s*$", Opts);
    private static readonly Regex LibraryRx = new(@"^\s+-\s+using\s+(?<lib>.+?)\s*$", Opts);
    private static readonly Regex ProjRx = new(@"^Proj\s+(?<used>\S+?),\s*compiled against\s+(?<built>\S+)\s*$", Opts);
    private static readonly Regex LabeledFileRx = new(@"^(?<label>initialization|configuration)\s+file:\s+(?<path>\S.*?)\s*$", Opts);
    private static readonly Regex EncodingRx = new(@"^encoding\s+(?<enc>\S+)\s*$", Opts);

    private static readonly Regex OutputCsRx = new(@"^output coordinate system:\s*(?<cs>\S.*?)\s*$", Opts);
    private static readonly Regex ConvergenceRx = new(@"^meridian convergence \(deg\):\s*(?<v>-?[\d.]+)\s*$", Opts);
    private static readonly Regex DeclinationRx = new(@"^\s+(?<date>\d{4}\.\d+\.\d+)\s+(?<v>-?[\d.]+)\s*$", Opts);
    private static readonly Regex AreaOfUseRx = new(@"^\s*Area of Use \(AoU\):\s*(?<v>\S.*?)\s*$", Opts);
    private static readonly Regex CrsRx = new(
        @"^\s*\[(?<from>[^\]]+?)\s*(?:→|->)\s*(?<to>[^\]]+?)\]\s*AoU:\s*\[(?<aou>[^\]]*)\]\s*" +
        @"transformation:\s*\[(?<tr>.*?)\]\s*definition:\s*\[(?<def>.*?)\]\s*accuracy:\s*\[(?<acc>.*?)\]\s*$", Opts);

    private static readonly Regex AvgLoopErrRx = new(@"^average loop error:\s*(?<v>-?[\d.]+)%", Opts);
    private static readonly Regex CompilationTimeRx = new(@"^compilation time:\s*(?<v>\d+)\s*sec", Opts);

    // "writing rez\cave.lox ... done" — the trailing "done" may land on a later line.
    private static readonly Regex WritingRx = new(@"^writing\s+(?<path>.+?)\s*\.\.\.\s*(?<done>done)?\s*$", Opts);
    // Any other "<stage> ... done" progress line.
    private static readonly Regex StageRx = new(@"^(?<name>[A-Za-z][^.]*?)\s*\.\.\.\s*(?<done>done)?\s*$", Opts);

    // "<prog>: error -- <rest>" (prog is a path, so match the severity token, not the first colon).
    private static readonly Regex DiagRx = new(
        @"^(?:(?<prog>.+?):\s+)?(?<sev>error|warning|hint)\s+--\s+(?<rest>.*?)\s*$", Opts);
    // Survex's own "data.svx:6: info: message" lines inside the cavern block.
    private static readonly Regex SurvexDiagRx = new(
        @"^(?<file>\S+):(?<line>\d+):\s*(?<sev>info|warning|error):\s*(?<msg>.*?)\s*$", Opts);
    // A location Therion embeds in a message: "<file> [<line>] -- <message>".
    private static readonly Regex DiagLocationRx = new(@"^(?<file>\S.*?)\s+\[(?<line>\d+)\]\s+--\s+(?<rest>.*)$", Opts);

    // Cavern statistics, echoed into the log with an " NN> " line-number prefix.
    private static readonly Regex CavernPrefixRx = new(@"^\s*\d+>\s?", Opts);
    private static readonly Regex SurvexVersionRx = new(@"^Survex\s+(?<v>\S+)", Opts);
    private static readonly Regex ContainsRx = new(@"^Survey contains (?<st>\d+) survey stations, joined by (?<sh>\d+) shots\.", Opts);
    private static readonly Regex LoopsRx = new(@"^There (?:is|are) (?<n>\d+) loops?\.", Opts);
    private static readonly Regex ComponentsRx = new(@"^Survey has (?<n>\d+) connected components?\.", Opts);
    private static readonly Regex TotalLenRx = new(@"^Total length of survey shots =\s*(?<v>[\d.]+)m\s*\(\s*(?<adj>[\d.]+)m adjusted\)", Opts);
    private static readonly Regex PlanLenRx = new(@"^Total plan length of survey shots =\s*(?<v>[\d.]+)m", Opts);
    private static readonly Regex VertLenRx = new(@"^Total vertical length of survey shots =\s*(?<v>[\d.]+)m", Opts);
    private static readonly Regex RangeRx = new(
        @"^(?<axis>Vertical|North-South|East-West) range =\s*(?<len>[\d.]+)m \(from (?<a>\S+) at (?<av>-?[\d.]+)m to (?<b>\S+) at (?<bv>-?[\d.]+)m\)", Opts);

    // " 71.92%    9.9m   13.7m   5   -0.4m    0.9m    9.8m [A - B - A]"
    private static readonly Regex LoopErrorRx = new(
        @"^\s*(?<rel>-?[\d.]+)%\s+(?<abs>-?[\d.]+)m\s+(?<total>-?[\d.]+)m\s+(?<sts>\d+)\s+" +
        @"(?<x>-?[\d.]+)m\s+(?<y>-?[\d.]+)m\s+(?<z>-?[\d.]+)m\s+\[(?<stations>.*)\]\s*$", Opts);

    // Transcription block: " 9> 623 : .@grind_intrare_0.grind -- 67156 : .@grind_term.grind"
    private static readonly Regex TranscriptionRx = new(@"(?<num>\d+)\s+:\s+(?<name>\S+)", Opts);

    // A banner line: "##### cavern log file #####".
    private static readonly Regex BannerRx = new(@"^#+\s*(?<title>.*?)\s*#+\s*$", Opts);

    /// <summary>
    /// True when <paramref name="text"/> looks like Therion compiler output. An unambiguous
    /// banner (the version line, or the cavern/loop-error block markers) is enough on its own;
    /// a <c>.log</c> extension alone is not, so an unrelated <c>.log</c> gets no summary tab.
    /// </summary>
    public static bool LooksLikeTherionLog(string? filePath, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var ext = filePath is null ? string.Empty : System.IO.Path.GetExtension(filePath);
        bool logExtension = ext.Equals(".log", StringComparison.OrdinalIgnoreCase);

        int scanned = 0;
        foreach (var raw in EnumerateLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (HeaderRx.IsMatch(line)) return true;
            if (line.Contains("cavern log file", StringComparison.Ordinal)) return true;
            if (line.Contains("loop errors", StringComparison.Ordinal) && line.StartsWith("#", StringComparison.Ordinal)) return true;

            // Weaker markers: only trusted when the file also *calls* itself a log.
            if (logExtension)
            {
                if (line.StartsWith("configuration file:", StringComparison.Ordinal)) return true;
                if (line.StartsWith("compilation time:", StringComparison.Ordinal)) return true;
                if (DiagRx.IsMatch(line) && line.Contains(" -- ", StringComparison.Ordinal)) return true;
            }

            if (++scanned >= 200) break; // only probe the head of the file
        }
        return false;
    }

    public static TherionLogSummary Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new TherionLogSummary();

        var s = new Builder();
        var section = Section.None;
        // Numeric station id -> qualified name, from the "transcription" block that follows
        // the cavern log and decodes the ids cavern printed in its range statistics.
        var transcription = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in EnumerateLines(text))
        {
            var line = rawLine.TrimEnd();

            if (BannerRx.Match(line) is { Success: true } banner)
            {
                section = SectionFor(banner.Groups["title"].Value);
                continue;
            }

            // The cavern/transcription blocks are not always closed by an "end of …" banner
            // (an empty cavern log has none), so a line that is neither cavern-prefixed nor a
            // diagnostic ends the block and is re-read as a top-level line.
            if (section is Section.Cavern or Section.Transcription)
            {
                if (line.Trim().Length == 0) continue;
                if (CavernPrefixRx.IsMatch(line))
                {
                    var body = StripCavernPrefix(line);
                    if (section == Section.Cavern) ReadCavernLine(body, s); else ReadTranscription(body, transcription);
                    continue;
                }
                if (ReadDiagnostic(line, s)) continue;
                if (section == Section.Cavern && ReadCavernLine(line, s)) continue;
                section = Section.None; // fall through to the top-level handling below
            }

            if (section == Section.LoopErrors && LoopErrorRx.Match(line) is { Success: true } le)
            {
                s.LoopErrors.Add(new TherionLogLoopError(
                    Num(le.Groups["rel"].Value), Num(le.Groups["abs"].Value), Num(le.Groups["total"].Value),
                    int.Parse(le.Groups["sts"].Value, CultureInfo.InvariantCulture),
                    Num(le.Groups["x"].Value), Num(le.Groups["y"].Value), Num(le.Groups["z"].Value),
                    le.Groups["stations"].Value, line));
                continue;
            }

            if (section == Section.Crs)
            {
                if (AreaOfUseRx.Match(line) is { Success: true } aou) { s.AreaOfUse ??= aou.Groups["v"].Value; continue; }
                if (CrsRx.Match(line) is { Success: true } crs)
                {
                    s.CrsTransformations.Add(new TherionLogCrsTransformation(
                        crs.Groups["from"].Value, crs.Groups["to"].Value,
                        crs.Groups["aou"].Value.Equals("yes", StringComparison.OrdinalIgnoreCase),
                        crs.Groups["tr"].Value, crs.Groups["def"].Value, crs.Groups["acc"].Value, line));
                    continue;
                }
            }

            // Diagnostics can appear anywhere, including inside a section.
            if (ReadDiagnostic(line, s)) continue;
            if (section != Section.None) continue;

            ReadTopLevelLine(line, s);
        }

        s.ResolveTranscription(transcription);
        return s.Build();
    }

    // ----- top-level lines ---------------------------------------------------

    private static void ReadTopLevelLine(string line, Builder s)
    {
        if (line.Trim() == "Press ENTER to exit!") { s.Aborted = true; return; }

        if (HeaderRx.Match(line) is { Success: true } h)
        {
            s.TherionVersion = h.Groups["ver"].Value;
            s.TherionReleaseDate = h.Groups["date"].Success ? h.Groups["date"].Value : null;
            return;
        }

        if (LibraryRx.Match(line) is { Success: true } lib)
        {
            var value = lib.Groups["lib"].Value;
            s.Libraries.Add(value);
            if (ProjRx.Match(value) is { Success: true } proj)
            {
                s.ProjVersion = proj.Groups["used"].Value;
                s.ProjCompiledAgainst = proj.Groups["built"].Value;
            }
            return;
        }

        if (LabeledFileRx.Match(line) is { Success: true } lf)
        {
            // The path is the whole remainder: it may contain spaces ("C:\Program Files\…").
            if (lf.Groups["label"].Value == "initialization") s.InitializationFile = lf.Groups["path"].Value;
            else s.ConfigurationFile = lf.Groups["path"].Value;
            return;
        }

        if (EncodingRx.Match(line) is { Success: true } enc) { s.Encoding = enc.Groups["enc"].Value; return; }
        if (OutputCsRx.Match(line) is { Success: true } cs) { s.OutputCoordinateSystem = cs.Groups["cs"].Value; return; }
        if (ConvergenceRx.Match(line) is { Success: true } mc) { s.MeridianConvergence = Num(mc.Groups["v"].Value); return; }
        if (DeclinationRx.Match(line) is { Success: true } dec)
        {
            s.Declinations.Add(new TherionLogDeclination(dec.Groups["date"].Value, Num(dec.Groups["v"].Value)));
            return;
        }
        if (AvgLoopErrRx.Match(line) is { Success: true } ale) { s.AverageLoopErrorPercent = Num(ale.Groups["v"].Value); return; }
        if (CompilationTimeRx.Match(line) is { Success: true } ct)
        {
            s.CompilationTimeSeconds = int.Parse(ct.Groups["v"].Value, CultureInfo.InvariantCulture);
            return;
        }

        // A lone "done" completes the progress line that was left open ("writing x.lox ...").
        if (line.Trim() == "done") { s.CompletePending(); return; }

        if (WritingRx.Match(line) is { Success: true } w)
        {
            var path = w.Groups["path"].Value;
            bool done = w.Groups["done"].Success;
            // "writing xtherion file" is a step, not an output artifact.
            if (path.Equals("xtherion file", StringComparison.OrdinalIgnoreCase)) s.AddStage("writing " + path, done);
            else s.AddOutput(path, done);
            return;
        }

        if (StageRx.Match(line) is { Success: true } st) s.AddStage(st.Groups["name"].Value.Trim(), st.Groups["done"].Success);
    }

    // ----- diagnostics -------------------------------------------------------

    private static bool ReadDiagnostic(string line, Builder s)
    {
        var trimmed = line.Trim();

        if (DiagRx.Match(trimmed) is { Success: true } d)
        {
            var severity = d.Groups["sev"].Value switch
            {
                "error" => TherionLogSeverity.Error,
                "warning" => TherionLogSeverity.Warning,
                _ => TherionLogSeverity.Hint,
            };
            var (message, file, ln, symbol) = SplitMessage(d.Groups["rest"].Value);
            s.Diagnostics.Add(new TherionLogDiagnostic(severity, message, file, ln, symbol, trimmed));
            return true;
        }

        if (SurvexDiagRx.Match(trimmed) is { Success: true } sd)
        {
            var severity = sd.Groups["sev"].Value switch
            {
                "error" => TherionLogSeverity.Error,
                "warning" => TherionLogSeverity.Warning,
                _ => TherionLogSeverity.Info,
            };
            s.Diagnostics.Add(new TherionLogDiagnostic(severity, sd.Groups["msg"].Value,
                sd.Groups["file"].Value, int.Parse(sd.Groups["line"].Value, CultureInfo.InvariantCulture),
                null, trimmed));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits the tail of a diagnostic into its parts. Therion writes
    /// <c>[&lt;file&gt; [&lt;line&gt;] --] &lt;message&gt; [-- &lt;symbol&gt;]</c>, where the trailing
    /// segment is the offending identifier only when it is a single token.
    /// </summary>
    private static (string Message, string? File, int? Line, string? Symbol) SplitMessage(string rest)
    {
        string? file = null;
        int? line = null;

        if (DiagLocationRx.Match(rest) is { Success: true } loc)
        {
            file = loc.Groups["file"].Value;
            line = int.Parse(loc.Groups["line"].Value, CultureInfo.InvariantCulture);
            rest = loc.Groups["rest"].Value;
        }

        string? symbol = null;
        int split = rest.LastIndexOf(" -- ", StringComparison.Ordinal);
        if (split >= 0)
        {
            var tail = rest[(split + 4)..].Trim();
            if (tail.Length > 0 && !tail.Contains(' '))
            {
                symbol = tail;
                rest = rest[..split];
            }
        }

        return (rest.Trim(), file, line, symbol);
    }

    // ----- cavern (Survex) statistics ---------------------------------------

    private static string StripCavernPrefix(string line) => CavernPrefixRx.Replace(line, string.Empty);

    /// <summary>Reads one line of the embedded cavern log; false when nothing was recognised.</summary>
    private static bool ReadCavernLine(string line, Builder s)
    {
        if (ReadDiagnostic(line, s)) return true;

        if (SurvexVersionRx.Match(line) is { Success: true } sv) { s.SurvexVersion = sv.Groups["v"].Value; return true; }
        if (line.StartsWith("Survey has no control points", StringComparison.Ordinal)) { s.HasNoControlPoints = true; return true; }
        if (ContainsRx.Match(line) is { Success: true } c)
        {
            s.StationCount = int.Parse(c.Groups["st"].Value, CultureInfo.InvariantCulture);
            s.ShotCount = int.Parse(c.Groups["sh"].Value, CultureInfo.InvariantCulture);
            return true;
        }
        if (LoopsRx.Match(line) is { Success: true } l) { s.LoopCount = int.Parse(l.Groups["n"].Value, CultureInfo.InvariantCulture); return true; }
        if (ComponentsRx.Match(line) is { Success: true } cc) { s.ConnectedComponents = int.Parse(cc.Groups["n"].Value, CultureInfo.InvariantCulture); return true; }
        if (TotalLenRx.Match(line) is { Success: true } tl)
        {
            s.TotalLength = Num(tl.Groups["v"].Value);
            s.TotalLengthAdjusted = Num(tl.Groups["adj"].Value);
            return true;
        }
        if (PlanLenRx.Match(line) is { Success: true } pl) { s.PlanLength = Num(pl.Groups["v"].Value); return true; }
        if (VertLenRx.Match(line) is { Success: true } vl) { s.VerticalLength = Num(vl.Groups["v"].Value); return true; }
        if (RangeRx.Match(line) is { Success: true } r)
        {
            var range = new TherionLogRange(Num(r.Groups["len"].Value),
                r.Groups["a"].Value, Num(r.Groups["av"].Value),
                r.Groups["b"].Value, Num(r.Groups["bv"].Value));
            switch (r.Groups["axis"].Value)
            {
                case "Vertical": s.VerticalRange = range; break;
                case "North-South": s.NorthSouthRange = range; break;
                default: s.EastWestRange = range; break;
            }
            return true;
        }

        // Node-degree histograms ("1070 1-nodes.") and other cavern chatter are ignored.
        return false;
    }

    private static void ReadTranscription(string line, Dictionary<string, string> map)
    {
        foreach (Match m in TranscriptionRx.Matches(StripCavernPrefix(line)))
            map[m.Groups["num"].Value] = m.Groups["name"].Value;
    }

    // ----- helpers -----------------------------------------------------------

    private enum Section { None, Cavern, Transcription, LoopErrors, Crs }

    private static Section SectionFor(string title) => title switch
    {
        "cavern log file" => Section.Cavern,
        "transcription" => Section.Transcription,
        "loop errors" => Section.LoopErrors,
        "CRS transformations chosen by PROJ" => Section.Crs,
        _ => Section.None, // every "end of …" banner closes back to the top level
    };

    private static double Num(string v) => double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static IEnumerable<string> EnumerateLines(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            yield return text[start..end];
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    /// <summary>Mutable accumulator; <see cref="Build"/> freezes it into the immutable summary.</summary>
    private sealed class Builder
    {
        public string? TherionVersion, TherionReleaseDate, ProjVersion, ProjCompiledAgainst, SurvexVersion;
        public string? InitializationFile, ConfigurationFile, Encoding, OutputCoordinateSystem, AreaOfUse;
        public double? MeridianConvergence, TotalLength, TotalLengthAdjusted, PlanLength, VerticalLength, AverageLoopErrorPercent;
        public int? StationCount, ShotCount, LoopCount, ConnectedComponents, CompilationTimeSeconds;
        public bool Aborted, HasNoControlPoints;
        public TherionLogRange? VerticalRange, NorthSouthRange, EastWestRange;

        public readonly List<string> Libraries = new();
        public readonly List<TherionLogDeclination> Declinations = new();
        public readonly List<TherionLogCrsTransformation> CrsTransformations = new();
        public readonly List<TherionLogLoopError> LoopErrors = new();
        public readonly List<TherionLogDiagnostic> Diagnostics = new();
        public readonly List<TherionLogStage> Stages = new();
        public readonly List<TherionLogOutputFile> OutputFiles = new();

        // "<step> ..." lines whose "done" hasn't been seen yet. They nest: Therion prints
        // "writing x.lox ...", then a sub-step's own "… done", then the lox's " done" alone
        // on the next line — so the open steps form a stack, innermost last.
        private readonly Stack<(bool IsOutput, int Index)> _pending = new();

        public void AddStage(string name, bool done)
        {
            Stages.Add(new TherionLogStage(name, done));
            if (!done) _pending.Push((false, Stages.Count - 1));
        }

        public void AddOutput(string path, bool done)
        {
            OutputFiles.Add(new TherionLogOutputFile(path, done));
            if (!done) _pending.Push((true, OutputFiles.Count - 1));
        }

        /// <summary>A standalone "done" line closes the innermost open progress line.</summary>
        public void CompletePending()
        {
            if (_pending.Count == 0) return;
            var (isOutput, i) = _pending.Pop();
            if (isOutput) OutputFiles[i] = OutputFiles[i] with { Completed = true };
            else Stages[i] = Stages[i] with { Completed = true };
        }

        /// <summary>Rewrites cavern's numeric station ids in the range statistics to their names.</summary>
        public void ResolveTranscription(Dictionary<string, string> map)
        {
            if (map.Count == 0) return;
            VerticalRange = Resolve(VerticalRange, map);
            NorthSouthRange = Resolve(NorthSouthRange, map);
            EastWestRange = Resolve(EastWestRange, map);
        }

        private static TherionLogRange? Resolve(TherionLogRange? r, Dictionary<string, string> map) =>
            r is null ? null
            : r with
            {
                FromStation = map.TryGetValue(r.FromStation, out var a) ? a : r.FromStation,
                ToStation = map.TryGetValue(r.ToStation, out var b) ? b : r.ToStation,
            };

        public TherionLogSummary Build() => new()
        {
            TherionVersion = TherionVersion,
            TherionReleaseDate = TherionReleaseDate,
            Libraries = Libraries,
            ProjVersion = ProjVersion,
            ProjCompiledAgainst = ProjCompiledAgainst,
            SurvexVersion = SurvexVersion,
            InitializationFile = InitializationFile,
            ConfigurationFile = ConfigurationFile,
            Encoding = Encoding,
            OutputCoordinateSystem = OutputCoordinateSystem,
            MeridianConvergence = MeridianConvergence,
            Declinations = Declinations,
            AreaOfUse = AreaOfUse,
            CrsTransformations = CrsTransformations,
            StationCount = StationCount,
            ShotCount = ShotCount,
            LoopCount = LoopCount,
            ConnectedComponents = ConnectedComponents,
            HasNoControlPoints = HasNoControlPoints,
            TotalLength = TotalLength,
            TotalLengthAdjusted = TotalLengthAdjusted,
            PlanLength = PlanLength,
            VerticalLength = VerticalLength,
            VerticalRange = VerticalRange,
            NorthSouthRange = NorthSouthRange,
            EastWestRange = EastWestRange,
            AverageLoopErrorPercent = AverageLoopErrorPercent,
            LoopErrors = LoopErrors,
            Stages = Stages,
            OutputFiles = OutputFiles,
            CompilationTimeSeconds = CompilationTimeSeconds,
            Aborted = Aborted,
            Diagnostics = Diagnostics,
        };
    }
}
