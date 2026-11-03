// A seeded generator of small multi-survey, optionally multi-file caves for the rename properties.
// While it prints the Therion text it records, for every name token it writes, which symbol that
// token denotes: a station token (bare or the point part of `x@sub.parent`) or a survey token (a
// `survey` name or one component of an `@` path). That record is the oracle the rename properties
// compare the occurrence index against, so it never asks the code under test what a token means.
// Every name, number and title is invented.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Therion.Semantics;

namespace Therion.Semantics.Tests.Properties.Rename;

/// <summary>A name token the generator wrote, and the symbol it denotes.</summary>
internal readonly record struct OracleSite(string File, int Start, int Length, SymbolId Symbol);

/// <summary>One generated cave: its files and every symbol token in them.</summary>
internal sealed record GeneratedRenameCave(
    int Seed,
    ImmutableDictionary<string, string> Files,
    ImmutableArray<OracleSite> Sites,
    ImmutableArray<GenSurvey> Surveys)
{
    /// <summary>The station and survey symbols that have at least one token.</summary>
    public ImmutableArray<SymbolId> Symbols { get; } =
        Sites.Select(s => s.Symbol).Distinct().OrderBy(s => s.ToString(), StringComparer.Ordinal).ToImmutableArray();

    public override string ToString()
    {
        var sb = new StringBuilder($"seed {Seed}\n");
        foreach (var (path, text) in Files.OrderBy(f => f.Key, StringComparer.Ordinal))
            sb.Append("=== ").Append(path).Append('\n').Append(text.Replace("\r", "\\r")).Append('\n');
        return sb.ToString();
    }
}

/// <summary>A survey of the generated cave.</summary>
internal sealed class GenSurvey
{
    public required string Name { get; set; }
    public GenSurvey? Parent { get; init; }
    public List<GenSurvey> Children { get; } = new();
    public List<string> Stations { get; } = new();
    /// <summary>Set when the survey starts its own file (only direct children of the root).</summary>
    public string? OwnFile { get; set; }

    /// <summary>The survey's name path as the workspace model keys it: from the root survey of its file.</summary>
    public ImmutableArray<string> ModelPath
    {
        get
        {
            var parts = new List<string>();
            for (var s = this; s is not null; s = s.OwnFile is null ? s.Parent : null) parts.Insert(0, s.Name);
            return parts.ToImmutableArray();
        }
    }

    public SymbolId SurveySymbol => new(SymbolKind.Survey, new QualifiedName(ModelPath));

    public SymbolId StationSymbol(string station) => new(SymbolKind.Station, new QualifiedName(ModelPath.Add(station)));

    public IEnumerable<GenSurvey> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants()) yield return d;
    }
}

internal static class RenameCaveGenerator
{
    /// <summary>Station names, including the ones that look like keywords or numbers.</summary>
    public static readonly string[] StationPool =
        ["1", "2", "3", "4", "01", "02", "10", "a12", "b7", "end", "END", "end1", "x-1", "p_3"];

    private static readonly string[] SurveyPool =
        ["north", "south", "ent", "upper", "lower", "deep", "sump", "loop", "pit", "hall", "rift", "crawl"];

    public static GeneratedRenameCave Generate(int seed)
    {
        var rnd = new Random(seed);
        var root = new GenSurvey { Name = "cave" };
        var unusedSurveyNames = SurveyPool.OrderBy(_ => rnd.Next()).ToList();

        // Survey tree: 1..3 children under the root, each with 0..2 children, sometimes one level deeper.
        AddStations(root, rnd);
        int top = rnd.Next(1, 4);
        for (int i = 0; i < top && unusedSurveyNames.Count > 0; i++)
        {
            var child = NewChild(root, unusedSurveyNames, rnd);
            int sub = rnd.Next(0, 3);
            for (int j = 0; j < sub && unusedSurveyNames.Count > 0; j++)
            {
                var grand = NewChild(child, unusedSurveyNames, rnd);
                if (rnd.NextDouble() < 0.3 && unusedSurveyNames.Count > 0) NewChild(grand, unusedSurveyNames, rnd);
            }
        }

        bool multiFile = rnd.NextDouble() < 0.5;
        if (multiFile)
            foreach (var c in root.Children)
                if (rnd.NextDouble() < 0.75) c.OwnFile = $"/gen/{c.Name}.th";

        var style = new PrintStyle(
            Newline: rnd.NextDouble() < 0.3 ? "\r\n" : "\n",
            Indent: rnd.Next(3) switch { 0 => "  ", 1 => "\t", _ => "    " },
            Centreline: rnd.NextDouble() < 0.5 ? "centreline" : "centerline",
            Comments: rnd.NextDouble() < 0.6);

        var writers = new Dictionary<string, Writer>(StringComparer.Ordinal);
        const string mainFile = "/gen/cave.th";
        var main = writers[mainFile] = new Writer(mainFile, style);
        if (style.Comments) main.Line(0, "# invented cave for a rename property; 1 2 end@north are only words here");
        PrintSurvey(root, main, 0, rnd, writers, style);

        // Sometimes a station token is the very last thing in the main file, with no newline after it.
        if (rnd.NextDouble() < 0.35)
        {
            // Only surveys printed in this file: a path from outside the root into an input file is
            // covered on its own (see the rename facts), so it does not mask everything else here.
            var targets = root.SelfAndDescendants()
                .Where(s => s != root && s.ModelPath[0] == root.Name).ToList();
            if (targets.Count > 0)
            {
                var a = targets[rnd.Next(targets.Count)];
                var b = targets[rnd.Next(targets.Count)];
                main.Raw("equate ");
                main.Ref(scope: null, a, a.Stations[rnd.Next(a.Stations.Count)]);
                main.Raw(" ");
                main.Ref(scope: null, b, b.Stations[rnd.Next(b.Stations.Count)]);
            }
        }

        var files = writers.ToImmutableDictionary(w => w.Key, w => w.Value.Text);
        var sites = writers.Values.SelectMany(w => w.Sites).ToImmutableArray();
        return new GeneratedRenameCave(seed, files, sites, root.SelfAndDescendants().ToImmutableArray());
    }

    private static GenSurvey NewChild(GenSurvey parent, List<string> unused, Random rnd)
    {
        var name = unused[^1];
        unused.RemoveAt(unused.Count - 1);
        var child = new GenSurvey { Name = name, Parent = parent };
        parent.Children.Add(child);
        AddStations(child, rnd);
        return child;
    }

    private static void AddStations(GenSurvey s, Random rnd)
    {
        int n = rnd.Next(2, 6);
        foreach (var name in StationPool.OrderBy(_ => rnd.Next()).Take(n)) s.Stations.Add(name);
    }

    private sealed record PrintStyle(string Newline, string Indent, string Centreline, bool Comments);

    private static void PrintSurvey(GenSurvey s, Writer w, int depth, Random rnd,
        Dictionary<string, Writer> writers, PrintStyle style)
    {
        w.Indent(depth);
        w.Raw("survey ");
        w.Token(s.Name, s.SurveySymbol);
        if (rnd.NextDouble() < 0.3) w.Raw(" -title \"Invented \"\"" + s.Name + "\"\" passage\"");
        w.EndLine();

        bool childrenFirst = rnd.NextDouble() < 0.4;
        if (childrenFirst) PrintChildren(s, w, depth, rnd, writers, style);
        PrintCentreline(s, w, depth + 1, rnd, style);
        if (!childrenFirst) PrintChildren(s, w, depth, rnd, writers, style);

        w.Line(depth, "endsurvey");
    }

    private static void PrintChildren(GenSurvey s, Writer w, int depth, Random rnd,
        Dictionary<string, Writer> writers, PrintStyle style)
    {
        foreach (var c in s.Children)
        {
            if (c.OwnFile is { } file)
            {
                w.Line(depth + 1, "input " + Path.GetFileName(file));
                var cw = writers[file] = new Writer(file, style);
                PrintSurvey(c, cw, 0, rnd, writers, style);
            }
            else PrintSurvey(c, w, depth + 1, rnd, writers, style);
        }
    }

    private static void PrintCentreline(GenSurvey s, Writer w, int depth, Random rnd, PrintStyle style)
    {
        w.Line(depth, style.Centreline);
        w.Line(depth + 1, "data normal from to length compass clino");

        // A tree of shots over the survey's own stations: each later station hangs off an earlier one.
        var st = s.Stations;
        for (int i = 1; i < st.Count; i++)
        {
            var from = st[rnd.Next(i)];
            var to = st[i];
            if (rnd.NextDouble() < 0.3) (from, to) = (to, from);
            Row(w, depth + 1, s, (s, from), (s, to), rnd, style);
        }
        if (st.Count > 2 && rnd.NextDouble() < 0.3)
            Row(w, depth + 1, s, (s, st[0]), (s, st[^1]), rnd, style);   // a loop

        var below = s.SelfAndDescendants().Where(d => d != s).ToList();

        // Connecting shots to descendant stations, written as `x@child[.parent…]`.
        foreach (var d in below)
        {
            if (rnd.NextDouble() < 0.5) continue;
            var local = (s, st[rnd.Next(st.Count)]);
            var remote = (d, d.Stations[rnd.Next(d.Stations.Count)]);
            if (rnd.NextDouble() < 0.5) Row(w, depth + 1, s, local, remote, rnd, style);
            else Row(w, depth + 1, s, remote, local, rnd, style);
        }

        // Equates: local–local, local–descendant, descendant–descendant.
        int equates = rnd.Next(0, 3);
        var pool = below.Prepend(s).ToList();
        for (int e = 0; e < equates; e++)
        {
            var a = pool[rnd.Next(pool.Count)];
            var b = pool[rnd.Next(pool.Count)];
            var sa = a.Stations[rnd.Next(a.Stations.Count)];
            var sb = b.Stations[rnd.Next(b.Stations.Count)];
            if (a == b && sa == sb) continue;
            w.Indent(depth + 1);
            w.Raw("equate ");
            w.Ref(s, a, sa);
            w.Raw(" ");
            w.Ref(s, b, sb);
            w.EndLine();
        }

        // At most one fix per survey: a local station or a descendant one.
        if (rnd.NextDouble() < 0.4)
        {
            var target = pool[rnd.Next(pool.Count)];
            w.Indent(depth + 1);
            w.Raw("fix ");
            w.Ref(s, target, target.Stations[rnd.Next(target.Stations.Count)]);
            w.Raw(string.Create(CultureInfo.InvariantCulture,
                $" {rnd.Next(1000, 9000)}.{rnd.Next(10)} {rnd.Next(1000, 9000)}.5 {rnd.Next(100, 900)}"));
            w.EndLine();
        }

        w.Line(depth, "end" + style.Centreline);
    }

    private static void Row(Writer w, int depth, GenSurvey scope,
        (GenSurvey Survey, string Station) from, (GenSurvey Survey, string Station) to, Random rnd, PrintStyle style)
    {
        if (style.Comments && rnd.NextDouble() < 0.1) w.Line(depth, "# 1 2 x@north end — a comment, not a shot");
        w.Indent(depth);
        w.Ref(scope, from.Survey, from.Station);
        w.Raw(rnd.NextDouble() < 0.2 ? "\t" : " ");
        w.Ref(scope, to.Survey, to.Station);
        w.Raw(string.Create(CultureInfo.InvariantCulture,
            $" {rnd.Next(1, 40)}.{rnd.Next(10)} {rnd.Next(0, 360)} {rnd.Next(-40, 41)}"));
        if (style.Comments && rnd.NextDouble() < 0.2) w.Raw(" # end 1@north");
        w.EndLine();
    }

    /// <summary>Appends text and records the symbol of each name token it writes.</summary>
    private sealed class Writer(string file, PrintStyle style)
    {
        private readonly StringBuilder _sb = new();
        public List<OracleSite> Sites { get; } = new();
        public string Text => _sb.ToString();

        public void Raw(string s) => _sb.Append(s);

        public void Indent(int depth)
        {
            for (int i = 0; i < depth; i++) _sb.Append(style.Indent);
        }

        public void EndLine() => _sb.Append(style.Newline);

        public void Line(int depth, string s)
        {
            Indent(depth);
            _sb.Append(s);
            EndLine();
        }

        public void Token(string text, SymbolId symbol)
        {
            Sites.Add(new OracleSite(file, _sb.Length, text.Length, symbol));
            _sb.Append(text);
        }

        /// <summary>
        /// Writes station <paramref name="station"/> of <paramref name="target"/> as seen from survey
        /// <paramref name="scope"/> (null: the top level of the file, outside every survey): bare when
        /// local, else <c>station@inner.….outer</c> up to the survey just below the scope.
        /// </summary>
        public void Ref(GenSurvey? scope, GenSurvey target, string station)
        {
            Token(station, target.StationSymbol(station));
            if (target == scope) return;
            char sep = '@';
            for (var s = target; s is not null && s != scope; s = s.Parent)
            {
                _sb.Append(sep);
                Token(s.Name, s.SurveySymbol);
                sep = '.';
            }
        }
    }
}
