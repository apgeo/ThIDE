// Project scaffolding for a bare TopoDroid-exported survey `.th`. TopoDroid emits a single
// `survey … centerline … endcenterline … endsurvey` file with no thconfig and no wrapper, so it
// cannot be compiled on its own. Given that file (and a few user choices) this builds the pieces a
// Therion project needs: a directory tree, a wrapper "connection" `.th` that inputs the survey and
// (optionally) georeferences it, and a thconfig with the chosen `export` commands.
//
// Pure string/plan output — no file I/O here (that belongs to the caller, per the core-lib rules).
// The caller creates the directories, copies the source survey (and its sketches) into place, and
// writes each file.
//
// Sketches (`.th2`) are copied beside the survey and are NOT input by the wrapper: a sketch's scraps
// name stations in the survey's own namespace (`point … station -name 0`), so it has to be input
// from inside that survey — which is what TopoDroid's export does (`input <plot>.th2` and the `map`
// blocks after `endcenterline`). Therion resolves those `input` paths relative to the survey file,
// so placing the sketches in the same directory is all the wrapper needs. Without sketches the map
// exports still compile; they come out centreline-only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Therion.Workspace.Import;

/// <summary>The Therion export command family a target belongs to.</summary>
public enum ExportKind { Model, Map, Atlas, Database, SurveyList, CaveList, ContinuationList }

/// <summary>
/// One <c>export</c> command to emit into the thconfig. <paramref name="Fmt"/> is the Therion
/// <c>-fmt</c> token (empty = Therion's default for that kind). <paramref name="Extension"/> drives
/// the default output filename. Map-only fields (<paramref name="Projection"/>, <paramref name="UseLayout"/>)
/// are ignored for non-map kinds; <paramref name="WallSource"/> applies to model <c>shp</c>/<c>kml</c>.
/// </summary>
public sealed record ExportItem(
    ExportKind Kind,
    string Fmt,
    string Extension,
    string? Projection = null,
    string? WallSource = null,
    bool UseLayout = false);

/// <summary>Metadata sniffed from a TopoDroid survey file.</summary>
public sealed record SourceSurveyInfo(string SurveyName, string Title, string EntranceHint);

/// <summary>Every knob the scaffolder exposes. Sensible defaults so the caller can override only what it needs.</summary>
public sealed record ScaffoldOptions
{
    /// <summary>Outer/wrapper survey name and project folder name.</summary>
    public string ProjectName { get; init; } = "cave";
    /// <summary>Survey name declared *inside* the TopoDroid file (used to build <c>station@survey</c> refs).</summary>
    public string InnerSurveyName { get; init; } = "";
    /// <summary>File name the source survey is copied to inside <see cref="SourceDir"/> (e.g. <c>cave.th</c>).</summary>
    public string SourceFileName { get; init; } = "survey.th";
    public string Title { get; init; } = "";

    // ── georeferencing (all optional) ──
    // The live centerline/cs/fix block is emitted only when both a fix (FixC1 + FixC2) and an
    // entrance station (with InnerSurveyName) are known; callers fill EntranceStation from the
    // option the user gave or from the entrance hint read out of the survey. A fix without a
    // station is written as a commented block instead.
    public string EntranceStation { get; init; } = "";   // e.g. "25" → 25@InnerSurveyName
    public string CoordinateSystem { get; init; } = "lat-long";
    public string FixC1 { get; init; } = "";             // first coord in CS order (lat for lat-long)
    public string FixC2 { get; init; } = "";             // second coord (long for lat-long)
    public string FixC3 { get; init; } = "0";            // altitude

    // ── layout ──
    public bool IncludeLayout { get; init; } = true;
    public int Scale { get; init; } = 500;
    public bool Legend { get; init; } = true;

    // ── layout of the generated tree / outputs ──
    public string SourceDir { get; init; } = "th";
    public string OutputDir { get; init; } = "rez";
    public bool CreateGraphicsDir { get; init; }
    public string GraphicsDir { get; init; } = "grafica";
    public string OutputBaseName { get; init; } = "";    // defaults to ProjectName
    public string Encoding { get; init; } = "utf-8";

    public IReadOnlyList<ExportItem> Exports { get; init; } = Array.Empty<ExportItem>();

    /// <summary>
    /// Bare file names of the sketches (<c>.th2</c>) that belong to the survey. Each is copied into
    /// <see cref="SourceDir"/> beside the survey under the same name, because the survey inputs them
    /// by a path relative to itself. Names must not contain a directory part.
    /// </summary>
    public IReadOnlyList<string> SketchFileNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The survey already carries its own <c>fix</c> (<see cref="TopodroidSurveyHeader.HasFix"/>).
    /// The wrapper then writes no georeference of its own, and coordinates given as well
    /// (<see cref="FixC1"/>/<see cref="FixC2"/>) are refused: a second fix on another station bends
    /// the survey into a false loop that Therion compiles without an error.
    /// </summary>
    public bool SurveyHasFix { get; init; }

    private string LayoutName => "L_" + Sanitize(ProjectName);
    internal string EffectiveLayoutName => LayoutName;
    internal string EffectiveBaseName => string.IsNullOrWhiteSpace(OutputBaseName) ? Sanitize(ProjectName) : OutputBaseName.Trim();
    internal bool HasFix => !string.IsNullOrWhiteSpace(FixC1) && !string.IsNullOrWhiteSpace(FixC2);

    // A station in a survey nested inside the inner one ("5@sub") becomes 5@sub.<inner>.
    internal string EntranceRef =>
        string.IsNullOrWhiteSpace(EntranceStation) || string.IsNullOrWhiteSpace(InnerSurveyName)
            ? string.Empty
            : EntranceStation.Contains('@')
                ? $"{EntranceStation.Trim()}.{InnerSurveyName.Trim()}"
                : $"{EntranceStation.Trim()}@{InnerSurveyName.Trim()}";

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.Length == 0 ? "cave" : sb.ToString();
    }
}

/// <summary>A text file the caller should write, relative to the project root. Uses forward slashes.</summary>
public sealed record ScaffoldFile(string RelativePath, string Content);

/// <summary>
/// A file of the caller's to copy byte for byte into the project: <paramref name="SourceFileName"/>
/// is the name the caller passed in <see cref="ScaffoldOptions"/> (the survey or one sketch), and
/// <paramref name="RelativePath"/> is where it goes (project-root-relative, forward slashes).
/// </summary>
public sealed record ScaffoldSourceCopy(string RelativePath, string SourceFileName);

/// <summary>
/// The file (or, for shapefile exports, the directory) one <see cref="ExportItem"/> makes Therion
/// write — <paramref name="RelativePath"/> is relative to the project root (the compile's working
/// directory) with forward slashes, exactly as it appears after <c>-o</c> in the thconfig and in
/// Therion's log.
/// </summary>
public sealed record ScaffoldExpectedOutput(ExportItem Export, string RelativePath);

/// <summary>
/// Everything the caller needs to materialise the project: directories to create, text files to
/// write, and where to place a copy of the source survey. All paths are project-root-relative and
/// use forward slashes (Therion accepts them on every platform).
/// </summary>
public sealed record ScaffoldPlan(
    IReadOnlyList<string> Directories,
    IReadOnlyList<ScaffoldFile> Files,
    string SourceCopyRelativePath,
    string ThconfigRelativePath)
{
    /// <summary>
    /// Every file to copy in, the survey first (its destination is <see cref="SourceCopyRelativePath"/>)
    /// followed by each sketch in the order given.
    /// </summary>
    public IReadOnlyList<ScaffoldSourceCopy> SourceCopies { get; init; } = Array.Empty<ScaffoldSourceCopy>();

    /// <summary>
    /// What the thconfig's exports will write, one entry per <see cref="ScaffoldOptions.Exports"/>
    /// item in order. Reconcile it with the log's written outputs (Therion.Build's
    /// <c>OutputReconciler</c> over <c>TherionLogSummary.OutputFiles</c>) to learn which were
    /// produced and which are missing.
    /// </summary>
    public IReadOnlyList<ScaffoldExpectedOutput> ExpectedOutputs { get; init; } = Array.Empty<ScaffoldExpectedOutput>();
}

public static class TopodroidProjectScaffold
{
    /// <summary>Sniffs the survey name, title and (if present, even in a comment) an entrance hint.</summary>
    public static SourceSurveyInfo Parse(string surveyText)
    {
        string name = "", title = "", entrance = "";
        // First non-comment `survey <name> …` line.
        foreach (var raw in surveyText.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.Length == 0 || line[0] == '#') continue;
            var m = Regex.Match(line, @"^survey\s+(\S+)");
            if (m.Success) { name = m.Groups[1].Value; title = ExtractTitle(line); break; }
        }
        // Entrance hint: scan the whole text (TopoDroid often leaves `-entrance N@survey` commented out).
        var em = Regex.Match(surveyText, @"-entrance\s+([^\s@#]+)");
        if (em.Success) entrance = em.Groups[1].Value;
        return new SourceSurveyInfo(name, title, entrance);
    }

    private static string ExtractTitle(string surveyLine)
    {
        // Inside a quoted string Therion writes a quote as "".
        var q = Regex.Match(surveyLine, "-title\\s+\"((?:[^\"]|\"\")*)\"");
        if (q.Success) return q.Groups[1].Value.Replace("\"\"", "\"");
        var bare = Regex.Match(surveyLine, @"-title\s+(\S+)");
        return bare.Success ? bare.Groups[1].Value : string.Empty;
    }

    /// <summary>The wrapper survey that inputs the TopoDroid file and (optionally) georeferences it.</summary>
    public static string BuildConnectionTh(ScaffoldOptions o)
    {
        ValidateOptions(o);
        var sb = new StringBuilder();
        sb.Append("encoding ").Append(o.Encoding).Append("\n\n");

        sb.Append("survey ").Append(ScaffoldOptions.Sanitize(o.ProjectName));
        if (!string.IsNullOrWhiteSpace(o.Title)) sb.Append(" -title \"").Append(o.Title.Trim().Replace("\"", "\"\"")).Append('"');
        if (!string.IsNullOrEmpty(o.EntranceRef)) sb.Append(" -entrance ").Append(o.EntranceRef);
        sb.Append("\n\n");

        // Quoted so a file name with spaces (TopoDroid names files after the survey title) stays one path.
        sb.Append("  input \"").Append(o.SourceDir).Append('/').Append(o.SourceFileName).Append("\"\n\n");

        // A fix needs a station to pin. Without a known entrance there is nothing truthful to write
        // (a placeholder name makes Therion stop with "station does not exist"), so the block stays
        // commented — with the coordinates filled in when they were given — for a person to finish.
        var c3 = string.IsNullOrWhiteSpace(o.FixC3) ? "0" : o.FixC3.Trim();
        if (o.SurveyHasFix)
        {
            sb.Append("  # The survey is georeferenced by its own fix, so the wrapper adds none.\n\n");
        }
        else if (o.HasFix && !string.IsNullOrEmpty(o.EntranceRef))
        {
            sb.Append("  centerline\n");
            sb.Append("    cs ").Append(o.CoordinateSystem).Append('\n');
            sb.Append("    fix ").Append(o.EntranceRef).Append(' ')
              .Append(o.FixC1.Trim()).Append(' ').Append(o.FixC2.Trim()).Append(' ')
              .Append(c3).Append('\n');
            sb.Append("  endcenterline\n\n");
        }
        else if (o.HasFix)
        {
            sb.Append("  # Coordinates were given but no entrance station is known, so there is no station\n");
            sb.Append("  # to fix them to. Replace STATION with the entrance station's name and uncomment.\n");
            sb.Append("  # centerline\n");
            sb.Append("  #   cs ").Append(o.CoordinateSystem).Append('\n');
            sb.Append("  #   fix STATION@").Append(o.InnerSurveyName).Append(' ')
              .Append(o.FixC1.Trim()).Append(' ').Append(o.FixC2.Trim()).Append(' ').Append(c3).Append('\n');
            sb.Append("  # endcenterline\n\n");
        }
        else
        {
            sb.Append("  # Georeference the cave (required for KML / shapefile / GIS export):\n");
            sb.Append("  # uncomment and fill in the entrance coordinates.\n");
            sb.Append("  # centerline\n");
            sb.Append("  #   cs ").Append(o.CoordinateSystem).Append('\n');
            sb.Append("  #   fix STATION@").Append(o.InnerSurveyName).Append(" <lat> <long> <altitude>\n");
            sb.Append("  # endcenterline\n\n");
        }

        sb.Append("endsurvey\n");
        return sb.ToString();
    }

    /// <summary>The thconfig: source line, optional layout, and one line per chosen export.</summary>
    public static string BuildThconfig(ScaffoldOptions o)
    {
        ValidateOptions(o);
        var sb = new StringBuilder();
        sb.Append("encoding ").Append(o.Encoding).Append('\n');
        sb.Append("source ").Append(ScaffoldOptions.Sanitize(o.ProjectName)).Append(".th\n\n");

        if (o.IncludeLayout)
        {
            sb.Append("layout ").Append(o.EffectiveLayoutName).Append('\n');
            sb.Append("  scale 1 ").Append(o.Scale.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  legend ").Append(o.Legend ? "on" : "off").Append('\n');
            sb.Append("endlayout\n\n");
        }

        var baseName = o.EffectiveBaseName;
        var outDir = o.OutputDir;
        var layoutName = o.IncludeLayout ? o.EffectiveLayoutName : string.Empty;

        var models = new List<string>();
        var maps = new List<string>();
        var other = new List<string>();
        foreach (var e in o.Exports)
        {
            var line = FormatExport(e, baseName, outDir, layoutName);
            (e.Kind is ExportKind.Model ? models
                : e.Kind is ExportKind.Map or ExportKind.Atlas ? maps
                : other).Add(line);
        }

        AppendSection(sb, "# 3D model exports", models);
        AppendSection(sb, "# 2D map exports", maps);
        AppendSection(sb, "# other exports", other);
        return sb.ToString();
    }

    /// <summary>
    /// Builds the full plan (dirs + files + where to copy the survey and its sketches) for the caller
    /// to execute. Throws <see cref="ArgumentException"/> when an option would not make a sound
    /// project (see <see cref="ValidateOptions"/>), when the survey or a sketch name has a directory
    /// part, or when two of them would be copied to the same place.
    /// </summary>
    public static ScaffoldPlan BuildPlan(ScaffoldOptions o)
    {
        ValidateOptions(o);
        var dirs = new List<string> { o.SourceDir, o.OutputDir };
        if (o.CreateGraphicsDir) dirs.Add(o.GraphicsDir);

        var connName = ScaffoldOptions.Sanitize(o.ProjectName) + ".th";
        var thconfigName = "thconfig.thc";
        var files = new List<ScaffoldFile>
        {
            new(connName, BuildConnectionTh(o)),
            new(thconfigName, BuildThconfig(o)),
        };
        var sourceCopy = o.SourceDir + "/" + o.SourceFileName;
        var copies = new List<ScaffoldSourceCopy> { new(sourceCopy, o.SourceFileName) };
        foreach (var sketch in o.SketchFileNames)
            copies.Add(new ScaffoldSourceCopy(o.SourceDir + "/" + sketch, sketch));
        return new ScaffoldPlan(dirs, files, sourceCopy, thconfigName)
        {
            SourceCopies = copies,
            ExpectedOutputs = ExpectedOutputs(o),
        };
    }

    /// <summary>The output path each export writes, in the order of <see cref="ScaffoldOptions.Exports"/>.</summary>
    public static IReadOnlyList<ScaffoldExpectedOutput> ExpectedOutputs(ScaffoldOptions o)
    {
        ValidateOptions(o);
        var list = new List<ScaffoldExpectedOutput>(o.Exports.Count);
        foreach (var e in o.Exports)
            list.Add(new ScaffoldExpectedOutput(e, OutputPath(e, o.EffectiveBaseName, o.OutputDir)));
        return list;
    }


    /// <summary>
    /// Throws <see cref="ArgumentException"/> when an option cannot be written into the wrapper and
    /// the thconfig as the plain value it is meant to be. Every value lands in a Therion command
    /// line, and the thconfig is a script Therion runs (it has a <c>system</c> command), so a line
    /// break in any of them, or a quote in a quoted one, would add a command of the caller's
    /// choosing; a directory that climbs out (<c>..</c>) or is rooted would make Therion — and the
    /// caller creating <see cref="ScaffoldPlan.Directories"/> — write outside the project. Values
    /// are checked against what Therion accepts there rather than escaped, except the title, whose
    /// quotes are written as Therion's <c>""</c>. Called by every builder here, so a server can rely
    /// on it whichever it calls.
    /// </summary>
    public static void ValidateOptions(ScaffoldOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        void Fail(string what, string value) =>
            throw new ArgumentException($"{what} '{value}' cannot be written into the project.", nameof(o));

        foreach (var (what, value) in new[]
                 {
                     ("ProjectName", o.ProjectName), ("InnerSurveyName", o.InnerSurveyName), ("Title", o.Title),
                     ("EntranceStation", o.EntranceStation), ("CoordinateSystem", o.CoordinateSystem),
                     ("FixC1", o.FixC1), ("FixC2", o.FixC2), ("FixC3", o.FixC3), ("SourceDir", o.SourceDir),
                     ("OutputDir", o.OutputDir), ("GraphicsDir", o.GraphicsDir), ("OutputBaseName", o.OutputBaseName),
                     ("Encoding", o.Encoding),
                 })
            if (value is null || value.Any(char.IsControl)) Fail(what, value ?? "");

        if (!ScaffoldValueRules.IsEncoding(o.Encoding)) Fail("Encoding", o.Encoding);
        if (!ScaffoldValueRules.IsCoordinateSystem(o.CoordinateSystem.Trim())) Fail("CoordinateSystem", o.CoordinateSystem);
        if (!string.IsNullOrWhiteSpace(o.InnerSurveyName) && !ScaffoldValueRules.IsSurveyName(o.InnerSurveyName.Trim()))
            Fail("InnerSurveyName", o.InnerSurveyName);
        if (!string.IsNullOrWhiteSpace(o.EntranceStation) && !ScaffoldValueRules.IsStationRef(o.EntranceStation.Trim()))
            Fail("EntranceStation", o.EntranceStation);
        foreach (var (what, value) in new[] { ("FixC1", o.FixC1), ("FixC2", o.FixC2), ("FixC3", o.FixC3) })
            if (!string.IsNullOrWhiteSpace(value)
                && !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                Fail(what, value);

        if (!IsProjectRelativeDir(o.SourceDir, allowDot: true)) Fail("SourceDir", o.SourceDir);
        if (!IsProjectRelativeDir(o.OutputDir, allowDot: false)) Fail("OutputDir", o.OutputDir);
        if (o.CreateGraphicsDir && !IsProjectRelativeDir(o.GraphicsDir, allowDot: false)) Fail("GraphicsDir", o.GraphicsDir);
        if (!string.IsNullOrWhiteSpace(o.OutputBaseName)
            && (!ScaffoldValueRules.IsBaseName(o.OutputBaseName.Trim()) || o.OutputBaseName.Trim() is "." or ".."))
            Fail("OutputBaseName", o.OutputBaseName);

        foreach (var e in o.Exports)
        {
            if (!ScaffoldValueRules.IsOptionToken(e.Fmt ?? "") || !ScaffoldValueRules.IsOptionToken(e.Projection ?? "")
                || !ScaffoldValueRules.IsOptionToken(e.WallSource ?? "") || !ScaffoldValueRules.IsExtension(e.Extension ?? ""))
                Fail("Export", $"{e.Kind} {e.Fmt} {e.Projection} {e.WallSource} {e.Extension}");
        }

        if (o.SurveyHasFix && o.HasFix)
            throw new ArgumentException(
                "The survey is already georeferenced by its own fix; giving coordinates as well would add a second fix.",
                nameof(o));

        ValidateCopyNames(o);
    }

    private static bool IsProjectRelativeDir(string dir, bool allowDot) => ScaffoldValueRules.IsProjectRelativeDir(dir, allowDot);

    // Copied names land in SourceDir as given, so a directory part ("../x", "a/b.th2") would place a
    // file outside it, and two names differing only in case collide on a case-insensitive disk. The
    // survey's name is written inside a quoted `input`, so a quote or a line break would end it.
    private static void ValidateCopyNames(ScaffoldOptions o)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { o.SourceFileName }.Concat(o.SketchFileNames))
        {
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".."
                || name.IndexOfAny(new[] { '/', '\\', '"' }) >= 0 || name.Contains(':') || name.Any(char.IsControl))
                throw new ArgumentException($"'{name}' is not a bare file name.", nameof(o));
            if (!seen.Add(name))
                throw new ArgumentException($"'{name}' is listed more than once.", nameof(o));
        }
    }

    /// <summary>
    /// Generated files whose destination under <paramref name="targetRoot"/> is the source survey
    /// itself. Scaffolding into the survey's own directory makes the wrapper <c>&lt;project&gt;.th</c>
    /// land on <paramref name="sourcePath"/> — the survey is derived from that name — which would
    /// replace the survey data with a handful of `input` lines. Callers must refuse the plan when
    /// this returns anything.
    /// </summary>
    public static IReadOnlyList<string> ConflictsWithSource(ScaffoldPlan plan, string targetRoot, string sourcePath)
        => ConflictsWithSources(plan, targetRoot, new[] { sourcePath });

    /// <summary>
    /// Like <see cref="ConflictsWithSource"/> for the survey and its sketches together: the generated
    /// files whose destination is one of <paramref name="sourcePaths"/>, plus the generated files a
    /// copied survey or sketch would land on. Callers must refuse the plan when this returns anything.
    /// </summary>
    public static IReadOnlyList<string> ConflictsWithSources(ScaffoldPlan plan, string targetRoot, IEnumerable<string> sourcePaths)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in sourcePaths) sources.Add(Path.GetFullPath(p));
        var copies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in plan.SourceCopies) copies.Add(Path.GetFullPath(Path.Combine(targetRoot, c.RelativePath)));

        var hits = new List<string>();
        foreach (var f in plan.Files)
        {
            var dest = Path.GetFullPath(Path.Combine(targetRoot, f.RelativePath));
            if (sources.Contains(dest) || copies.Contains(dest)) hits.Add(f.RelativePath);
        }
        return hits;
    }

    private static void AppendSection(StringBuilder sb, string header, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        sb.Append(header).Append('\n');
        foreach (var l in lines) sb.Append(l).Append('\n');
        sb.Append('\n');
    }

    private static string FormatExport(ExportItem e, string baseName, string outDir, string layoutName)
    {
        var sb = new StringBuilder();
        sb.Append("export ").Append(KindWord(e.Kind));
        if (e.Kind is ExportKind.Map or ExportKind.Atlas && !string.IsNullOrEmpty(e.Projection))
            sb.Append(" -projection ").Append(e.Projection);
        if (!string.IsNullOrEmpty(e.Fmt)) sb.Append(" -fmt ").Append(e.Fmt);
        if (e.UseLayout && !string.IsNullOrEmpty(layoutName)) sb.Append(" -layout ").Append(layoutName);
        if (!string.IsNullOrEmpty(e.WallSource)) sb.Append(" -wall-source ").Append(e.WallSource);
        sb.Append(" -o \"").Append(OutputPath(e, baseName, outDir)).Append('"');
        return sb.ToString();
    }

    private static string OutputPath(ExportItem e, string baseName, string outDir)
        => outDir + "/" + OutputName(e, baseName);

    private static string OutputName(ExportItem e, string baseName)
    {
        var suffix = e.Kind is ExportKind.Map or ExportKind.Atlas && !string.IsNullOrEmpty(e.Projection)
            ? "-" + e.Projection
            : string.Empty;
        return baseName + suffix + e.Extension;
    }

    private static string KindWord(ExportKind k) => k switch
    {
        ExportKind.Model => "model",
        ExportKind.Map => "map",
        ExportKind.Atlas => "atlas",
        ExportKind.Database => "database",
        ExportKind.SurveyList => "survey-list",
        ExportKind.CaveList => "cave-list",
        ExportKind.ContinuationList => "continuation-list",
        _ => "model",
    };
}
