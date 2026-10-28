// What a new trip most likely looks like: the units, calibration, instruments and data order of the
// most recent dated trip in the project, and the people who have surveyed in it.

using System.Globalization;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Workspace.Templates;

/// <param name="SourceFile">The file the readings were taken from (the most recently dated one).</param>
/// <param name="TeamMembers">Everyone named in a <c>team</c> line, most active first.</param>
public sealed record TripDefaults(
    string? Units, string? Calibrate, string? Instrument, string? Data, string? Team,
    string? SourceFile, string? LatestDate, IReadOnlyList<string> TeamMembers)
{
    public static TripDefaults Empty { get; } = new(null, null, null, null, null, null, null, []);

    /// <summary>
    /// Reads the defaults from the workspace: the centreline holding the latest <c>date</c> gives
    /// its units, calibrations and instruments (every line of each), its last data order and its team.
    /// </summary>
    /// <param name="readText">A file's text (the editor buffer when open).</param>
    /// <param name="ast">A file's parse, when the caller already has one (else it is parsed here).</param>
    public static TripDefaults From(WorkspaceSemanticModel ws, Func<string, string?> readText, Func<string, TherionFile?>? ast = null)
    {
        string? bestFile = null, bestDate = null, bestText = null;
        BlockCommand? bestBlock = null;
        foreach (var path in ws.PerFile.Keys.Where(p => p.EndsWith(".th", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (readText(path) is not { } text) continue;
            var tree = ast?.Invoke(path) ?? new ThParser().Parse(path, text).Value;
            foreach (var (block, date) in DatedBlocks(tree?.Children ?? []))
            {
                var key = SortableDate(date.Value);
                if (key is null || bestDate is not null && string.CompareOrdinal(key, bestDate) <= 0) continue;
                (bestFile, bestDate, bestText, bestBlock) = (path, key, text, block);
            }
        }

        var members = DataAnalytics.TeamMembers(ws).Select(m => m.Name).ToList();
        if (bestBlock is null) return Empty with { TeamMembers = members };

        var commands = bestBlock.Children.OfType<TherionCommand>().ToList();
        string? Lines(string keyword, bool lastOnly = false)
        {
            var hits = commands.Where(c => string.Equals(c.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
                .Select(c => Text(bestText!, c)).ToList();
            if (hits.Count == 0) return null;
            return lastOnly ? hits[^1] : string.Join("\n", hits);
        }
        return new TripDefaults(Lines("units"), Lines("calibrate"), Lines("instrument"), Lines("data", lastOnly: true),
            Lines("team"), bestFile, bestDate, members);
    }

    private static string Text(string text, TherionNode node)
    {
        int start = node.Span.StartOffset, end = Math.Min(text.Length, start + node.Span.Length);
        return text[start..end].TrimEnd();
    }

    // Each centreline with the date line inside it.
    private static IEnumerable<(BlockCommand Block, DateCommand Date)> DatedBlocks(IEnumerable<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n is not BlockCommand b) continue;
            if (b is CentrelineCommand)
                foreach (var d in b.Children.OfType<DateCommand>()) yield return (b, d);
            foreach (var inner in DatedBlocks(b.Children)) yield return inner;
        }
    }

    /// <summary>
    /// The first date of a <c>date</c> value as yyyy.MM.dd (padded), for ordering; null when it does
    /// not start with a year. Therion dates may carry a time or a range (<c>2026.10.21@10:00 - …</c>).
    /// </summary>
    public static string? SortableDate(string value)
    {
        var first = value.Trim().Split([' ', '@', '-'], 2)[0];
        var parts = first.Split('.');
        if (parts.Length == 0 || parts[0].Length != 4 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return null;
        string Part(int i) => i < parts.Length && int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n.ToString("00", CultureInfo.InvariantCulture) : "00";
        return $"{parts[0]}.{Part(1)}.{Part(2)}";
    }

    /// <summary>
    /// True for a value Therion accepts after <c>date</c>: <c>yyyy[.mm[.dd[@hh[:mm[:ss[.ss]]]]]]</c>,
    /// or two of them as a range (<c>2026.10.27 - 2026.10.28</c>), with real months, days and times.
    /// </summary>
    public static bool IsDate(string value)
    {
        var parts = value.Trim().Split(" - ", 3, StringSplitOptions.TrimEntries);
        return parts.Length <= 2 && parts.All(IsOneDate);

        static bool IsOneDate(string d)
        {
            var m = DatePattern.Match(d);
            if (!m.Success) return false;
            static bool In(System.Text.RegularExpressions.Group g, int lo, int hi) =>
                !g.Success || int.Parse(g.Value, CultureInfo.InvariantCulture) is var n && n >= lo && n <= hi;
            return In(m.Groups["mo"], 1, 12) && In(m.Groups["d"], 1, 31)
                && In(m.Groups["h"], 0, 23) && In(m.Groups["mi"], 0, 59) && In(m.Groups["s"], 0, 59);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex DatePattern = new(
        @"^[0-9]{4}(\.(?<mo>[0-9]{1,2})(\.(?<d>[0-9]{1,2})(@(?<h>[0-9]{1,2})(:(?<mi>[0-9]{1,2})(:(?<s>[0-9]{1,2})(\.[0-9]+)?)?)?)?)?)?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
