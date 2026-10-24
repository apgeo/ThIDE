// Reads the descriptive part of a TopoDroid-exported survey `.th`: who surveyed it and when, the
// instrument conventions, whether it is georeferenced (and on which station), its entrance, and the
// files it inputs. A server receiving the file uses this to decide what the scaffold must add (a
// fix only when the survey has none) and to check that the sketches it was handed are the ones the
// survey actually inputs.
//
// Pure: text in, record out. Only the first `survey … endsurvey` block is read; shots and scraps
// are skipped, and anything unrecognised is ignored rather than reported.

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
}

public static class TopodroidSurveyHeaderReader
{
    /// <summary>
    /// Reads the header of the first survey in <paramref name="surveyText"/>. The entrance station
    /// comes from the survey's <c>-entrance</c> option, else from a <c>station … entrance</c> flag,
    /// else from a commented-out <c>-entrance</c> (TopoDroid writes one) — the last case sets
    /// <see cref="TopodroidSurveyHeader.EntranceFromComment"/>. A station reference's <c>@survey</c>
    /// part is dropped.
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

        int depth = 0;          // survey nesting inside the first survey
        bool inCenterline = false, done = false;
        foreach (var raw in surveyText.Split('\n'))
        {
            if (done) break;
            var t = Tokenize(raw);
            if (t.Count == 0) continue;
            var cmd = t[0].ToLowerInvariant();

            if (depth == 0)
            {
                if (cmd != "survey" || t.Count < 2) continue;
                depth = 1;
                name = t[1];
                title = Option(t, "-title") ?? "";
                entrance = StripSurvey(Option(t, "-entrance") ?? "");
                continue;
            }

            switch (cmd)
            {
                case "survey": depth++; continue;
                case "endsurvey": if (--depth == 0) done = true; continue;
                case "centerline" or "centreline": inCenterline = true; continue;
                case "endcenterline" or "endcentreline": inCenterline = false; continue;
                case "input" when !inCenterline && t.Count > 1: inputs.Add(t[1]); continue;
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
                    fixStation = StripSurvey(t[1]);
                    fixCoords.AddRange(t.Skip(2));
                    break;
                case "station" when t.Count > 2 && flaggedEntrance is null
                                    && t.Skip(2).Any(x => x.Equals("entrance", StringComparison.OrdinalIgnoreCase)):
                    flaggedEntrance = StripSurvey(t[1]);
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
            fixCoords, entrance, fromComment, inputs);
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

    private static string StripSurvey(string station)
    {
        var at = station.IndexOf('@');
        return at < 0 ? station : station[..at];
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
