using System.Globalization;

namespace Therion.Semantics;

/// <summary>
/// A Therion survey date as a closed [Min, Max] day interval (CQ-03). A partial date is the whole
/// interval it names — <c>2000</c> is 1 Jan…31 Dec 2000, <c>2000.07</c> is the whole of July — so a
/// range filter can ask a single question ("does this overlap 2000…2005?") whatever precision the
/// surveyor wrote. Both ends inclusive.
/// </summary>
public readonly record struct TherionDateInterval(DateOnly Min, DateOnly Max)
{
    /// <summary>True when this interval shares at least one day with <paramref name="other"/>.</summary>
    public bool Overlaps(TherionDateInterval other) => Min <= other.Max && other.Min <= Max;

    /// <summary>True when this interval shares at least one day with [<paramref name="from"/>, <paramref name="to"/>].</summary>
    public bool Overlaps(DateOnly from, DateOnly to) => Min <= to && from <= Max;
}

/// <summary>
/// Parses Therion date strings — <c>YYYY</c>, <c>YYYY.MM</c>, <c>YYYY.MM.DD</c>, and intervals
/// <c>a - b</c> — to day intervals. Time-of-day (<c>@HH:MM:SS</c>) is ignored; a partial date expands to
/// the whole span it names. Tolerant of the tokenizer's artefact spaces around the dots (the value can
/// arrive as "2024.07 .01"). Never throws: anything unparseable is <c>null</c>.
/// </summary>
public static class TherionDate
{
    /// <summary>The interval a single date string names, or null when it doesn't parse.</summary>
    public static TherionDateInterval? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Strip every space/tab (tokenizer artefacts around dots) so only the meaningful punctuation
        // remains; the interval dash and the '@' time separator survive because they aren't whitespace.
        var compact = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0) return null;

        int dash = compact.IndexOf('-');
        if (dash > 0)
        {
            var start = ParseSingle(compact[..dash]);
            var end = ParseSingle(compact[(dash + 1)..]);
            return start is { } s && end is { } e ? new TherionDateInterval(s.Min, e.Max) : null;
        }

        return ParseSingle(compact);
    }

    /// <summary>The interval spanning every parseable date in <paramref name="dates"/> (min of mins to
    /// max of maxes), or null when none parse — a survey's overall date extent.</summary>
    public static TherionDateInterval? Span(IEnumerable<string>? dates)
    {
        if (dates is null) return null;
        TherionDateInterval? span = null;
        foreach (var d in dates)
        {
            if (Parse(d) is not { } i) continue;
            span = span is { } cur
                ? new TherionDateInterval(cur.Min < i.Min ? cur.Min : i.Min, cur.Max > i.Max ? cur.Max : i.Max)
                : i;
        }
        return span;
    }

    private static TherionDateInterval? ParseSingle(string token)
    {
        int at = token.IndexOf('@');            // drop time-of-day
        if (at >= 0) token = token[..at];
        if (token.Length == 0) return null;

        var parts = token.Split('.');
        if (parts.Length is < 1 or > 3) return null;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || year is < 1 or > 9999)
            return null;

        int? month = null, day = null;
        if (parts.Length >= 2)
        {
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 1 or > 12)
                return null;
            month = m;
        }
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                || d < 1 || d > DateTime.DaysInMonth(year, month!.Value))
                return null;
            day = d;
        }

        var min = new DateOnly(year, month ?? 1, day ?? 1);
        var max = day is { } dd
            ? new DateOnly(year, month!.Value, dd)
            : month is { } mm
                ? new DateOnly(year, mm, DateTime.DaysInMonth(year, mm))
                : new DateOnly(year, 12, 31);
        return new TherionDateInterval(min, max);
    }
}
