// Reads the descriptive part of a TopoDroid-exported survey `.th`: who surveyed it and when, the
// instrument conventions, whether it is georeferenced (and on which station), its entrance, and the
// files it inputs. A server receiving the file uses this to decide what the scaffold must add (a
// fix only when the survey has none) and to check that the sketches it was handed are the ones the
// survey actually inputs.
//
// Pure: text in, record out. Only the first `survey … endsurvey` block is read for the header;
// shots and scraps are skipped, and anything unrecognised is ignored rather than reported. The
// files the text reads (`input`, `import`) are collected from the whole text, because Therion
// reads them wherever they stand.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Therion.Workspace.Import;

/// <summary>
/// What a TopoDroid survey file says about itself. List values are in file order; every value is
/// the text as written (no unit conversion). <see cref="CoordinateSystem"/> and
/// <see cref="FixStation"/> are null when the survey has no <c>cs</c> / <c>fix</c>.
/// </summary>
public sealed record TopodroidSurveyHeader(
    string SurveyName,
    string Title,
    IReadOnlyList<string> Dates,
    IReadOnlyList<string> Team,
    string? Declination,
    IReadOnlyList<string> Units,
    string? CoordinateSystem,
    string? FixStation,
    IReadOnlyList<string> FixCoordinates,
    string EntranceStation,
    bool EntranceFromComment,
    IReadOnlyList<string> Inputs)
{
    public bool HasCoordinateSystem => CoordinateSystem is not null;
    public bool HasFix => FixStation is not null;

    /// <summary>The paths of the text's <c>import</c> commands (centreline data Therion reads from another file), in file order.</summary>
    public IReadOnlyList<string> Imports { get; init; } = Array.Empty<string>();
}

public static class TopodroidSurveyHeaderReader
{
    /// <summary>
    /// Reads the header of the first survey in <paramref name="surveyText"/>. The entrance station
    /// comes from the survey's <c>-entrance</c> option, else from a <c>station … entrance</c> flag,
    /// else from a commented-out <c>-entrance</c> (TopoDroid writes one) — the last case sets
    /// <see cref="TopodroidSurveyHeader.EntranceFromComment"/>. Station references are given
    /// relative to the first survey: a reference to the survey itself loses its <c>@survey</c> part,
    /// and a station inside a nested survey keeps the path to it (<c>5@sub</c>), innermost first.
    /// <see cref="TopodroidSurveyHeader.Inputs"/> and <see cref="TopodroidSurveyHeader.Imports"/>
    /// come from every line of the text, inside the first survey or not.
    /// </summary>
    public static TopodroidSurveyHeader Read(string surveyText)
    {
        string name = "", title = "", entrance = "";
        string? declination = null, cs = null, fixStation = null, flaggedEntrance = null;
        var dates = new List<string>();
        var team = new List<string>();
        var units = new List<string>();
        var fixCoords = new List<string>();
        var inputs = new List<string>();
        var imports = new List<string>();

        var nested = new List<string>();   // names of the surveys open inside the first one, outermost first
        int depth = 0;          // survey nesting inside the first survey
        bool inCenterline = false, done = false;
        foreach (var raw in surveyText.Split('\n'))
        {
            var t = Tokenize(raw);
            if (t.Count == 0) continue;
            var cmd = t[0].ToLowerInvariant();

            if (cmd == "input" && t.Count > 1) { inputs.Add(t[1]); continue; }
            if (cmd == "import" && t.Count > 1) { imports.Add(t[1]); continue; }
            if (done) continue;

            if (depth == 0)
            {
                if (cmd != "survey" || t.Count < 2) continue;
                depth = 1;
                name = t[1];
                title = Option(t, "-title") ?? "";
                entrance = Qualify(Option(t, "-entrance") ?? "", name, nested);
                continue;
            }

            switch (cmd)
            {
                case "survey": depth++; nested.Add(t.Count > 1 ? t[1] : ""); continue;
                case "endsurvey":
                    if (--depth == 0) done = true;
                    else nested.RemoveAt(nested.Count - 1);
                    continue;
                case "centerline" or "centreline": inCenterline = true; continue;
                case "endcenterline" or "endcentreline": inCenterline = false; continue;
            }
            if (!inCenterline) continue;

            switch (cmd)
            {
                case "date" when t.Count > 1: dates.Add(string.Join(' ', t.Skip(1))); break;
                case "team" when t.Count > 1: team.Add(t[1]); break;
                case "units" when t.Count > 1: units.Add(string.Join(' ', t.Skip(1))); break;
                case "declination" when t.Count > 1: declination ??= string.Join(' ', t.Skip(1)); break;
                case "cs" when t.Count > 1: cs ??= t[1]; break;
                case "fix" when t.Count > 1 && fixStation is null:
                    fixStation = Qualify(t[1], name, nested);
                    fixCoords.AddRange(t.Skip(2));
                    break;
                case "station" when t.Count > 2 && flaggedEntrance is null
                                    && t.Skip(2).Any(x => x.Equals("entrance", StringComparison.OrdinalIgnoreCase)):
                    flaggedEntrance = Qualify(t[1], name, nested);
                    break;
            }
        }

        bool fromComment = false;
        if (entrance.Length == 0 && flaggedEntrance is not null) entrance = flaggedEntrance;
        if (entrance.Length == 0)
        {
            var hint = TopodroidProjectScaffold.Parse(surveyText).EntranceHint;
            if (hint.Length > 0) { entrance = hint; fromComment = true; }
        }

        return new TopodroidSurveyHeader(name, title, dates, team, declination, units, cs, fixStation,
            fixCoords, entrance, fromComment, inputs) { Imports = imports };
    }

    /// <summary>
    /// The <c>input</c> and <c>import</c> paths of <paramref name="header"/> that could reach a file
    /// outside the directory the survey sits in: a rooted path, a drive or UNC prefix, a backslash,
    /// a <c>..</c> segment, or any directory part at all (TopoDroid writes bare names; a sketch sits
    /// beside its survey). Therion reads whatever these name, and the content can come back in its
    /// log or its exports, so a server must refuse an uploaded survey — and each uploaded sketch,
    /// read the same way — when this returns anything.
    /// </summary>
    public static IReadOnlyList<string> UnsafeInputs(TopodroidSurveyHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Inputs.Concat(header.Imports).Where(IsUnsafeInputPath).ToList();
    }

    private static bool IsUnsafeInputPath(string path)
    {
        var p = path.Trim();
        return p.Length == 0 || p is "." or ".."
            || p.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
            || Path.IsPathRooted(p)
            || p.Any(char.IsControl);
    }

    /// <summary>
    /// The names in <paramref name="sketchFileNames"/> that <paramref name="header"/>'s survey does
    /// not input. Such a sketch is copied into the project but Therion never reads it, so its scraps
    /// are missing from the maps. An <c>input</c> may omit the <c>.th2</c> extension.
    /// </summary>
    public static IReadOnlyList<string> SketchesNotInput(TopodroidSurveyHeader header, IEnumerable<string> sketchFileNames)
    {
        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in header.Inputs)
        {
            var file = Path.GetFileName(i.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
            inputNames.Add(file);
            inputNames.Add(file + ".th2");
        }
        return sketchFileNames.Where(s => !inputNames.Contains(s)).ToList();
    }

    private static string? Option(IReadOnlyList<string> t, string option)
    {
        for (int i = 2; i < t.Count - 1; i++)
            if (t[i].Equals(option, StringComparison.OrdinalIgnoreCase)) return t[i + 1];
        return null;
    }

    // A station reference written inside the nested surveys `nested` (outermost first), made
    // relative to the first survey `root`: 5 inside sub → 5@sub, 5@deeper inside sub → 5@deeper.sub.
    // A reference that ends with the first survey's own name drops that part (2@s inside s → 2).
    private static string Qualify(string station, string root, IReadOnlyList<string> nested)
    {
        if (station.Length == 0) return station;
        var path = string.Join('.', nested.Reverse());
        string qualified = path.Length == 0 ? station
            : station.Contains('@') ? station + "." + path
            : station + "@" + path;
        if (root.Length > 0)
        {
            if (qualified.EndsWith("@" + root, StringComparison.Ordinal)) return qualified[..^(root.Length + 1)];
            if (qualified.EndsWith("." + root, StringComparison.Ordinal)) return qualified[..^(root.Length + 1)];
        }
        return qualified;
    }

    // Whitespace-separated tokens; a double-quoted string is one token (quotes removed, "" inside
    // it is a literal quote, Therion's escape). A '#' outside quotes starts a comment.
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false, any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
                continue;
            }
            if (c == '#') break;
            if (c == '"') { inQuotes = true; any = true; continue; }
            if (char.IsWhiteSpace(c))
            {
                if (any) { tokens.Add(sb.ToString()); sb.Clear(); any = false; }
                continue;
            }
            sb.Append(c);
            any = true;
        }
        if (any) tokens.Add(sb.ToString());
        return tokens;
    }
}
