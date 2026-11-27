// Therion's own way of splitting a command line into arguments: whitespace separates, "…" quotes
// (a doubled quote is a quote), […] brackets a list that stays one argument, # starts a comment and a
// backslash before the line break continues the line. The parsed tree is used for the structure of a
// file; each command's arguments are read again from its text this way, so nothing the tokenizer
// split differently (a bracketed LRUD pair, a date) changes what a reading means.

using System.Globalization;
using System.Text;

namespace Therion.Workspace.Flatten;

internal readonly record struct TherionArg(string Value, int Start, int End, bool Quoted);

internal static class TherionArgs
{
    /// <summary>The arguments of <paramref name="line"/> with their offsets in it.</summary>
    public static List<TherionArg> Split(string line)
    {
        var args = new List<TherionArg>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && (line[i + 1] == '\n' || line[i + 1] == '\r')) { i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#') break;
            int start = i;
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(line[i++]);
                }
                args.Add(new TherionArg(sb.ToString(), start, i, Quoted: true));
                continue;
            }
            if (c == '[')
            {
                int depth = 0;
                while (i < line.Length)
                {
                    if (line[i] == '[') depth++;
                    else if (line[i] == ']' && --depth == 0) { i++; break; }
                    i++;
                }
                args.Add(new TherionArg(line[start..i], start, i, Quoted: false));
                continue;
            }
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '#') i++;
            args.Add(new TherionArg(line[start..i], start, i, Quoted: false));
        }
        return args;
    }

    /// <summary>The values of a bracketed list (<c>[1 2]</c> → 1, 2); a plain value comes back alone.</summary>
    public static List<string> ListItems(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 && v[0] == '[' && v[^1] == ']') v = v[1..^1];
        return Split(v).Select(a => a.Value).ToList();
    }

    /// <summary>What Therion's number parser makes of a token.</summary>
    public static NumberKind Parse(string token, out double value)
    {
        value = double.NaN;
        switch (token)
        {
            case "-" or "." or "nan" or "NaN" or "NAN": return NumberKind.Missing;
            case "up" or "Up" or "UP": value = double.PositiveInfinity; return NumberKind.Up;
            case "down" or "Down" or "DOWN": value = double.NegativeInfinity; return NumberKind.Down;
            case "inf" or "Inf" or "INF": value = double.PositiveInfinity; return NumberKind.Infinite;
            case "-inf" or "-Inf" or "-INF": value = double.NegativeInfinity; return NumberKind.Infinite;
        }
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            ? NumberKind.Number
            : NumberKind.Invalid;
    }

    /// <summary>
    /// A degree value that may be written <c>d:m</c> or <c>d:m:s</c> (Therion reads compass and clino
    /// readings in degrees, and lat-long coordinates, that way).
    /// </summary>
    public static NumberKind ParseDms(string token, out double value)
    {
        if (token.IndexOf(':') < 0) return Parse(token, out value);
        value = double.NaN;
        var parts = token.Split(':');
        if (parts.Length > 3) return NumberKind.Invalid;
        if (Parse(parts[0], out var d) != NumberKind.Number || d != Math.Floor(d)) return NumberKind.Invalid;
        if (Parse(parts[1], out var m) != NumberKind.Number || m < 0) return NumberKind.Invalid;
        double s = 0;
        if (parts.Length == 3)
        {
            if (m != Math.Floor(m) || Parse(parts[2], out s) != NumberKind.Number || s < 0) return NumberKind.Invalid;
        }
        // As Therion's thparse_double_dms: minutes and seconds are added to a positive degree value and
        // subtracted from any other, zero included.
        double magnitude = m / 60.0 + s / 3600.0;
        value = d > 0 ? d + magnitude : d - magnitude;
        return NumberKind.Number;
    }
}

internal enum NumberKind { Number, Missing, Up, Down, Infinite, Invalid }
