// Survex (.svx) → Therion (.th) importer. Survex's data model is very close to Therion's
// (begin/end ≈ survey, *fix/*equate/*data map almost 1:1), so this produces a faithful, readable
// .th scaffold. Pure string→string; the caller writes the file. Not every Survex command is
// supported — unknown `*commands` are passed through as comments so nothing is silently lost.
//
// Survex settings (*units, *data, …) stay in force inside a nested *begin and after its *end, while
// every Therion centreline starts from defaults; each Scope therefore remembers the settings in
// force and every centreline the importer opens restates them. A station written `sub.station` is
// rewritten to Therion's `station@sub`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Therion.Workspace.Import;

/// <summary>Converts Survex <c>.svx</c> survey source into Therion <c>.th</c> source.</summary>
public static class SurvexImporter
{
    public static string Import(string svx)
    {
        var sb = new StringBuilder();
        // A scope = one *begin block. CentrelineOpen tracks whether we've opened a centreline to
        // hold this scope's direct data/fix/equate lines (Therion requires data inside centreline).
        var scopes = new Stack<Scope>();
        scopes.Push(new Scope(null, 0));   // implicit file-level scope

        string Indent(int extra = 0) => new string(' ', (scopes.Count - 1 + extra) * 2);

        void EnsureCentreline()
        {
            var s = scopes.Peek();
            if (!s.CentrelineOpen)
            {
                sb.Append(Indent()).Append("centreline\n");
                s.CentrelineOpen = true;
                foreach (var (_, setting) in s.Settings) sb.Append(Indent(1)).Append(setting).Append('\n');
            }
        }
        // Writes a setting line into the (opened) centreline and keeps it for the centrelines opened later.
        void Setting(string key, string line, bool replace = true)
        {
            EnsureCentreline();
            sb.Append(Indent(1)).Append(line).Append('\n');
            scopes.Peek().Remember(key, line, replace);
        }
        void CloseCentreline()
        {
            var s = scopes.Peek();
            if (s.CentrelineOpen)
            {
                sb.Append(Indent()).Append("endcentreline\n");
                s.CentrelineOpen = false;
            }
        }

        foreach (var raw in svx.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var (line, comment) = SplitComment(raw);
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                if (comment is not null) sb.Append(Indent()).Append("# ").Append(comment).Append('\n');
                continue;
            }

            if (trimmed.StartsWith('*'))
            {
                var (cmd, rest) = SplitFirst(trimmed[1..].Trim());
                switch (cmd.ToLowerInvariant())
                {
                    case "begin":
                        CloseCentreline();
                        var name = FirstToken(rest);
                        sb.Append(Indent()).Append("survey ").Append(string.IsNullOrEmpty(name) ? "_" : name).Append('\n');
                        scopes.Push(new Scope(name, scopes.Count, scopes.Peek()));
                        break;
                    case "end":
                        CloseCentreline();
                        if (scopes.Count > 1)
                        {
                            scopes.Pop();
                            sb.Append(Indent()).Append("endsurvey\n");
                        }
                        break;
                    case "title":
                        // Carried as a survey option isn't possible after the header line; keep as comment.
                        sb.Append(Indent()).Append("# title ").Append(rest.Trim()).Append('\n');
                        break;
                    case "fix":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("fix ").Append(QualifyStations(rest.Trim(), scopes.Peek(), firstOnly: true)).Append('\n');
                        break;
                    case "equate":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("equate ").Append(QualifyStations(rest.Trim(), scopes.Peek())).Append('\n');
                        break;
                    case "data":
                        var dataLine = "data " + MapDataReadings(rest.Trim());
                        Setting("data", dataLine);
                        scopes.Peek().DataColumns = DataColumns(rest);
                        break;
                    case "date":
                        Setting("date", "date " + rest.Trim().Replace('.', '.'));
                        break;
                    case "team":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("# team ").Append(rest.Trim()).Append('\n');
                        break;
                    case "flags":
                        Setting("flags", "flags " + rest.Trim(), replace: false);   // cumulative
                        break;
                    case "calibrate":
                        Setting("calibrate " + FirstToken(rest), "calibrate " + rest.Trim());
                        break;
                    case "units":
                        var unitWords = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        Setting("units " + string.Join(' ', unitWords.Take(Math.Max(unitWords.Length - 1, 1))), "units " + rest.Trim());
                        break;
                    case "cs":
                        Setting("cs", "cs " + rest.Trim());
                        break;
                    case "set":
                        var setWords = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (setWords.Length > 1 && setWords[0].Equals("separator", StringComparison.OrdinalIgnoreCase)
                            && setWords[1].Trim('"').Length == 1)
                            scopes.Peek().Separator = setWords[1].Trim('"')[0];
                        sb.Append(Indent()).Append("# *").Append(trimmed[1..].Trim()).Append('\n');
                        break;
                    case "include":
                        sb.Append(Indent()).Append("input ").Append(Unquote(rest.Trim())).Append('\n');
                        break;
                    default:
                        // Unsupported *command — keep verbatim as a comment so it isn't lost.
                        sb.Append(Indent()).Append("# *").Append(trimmed[1..].Trim()).Append('\n');
                        break;
                }
                if (comment is not null) { /* inline comment on a command line — drop quietly */ }
                continue;
            }

            // A plain data row inside the current scope.
            EnsureCentreline();
            sb.Append(Indent(1)).Append(QualifyRow(trimmed, scopes.Peek()));
            if (comment is not null) sb.Append(" # ").Append(comment);
            sb.Append('\n');
        }

        // Close any scopes the file left open (malformed but be forgiving).
        while (scopes.Count > 1)
        {
            CloseCentreline();
            scopes.Pop();
            sb.Append(Indent()).Append("endsurvey\n");
        }
        CloseCentreline();
        return sb.ToString();
    }

    // Survex `*data normal from to tape compass clino` ⇒ Therion uses the same reading keywords;
    // only `tape`→`tape`, `compass`→`compass`, `clino`→`clino` already match. Pass through.
    private static string MapDataReadings(string rest) => rest;

    private sealed class Scope
    {
        // A child starts with the settings and station syntax in force where it begins.
        public Scope(string? name, int depth, Scope? parent = null)
        {
            Name = name; Depth = depth;
            if (parent is null) return;
            Settings.AddRange(parent.Settings);
            DataColumns = parent.DataColumns;
            Separator = parent.Separator;
        }
        public string? Name { get; }
        public int Depth { get; }
        public bool CentrelineOpen { get; set; }
        /// <summary>The setting lines in force (Therion spelling), in the order they were given, keyed by what they set.</summary>
        public List<(string Key, string Line)> Settings { get; } = new();
        /// <summary>The column names of the active <c>*data</c> line after its style; Survex's default is <c>from to tape compass clino</c>.</summary>
        public string[] DataColumns { get; set; } = { "from", "to", "tape", "compass", "clino" };
        public char Separator { get; set; } = '.';

        public void Remember(string key, string line, bool replace)
        {
            if (replace) Settings.RemoveAll(x => x.Key == key);
            Settings.Add((key, line));
        }
    }

    // The columns of `*data <style> <column>…`; `default` (or no column) restores Survex's default order.
    private static string[] DataColumns(string rest)
    {
        var words = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words[0].Equals("default", StringComparison.OrdinalIgnoreCase))
            return new[] { "from", "to", "tape", "compass", "clino" };
        return words.Skip(1).Select(w => w.ToLowerInvariant()).ToArray();
    }

    // Survex names a station in a sub-survey `a.b.5`; Therion writes `5@b.a` (innermost survey first).
    // A name without the separator, or with an empty part (`..`, `.`, a trailing separator), is kept.
    private static string QualifyStation(string name, char separator)
    {
        if (name.IndexOf(separator) < 0) return name;
        var parts = name.Split(separator);
        if (parts.Any(p => p.Length == 0)) return name;
        return parts[^1] + "@" + string.Join('.', parts.Take(parts.Length - 1).Reverse());
    }

    // Rewrites the station names of a `*equate` (all words) or `*fix` (the first word) argument.
    private static string QualifyStations(string args, Scope scope, bool firstOnly = false)
    {
        var words = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length && (!firstOnly || i == 0); i++)
            words[i] = QualifyStation(words[i], scope.Separator);
        return string.Join(' ', words);
    }

    // Rewrites the station columns (from / to / station) of a data row to follow the active `*data`
    // order. A row is left as written when it names no sub-survey station or the order has `newline`.
    private static string QualifyRow(string row, Scope scope)
    {
        var cols = scope.DataColumns;
        if (cols.Contains("newline")) return row;
        var words = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool changed = false;
        for (int i = 0; i < words.Length && i < cols.Length; i++)
        {
            if (cols[i] is not ("from" or "to" or "station")) continue;
            var q = QualifyStation(words[i], scope.Separator);
            if (q != words[i]) { words[i] = q; changed = true; }
        }
        return changed ? string.Join(' ', words) : row;
    }

    private static (string Line, string? Comment) SplitComment(string s)
    {
        int i = s.IndexOf(';');
        return i < 0 ? (s, null) : (s[..i], s[(i + 1)..].Trim());
    }

    private static (string First, string Remainder) SplitFirst(string s)
    {
        int i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        return (s[..i], i < s.Length ? s[(i + 1)..] : string.Empty);
    }

    private static string FirstToken(string s)
    {
        var t = s.Trim();
        int i = 0;
        while (i < t.Length && !char.IsWhiteSpace(t[i])) i++;
        return t[..i];
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
}
