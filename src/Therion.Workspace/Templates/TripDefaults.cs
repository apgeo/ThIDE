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
    /// Reads the defaults from the workspace. <paramref name="readText"/> gives a file's text (the
    /// editor buffer when open).
    /// </summary>
    public static TripDefaults From(WorkspaceSemanticModel ws, Func<string, string?> readText)
    {
        string? bestFile = null, bestDate = null, bestText = null;
        int bestOffset = 0;
        foreach (var path in ws.PerFile.Keys.Where(p => p.EndsWith(".th", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (readText(path) is not { } text) continue;
            var ast = new ThParser().Parse(path, text).Value;
            foreach (var date in Dates(ast?.Children ?? []))
            {
                var key = SortableDate(date.Value);
                if (key is null || bestDate is not null && string.CompareOrdinal(key, bestDate) <= 0) continue;
                (bestFile, bestDate, bestText) = (path, key, text);
                // Read the commands of the centreline that holds this date: everything up to its end.
                bestOffset = text.IndexOf("endcentreline", date.Span.StartOffset, StringComparison.OrdinalIgnoreCase) is var e and >= 0 ? e
                           : text.IndexOf("endcenterline", date.Span.StartOffset, StringComparison.OrdinalIgnoreCase) is var e2 and >= 0 ? e2
                           : text.Length;
            }
        }

        var members = DataAnalytics.TeamMembers(ws).Select(m => m.Name).ToList();
        if (bestFile is null) return Empty with { TeamMembers = members };

        var vars = SnippetVariables.Standard(new SnippetContext(bestFile, bestText!, bestOffset, DateTime.Now));
        return new TripDefaults(vars("LAST_UNITS"), vars("LAST_CALIBRATE"), vars("LAST_INSTRUMENT"), vars("LAST_DATA"),
            vars("TEAM_RECENT"), bestFile, bestDate, members);
    }

    private static IEnumerable<DateCommand> Dates(IEnumerable<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n is DateCommand d) yield return d;
            else if (n is BlockCommand b) foreach (var inner in Dates(b.Children)) yield return inner;
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
}
