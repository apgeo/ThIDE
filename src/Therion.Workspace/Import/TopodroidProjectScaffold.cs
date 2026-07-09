// Project scaffolding for a bare TopoDroid-exported survey `.th`. TopoDroid emits a single
// `survey … centerline … endcenterline … endsurvey` file with no thconfig and no wrapper, so it
// cannot be compiled on its own. Given that file (and a few user choices) this builds the pieces a
// Therion project needs: a directory tree, a wrapper "connection" `.th` that inputs the survey and
// (optionally) georeferences it, and a thconfig with the chosen `export` commands.
//
// Pure string/plan output — no file I/O here (that belongs to the caller, per the core-lib rules).
// The caller creates the directories, copies the source survey into place, and writes each file.

using System;
using System.Collections.Generic;
using System.Globalization;
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

    // ── georeferencing (all optional; emitted only when a fix is supplied) ──
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

    private string LayoutName => "L_" + Sanitize(ProjectName);
    internal string EffectiveLayoutName => LayoutName;
    internal string EffectiveBaseName => string.IsNullOrWhiteSpace(OutputBaseName) ? Sanitize(ProjectName) : OutputBaseName.Trim();
    internal bool HasFix => !string.IsNullOrWhiteSpace(FixC1) && !string.IsNullOrWhiteSpace(FixC2);

    internal string EntranceRef =>
        string.IsNullOrWhiteSpace(EntranceStation) || string.IsNullOrWhiteSpace(InnerSurveyName)
            ? string.Empty
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
/// Everything the caller needs to materialise the project: directories to create, text files to
/// write, and where to place a copy of the source survey. All paths are project-root-relative and
/// use forward slashes (Therion accepts them on every platform).
/// </summary>
public sealed record ScaffoldPlan(
    IReadOnlyList<string> Directories,
    IReadOnlyList<ScaffoldFile> Files,
    string SourceCopyRelativePath,
    string ThconfigRelativePath);

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
        var q = Regex.Match(surveyLine, "-title\\s+\"([^\"]*)\"");
        if (q.Success) return q.Groups[1].Value;
        var bare = Regex.Match(surveyLine, @"-title\s+(\S+)");
        return bare.Success ? bare.Groups[1].Value : string.Empty;
    }

    /// <summary>The wrapper survey that inputs the TopoDroid file and (optionally) georeferences it.</summary>
    public static string BuildConnectionTh(ScaffoldOptions o)
    {
        var sb = new StringBuilder();
        sb.Append("encoding ").Append(o.Encoding).Append("\n\n");

        sb.Append("survey ").Append(ScaffoldOptions.Sanitize(o.ProjectName));
        if (!string.IsNullOrWhiteSpace(o.Title)) sb.Append(" -title \"").Append(o.Title.Trim()).Append('"');
        if (!string.IsNullOrEmpty(o.EntranceRef)) sb.Append(" -entrance ").Append(o.EntranceRef);
        sb.Append("\n\n");

        sb.Append("  input ").Append(o.SourceDir).Append('/').Append(o.SourceFileName).Append("\n\n");

        if (o.HasFix)
        {
            sb.Append("  centerline\n");
            sb.Append("    cs ").Append(o.CoordinateSystem).Append('\n');
            var station = string.IsNullOrEmpty(o.EntranceRef) ? "STATION@" + o.InnerSurveyName : o.EntranceRef;
            sb.Append("    fix ").Append(station).Append(' ')
              .Append(o.FixC1.Trim()).Append(' ').Append(o.FixC2.Trim()).Append(' ')
              .Append(string.IsNullOrWhiteSpace(o.FixC3) ? "0" : o.FixC3.Trim()).Append('\n');
            sb.Append("  endcenterline\n\n");
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

    /// <summary>Builds the full plan (dirs + files + where to copy the source) for the caller to execute.</summary>
    public static ScaffoldPlan BuildPlan(ScaffoldOptions o)
    {
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
        return new ScaffoldPlan(dirs, files, sourceCopy, thconfigName);
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
        sb.Append(" -o \"").Append(outDir).Append('/').Append(OutputName(e, baseName)).Append('"');
        return sb.ToString();
    }

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
