// The oracle: parse every printed file, build the workspace model, and compare what the binder
// recovered with the generated cave. Names are compared by their absolute survey path, worked out
// independently of the product from the survey a file is input under and Therion's `@` rule
// (`point@inner.outer` written in survey S names S.outer.inner.point).

using System.Collections.Immutable;
using System.Text;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests.Properties.CaveGen;

internal static class BinderOracle
{
    private const double Tolerance = 1e-9;

    public static WorkspaceSemanticModel Build(IReadOnlyList<PrintedFile> files)
    {
        var parser = new ThParser();
        var parsed = files.ToDictionary(f => f.Path, f => parser.Parse(f.Path, f.Text));
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => true);
    }

    /// <summary>
    /// The absolute name of a model name <paramref name="q"/> bound in a file input under
    /// <paramref name="prefix"/>: a splay marker stays itself; an <c>@</c> placeholder is read by
    /// Therion's rule.
    /// </summary>
    public static string Canon(ImmutableArray<string> prefix, QualifiedName q)
    {
        if (q.Parts.Length == 1 && q.Last is "-" or ".") return q.Last;
        var parts = prefix.AddRange(q.Parts);
        var last = parts[^1];
        if (last.Contains('@'))
        {
            var r = StationRef.Parse(last);
            parts = parts.RemoveAt(parts.Length - 1).AddRange(r.SurveyPathTopDown).Add(r.Point);
        }
        return string.Join('.', parts);
    }

    /// <summary>Every way the recovered model differs from the cave; empty when they agree.</summary>
    public static List<string> Check(GCave cave, IReadOnlyList<PrintedFile> files)
    {
        var problems = new List<string>();
        var ws = Build(files);
        var prefixOf = files.ToDictionary(f => f.Path, f => f.Prefix);
        var models = files.Select(f => (File: f, Model: ws.PerFile.TryGetValue(f.Path, out var m) ? m : null)).ToList();
        foreach (var (f, m) in models)
            if (m is null) problems.Add($"{f.Path}: no semantic model");
        if (problems.Count > 0) return problems;

        // ---- diagnostics: a valid cave raises no warning and no error ----
        foreach (var d in ws.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
            problems.Add($"diagnostic {d.Code} {d.Severity} at {d.Span}: {d.Message}");
        foreach (var (f, m) in models)
            foreach (var d in ws.ValidateEquateReferences(m!))
                problems.Add($"unresolved reference {d.Code} at {d.Span}: {d.Message}");

        // ---- stations: exactly the generated ones (placeholders fold into the station they name) ----
        var expectedStations = cave.Surveys.SelectMany(s => s.Stations.Select(st => s.Ref(st).Key)).ToHashSet();
        var relToAbs = new Dictionary<string, string>();
        var modelStations = new Dictionary<string, List<StationSymbol>>();
        foreach (var (f, m) in models)
            foreach (var (qn, sym) in m!.Stations)
            {
                var abs = Canon(f.Prefix, qn);
                if (relToAbs.TryGetValue(qn.ToString(), out var seen) && seen != abs)
                    problems.Add($"generator: relative name {qn} is {seen} and {abs}");
                relToAbs[qn.ToString()] = abs;
                if (!modelStations.TryGetValue(abs, out var list)) modelStations[abs] = list = new List<StationSymbol>();
                list.Add(sym);
            }
        foreach (var missing in expectedStations.Except(modelStations.Keys).Order())
            problems.Add($"station {missing} is missing from the model");
        foreach (var extra in modelStations.Keys.Except(expectedStations).Order())
            problems.Add($"model has a station {extra} the cave does not");

        // ---- every placeholder resolves, through the workspace, to the station Therion's rule names ----
        foreach (var (f, m) in models)
            foreach (var qn in m!.Stations.Keys.Where(q => q.Last.Contains('@')))
            {
                var resolved = ws.ResolveStationSymbol(qn.Last);
                var expected = Canon(f.Prefix, qn);
                var got = resolved is null ? "nothing" : relToAbs.GetValueOrDefault(resolved.Name.ToString(), resolved.Name.ToString());
                if (got != expected)
                    problems.Add($"'{qn.Last}' in {System.IO.Path.GetFileName(f.Path)} resolves to {got}, expected {expected}");
            }

        // ---- surveys and their titles ----
        var expectedSurveys = cave.Surveys.ToDictionary(s => string.Join('.', s.Path), s => s.Title);
        var modelSurveys = new Dictionary<string, string?>();
        foreach (var (f, m) in models)
            foreach (var (qn, sym) in m!.Surveys)
                modelSurveys[string.Join('.', f.Prefix.AddRange(qn.Parts))] = sym.Title;
        foreach (var (path, title) in expectedSurveys)
        {
            if (!modelSurveys.TryGetValue(path, out var got)) problems.Add($"survey {path} is missing");
            else if (got != title) problems.Add($"survey {path} has title {Show(got)}, expected {Show(title)}");
        }
        foreach (var extra in modelSurveys.Keys.Except(expectedSurveys.Keys))
            problems.Add($"model has a survey {extra} the cave does not");

        // ---- shots: the same multiset, in metres and degrees, with the same flags ----
        var expectedShots = cave.Surveys.SelectMany(s => s.Shots).Select(sh => new ShotView(
            sh.From.Key, sh.To?.Key ?? sh.SplayMarker, sh.LengthM, sh.CompassDeg, sh.ClinoDeg,
            sh.To is null ? sh.Flags | ShotFlags.Splay : sh.Flags)).ToList();
        var modelShots = models.SelectMany(fm => fm.Model!.Shots.Select(sh => new ShotView(
            Canon(fm.File.Prefix, sh.From), Canon(fm.File.Prefix, sh.To), sh.Length, sh.Compass, sh.Clino, sh.Flags))).ToList();
        CompareShots(expectedShots, modelShots, problems);

        // ---- equate classes over the whole workspace ----
        var expectedClasses = ExpectedEquateClasses(cave);
        var graph = WorkspaceEquates.Build(ws);
        var modelClasses = new HashSet<string>();
        foreach (var group in graph.Groups())
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var qn in group)
                names.Add(relToAbs.TryGetValue(qn.ToString(), out var abs) ? abs : "?" + qn);
            if (names.Count > 1) modelClasses.Add(string.Join(" = ", names));
        }
        foreach (var c in expectedClasses.Except(modelClasses)) problems.Add($"equate class missing: {c}");
        foreach (var c in modelClasses.Except(expectedClasses)) problems.Add($"unexpected equate class: {c}");

        // ---- fixes (d:m:s converted) ----
        var expectedFixes = cave.Surveys.SelectMany(s => s.Fixes).ToDictionary(fx => fx.Station.Key);
        var modelFixes = modelStations
            .SelectMany(kv => kv.Value.Where(s => s.Kind == StationDeclarationKind.Fix).Select(s => (kv.Key, s)))
            .ToList();
        foreach (var (name, sym) in modelFixes)
        {
            if (!expectedFixes.TryGetValue(name, out var fx)) { problems.Add($"station {name} is fixed in the model only"); continue; }
            if (!Near(sym.FixX, fx.X.Value) || !Near(sym.FixY, fx.Y.Value) || !Near(sym.FixZ, fx.Z))
                problems.Add($"fix {name} is ({sym.FixX}, {sym.FixY}, {sym.FixZ}), expected ({fx.X.Value}, {fx.Y.Value}, {fx.Z})");
        }
        foreach (var name in expectedFixes.Keys.Except(modelFixes.Select(x => x.Key)))
            problems.Add($"fix of {name} is missing");

        // ---- station notes: comment and entrance flag ----
        var expectedNotes = cave.Surveys.SelectMany(s => s.Notes).ToDictionary(n => n.Station.Key);
        foreach (var (name, syms) in modelStations)
        {
            var comment = syms.Select(s => s.Comment).FirstOrDefault(c => c is not null);
            bool entrance = syms.Any(s => s.IsEntrance);
            expectedNotes.TryGetValue(name, out var note);
            if (comment != note?.Comment) problems.Add($"station {name} has comment {Show(comment)}, expected {Show(note?.Comment)}");
            if (entrance != (note?.Entrance ?? false)) problems.Add($"station {name} entrance flag is {entrance}");
        }

        return problems;
    }

    private sealed record ShotView(string From, string To, double? Length, double? Compass, double? Clino, ShotFlags Flags)
    {
        public override string ToString() =>
            $"{From} -> {To} L={Length} C={Compass} I={Clino} [{Flags}]";
    }

    private static void CompareShots(List<ShotView> expected, List<ShotView> actual, List<string> problems)
    {
        static string Key(ShotView s) => s.From + "\u0001" + s.To;
        var byKey = actual.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var e in expected)
        {
            if (!byKey.TryGetValue(Key(e), out var candidates) || candidates.Count == 0)
            {
                problems.Add($"shot {e} is missing");
                continue;
            }
            var match = candidates.FirstOrDefault(a => Same(e, a));
            if (match is null)
            {
                problems.Add($"shot {e} was bound as {candidates[0]}");
                candidates.RemoveAt(0);
            }
            else
            {
                candidates.Remove(match);
            }
        }
        foreach (var rest in byKey.Values.SelectMany(v => v))
            problems.Add($"model has a shot {rest} the cave does not");
    }

    private static bool Same(ShotView e, ShotView a) =>
        Near(a.Length, e.Length) && SameAngle(a.Compass, e.Compass) && Near(a.Clino, e.Clino) && a.Flags == e.Flags;

    private static bool Near(double? a, double? b) =>
        a is null ? b is null : b is not null && Math.Abs(a.Value - b.Value) <= Tolerance;

    private static bool SameAngle(double? a, double? b)
    {
        if (a is null || b is null) return a is null && b is null;
        double d = Math.Abs(a.Value - b.Value) % 360.0;
        return Math.Min(d, 360.0 - d) <= Tolerance;
    }

    private static HashSet<string> ExpectedEquateClasses(GCave cave)
    {
        var parent = new Dictionary<string, string>();
        string Find(string x)
        {
            parent.TryAdd(x, x);
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        foreach (var eq in cave.Surveys.SelectMany(s => s.Equates))
            for (int i = 1; i < eq.Members.Length; i++)
                parent[Find(eq.Members[i].Key)] = Find(eq.Members[0].Key);
        return parent.Keys.GroupBy(Find)
            .Select(g => new SortedSet<string>(g, StringComparer.Ordinal))
            .Where(s => s.Count > 1)
            .Select(s => string.Join(" = ", s))
            .ToHashSet();
    }

    private static string Show(string? s) => s is null ? "none" : "\"" + s + "\"";

    /// <summary>The printed files as one readable block, for a failure message.</summary>
    public static string Dump(IReadOnlyList<PrintedFile> files, int maxChars = 6000)
    {
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            sb.Append("--- ").Append(System.IO.Path.GetFileName(f.Path))
              .Append(" (input under '").Append(string.Join('.', f.Prefix)).Append("') ---\n");
            sb.Append(f.Text.Replace("\r\n", "⏎\n").Replace("\r", "␍\n")).Append('\n');
        }
        var s = sb.ToString();
        return s.Length > maxChars ? s[..maxChars] + "\n…" : s;
    }
}
