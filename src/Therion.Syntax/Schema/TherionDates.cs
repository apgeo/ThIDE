// C7 — Therion date/time grammar (thdate.cxx::parse, B7-verified):
//   <date> = YYYY[.MM[.DD[@HH[:MM[:SS[.S…]]]]]]   (month 1–12, day 1–31, hour ≤23, min/sec ≤59)
//   <spec> = "-" (empty date) | <date> | <date> - <date>   (interval)

using System;
using System.Globalization;

namespace Therion.Syntax.Schema;

/// <summary>Validates Therion date specifications (thdate.cxx grammar).</summary>
public static class TherionDates
{
    /// <summary>Returns an error description, or null when <paramref name="spec"/> is valid.</summary>
    public static string? Check(string spec)
    {
        // thdate.cxx skips whitespace entirely inside a date spec (char 32 → continue), and our
        // parser re-joins tokens with spaces ("14 : 30") — so strip all whitespace up front.
        // Therion's reader also unquotes arguments before thdate sees them (`date "2018.07.29"`
        // is valid in the wild), so quotes are stripped too.
        var s = string.Concat(spec.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim('"');
        if (s.Length == 0 || s == "-") return null;

        // Interval: split on a '-' that separates two dates (not the leading empty-date dash).
        int dash = s.IndexOf('-', 1);
        if (dash > 0)
        {
            return CheckSingle(s[..dash]) ?? CheckSingle(s[(dash + 1)..]);
        }
        return CheckSingle(s);
    }

    private static string? CheckSingle(string s)
    {
        if (s.Length == 0) return "empty date in interval";

        // Split date@time.
        string datePart = s, timePart = "";
        int at = s.IndexOf('@');
        if (at >= 0) { datePart = s[..at]; timePart = s[(at + 1)..]; }

        var d = datePart.Split('.');
        if (d.Length > 3) return $"too many date components in '{s}'";
        if (!IsInt(d[0], out _)) return $"invalid year '{d[0]}'";
        if (d.Length > 1 && (!IsInt(d[1], out var month) || month < 1 || month > 12))
            return $"invalid month '{d[1]}'";
        if (d.Length > 2 && (!IsInt(d[2], out var day) || day < 1 || day > 31))
            return $"invalid day '{d[2]}'";

        if (at < 0) return null;
        if (d.Length < 3) return "a time requires a full year.month.day date";

        var t = timePart.Split(':');
        if (t.Length > 3) return $"too many time components in '{s}'";
        if (!IsInt(t[0], out var hour) || hour > 23) return $"invalid hour '{t[0]}'";
        if (t.Length > 1 && (!IsInt(t[1], out var minute) || minute > 59))
            return $"invalid minute '{t[1]}'";
        if (t.Length > 2 &&
            (!double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)
             || sec < 0 || sec >= 60))
            return $"invalid second '{t[2]}'";
        return null;
    }

    private static bool IsInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
