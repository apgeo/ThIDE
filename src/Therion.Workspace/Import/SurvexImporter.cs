// Survex (.svx) → Therion (.th) importer. Survex's data model is very close to Therion's
// (begin/end ≈ survey, *fix/*equate/*data map almost 1:1), so this produces a faithful, readable
// .th scaffold. Pure string→string; the caller writes the file. Not every Survex command is
// supported — unknown `*commands` are passed through as comments so nothing is silently lost.

using System;
using System.Collections.Generic;
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
            }
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
                        scopes.Push(new Scope(name, scopes.Count));
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
                        sb.Append(Indent(1)).Append("fix ").Append(rest.Trim()).Append('\n');
                        break;
                    case "equate":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("equate ").Append(rest.Trim()).Append('\n');
                        break;
                    case "data":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("data ").Append(MapDataReadings(rest.Trim())).Append('\n');
                        break;
                    case "date":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("date ").Append(rest.Trim().Replace('.', '.')).Append('\n');
                        break;
                    case "team":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("# team ").Append(rest.Trim()).Append('\n');
                        break;
                    case "flags":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("flags ").Append(rest.Trim()).Append('\n');
                        break;
                    case "calibrate":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("calibrate ").Append(rest.Trim()).Append('\n');
                        break;
                    case "units":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("units ").Append(rest.Trim()).Append('\n');
                        break;
                    case "cs":
                        EnsureCentreline();
                        sb.Append(Indent(1)).Append("cs ").Append(rest.Trim()).Append('\n');
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
            sb.Append(Indent(1)).Append(trimmed);
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
        public Scope(string? name, int depth) { Name = name; Depth = depth; }
        public string? Name { get; }
        public int Depth { get; }
        public bool CentrelineOpen { get; set; }
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
