// rename_symbol end to end, over generated projects on disk. For a random station or survey of a
// generated project, the tool's applied rename must write exactly what the rename plan of the loaded
// model says, keep every token on the same symbol once the project is loaded again (the renamed one
// under its new name), and leave the project byte-identical when the new name is taken in that scope
// or when only a dry run was asked for. Fixed seeds; every name and number is invented.

using System.Globalization;
using System.Text;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using Therion.Semantics;

namespace Therion.Mcp.Tests;

public class RenameSymbolPropertyTests
{
    private const int Projects = 40;
    private const int BaseSeed = 0x4E4D_0040;

    private static readonly string[] StationPool = ["1", "2", "3", "01", "10", "a12", "end", "END", "end1", "x-1"];
    private static readonly string[] SurveyPool = ["north", "south", "ent", "upper", "lower", "deep", "sump"];
    private static readonly string[] FreshNames = ["r1", "renamed", "q-7", "Z99", "k"];

    public static IEnumerable<object[]> Batches() => Enumerable.Range(0, Projects / 10).Select(b => new object[] { b });

    /// <summary>A small project: a root survey with child surveys, inline or in their own files.</summary>
    private sealed class Project : IDisposable
    {
        public required string Root { get; init; }
        public required string Thconfig { get; init; }
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ } }

        public Dictionary<string, byte[]> Snapshot() =>
            Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
    }

    private static Project Generate(int seed)
    {
        var rnd = new Random(seed);
        var root = Path.Combine(Path.GetTempPath(), "thmcp_rename_" + seed.ToString("x", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        root = WorkspacePaths.Canonicalize(root);
        string nl = rnd.NextDouble() < 0.3 ? "\r\n" : "\n";
        var project = new Project { Root = root, Thconfig = Path.Combine(root, "project.thconfig") };
        File.WriteAllText(project.Thconfig, "source cave.th" + nl);

        string[] Stations() => StationPool.OrderBy(_ => rnd.Next()).Take(rnd.Next(2, 5)).ToArray();
        string Row(string from, string to) => string.Create(CultureInfo.InvariantCulture,
            $"    {from} {to} {rnd.Next(1, 30)}.{rnd.Next(10)} {rnd.Next(360)} {rnd.Next(-30, 31)}{nl}");
        string Body(string[] st)
        {
            var sb = new StringBuilder($"  centreline{nl}    data normal from to length compass clino{nl}");
            for (int i = 1; i < st.Length; i++) sb.Append(Row(st[rnd.Next(i)], st[i]));
            return sb.ToString();
        }

        var children = SurveyPool.OrderBy(_ => rnd.Next()).Take(rnd.Next(1, 4)).ToArray();
        var childStations = children.ToDictionary(c => c, _ => Stations());
        var inline = new List<string>();
        var main = new StringBuilder($"survey cave{nl}");
        foreach (var c in children)
        {
            var text = $"survey {c}{nl}{Body(childStations[c])}  endcentreline{nl}endsurvey{nl}";
            if (rnd.NextDouble() < 0.5)
            {
                project.Files[Path.Combine(root, c + ".th")] = text;
                main.Append($"  input {c}.th{nl}");
            }
            else
            {
                inline.Add(c);
                main.Append(string.Concat(text.Split(nl).Where(l => l.Length > 0).Select(l => "  " + l + nl)));
            }
        }
        var own = Stations();
        main.Append(Body(own));
        foreach (var c in children)
        {
            var cs = childStations[c];
            main.Append(Row(own[rnd.Next(own.Length)], cs[rnd.Next(cs.Length)] + "@" + c));
        }
        if (children.Length > 1)
        {
            string a = children[0], b = children[1];
            main.Append($"    equate {childStations[a][0]}@{a} {childStations[b][^1]}@{b}{nl}");
        }
        main.Append($"    fix {own[0]} 1000 2000 300{nl}  endcentreline{nl}endsurvey{nl}");
        // A station token that ends the file, with no newline after it.
        if (inline.Count > 0 && rnd.NextDouble() < 0.5)
        {
            var c = inline[rnd.Next(inline.Count)];
            main.Append($"equate {childStations[c][0]}@{c}.cave {childStations[c][^1]}@{c}.cave");
        }
        project.Files[Path.Combine(root, "cave.th")] = main.ToString();

        foreach (var (path, text) in project.Files) File.WriteAllText(path, text);
        return project;
    }

    private static async Task<(WorkspaceHost Host, WorkspaceSemanticModel Model)> LoadAsync(Project project)
    {
        var host = new WorkspaceHost();
        var snapshot = await host.LoadAsync(project.Thconfig);
        return (host, snapshot.Model);
    }

    private static HashSet<(string File, int Start, int Length, SymbolId Symbol)> Sites(WorkspaceSemanticModel ws) =>
        ws.StationOccurrences
            .Where(kv => kv.Key.Kind is SymbolKind.Station or SymbolKind.Survey && !kv.Key.Name.Last.Contains('@'))
            .SelectMany(kv => kv.Value.Select(o => (o.Span.FilePath, o.Span.StartOffset, o.Span.Length, kv.Key)))
            .ToHashSet();

    /// <summary>A symbol after <paramref name="target"/> is renamed to <paramref name="renamed"/>: itself, or one under a renamed survey.</summary>
    private static SymbolId AfterRename(SymbolId symbol, SymbolId target, SymbolId renamed)
    {
        if (symbol == target) return renamed;
        var prefix = target.Name.Parts;
        var parts = symbol.Name.Parts;
        if (target.Kind != SymbolKind.Survey || parts.Length <= prefix.Length || !parts.Take(prefix.Length).SequenceEqual(prefix))
            return symbol;
        return symbol with { Name = new QualifiedName(renamed.Name.Parts.AddRange(parts.Skip(prefix.Length))) };
    }

    /// <summary>The name the tool takes for a symbol: a survey's dotted path, or <c>station@inner.….outer</c>.</summary>
    private static string ToolName(SymbolId symbol)
    {
        var parts = symbol.Name.Parts;
        if (symbol.Kind == SymbolKind.Survey || parts.Length == 1) return symbol.Name.ToString();
        return parts[^1] + "@" + string.Join('.', parts.Take(parts.Length - 1).Reverse());
    }

    private static List<SymbolId> Renameable(WorkspaceSemanticModel ws) =>
        ws.StationOccurrences.Keys
            .Where(k => k.Kind is SymbolKind.Station or SymbolKind.Survey && !k.Name.Last.Contains('@'))
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> Siblings(WorkspaceSemanticModel ws, SymbolId symbol)
    {
        var parent = symbol.Name.Parts.RemoveAt(symbol.Name.Parts.Length - 1);
        IEnumerable<QualifiedName> names = symbol.Kind == SymbolKind.Station
            ? ws.StationsByQn.Values.Select(s => s.Name)
            : ws.SurveysByFullName.Values.Select(s => s.Name);
        return names.Where(n => n.Parts.Length == symbol.Name.Parts.Length
                                && n.Parts.Take(parent.Length).SequenceEqual(parent)
                                && !n.Last.Contains('@'))
            .Select(n => n.Last).Where(n => n != symbol.Name.Last).Distinct();
    }

    [Theory]
    [MemberData(nameof(Batches))]
    public async Task An_applied_rename_writes_the_plan_and_keeps_every_reference_on_its_symbol(int batch)
    {
        foreach (var seed in Enumerable.Range(BaseSeed + batch * 10, 10))
        {
            using var project = Generate(seed);
            var rnd = new Random(seed ^ 0x3C3C);
            var (host, before) = await LoadAsync(project);
            await using var _ = host;

            var symbols = Renameable(before);
            var target = symbols[rnd.Next(symbols.Count)];
            var oldName = target.Name.Last;
            // A station may take a name another survey uses; survey names stay unique.
            var taken = target.Kind == SymbolKind.Survey
                ? before.SurveysByLastName.Keys.ToHashSet(StringComparer.Ordinal)
                : Siblings(before, target).ToHashSet(StringComparer.Ordinal);
            var pool = target.Kind == SymbolKind.Survey ? FreshNames : FreshNames.Concat(StationPool).ToArray();
            var newName = pool.Where(n => n != oldName && !taken.Contains(n)).OrderBy(_ => rnd.Next()).First();
            var context = $"seed {seed}: rename {target} -> {newName}\n" +
                string.Join("\n", project.Files.Select(f => $"=== {f.Key}\n{f.Value}"));

            // What the library plan of the loaded model says the files become.
            var plan = SymbolRenamePlan.Compute(before, target, oldName, File.ReadAllText);
            var expected = project.Files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
            foreach (var edit in plan)
            {
                var text = expected[edit.FilePath];
                foreach (var (start, length) in edit.Spans.OrderByDescending(s => s.Start))
                    text = string.Concat(text.AsSpan(0, start), newName, text.AsSpan(start + length));
                expected[edit.FilePath] = text;
            }

            var tools = new RenameTools(host, new MutationEngine(host));
            var result = await tools.RenameSymbol(ToolName(target), newName,
                kind: target.Kind == SymbolKind.Station ? "station" : "survey", dryRun: false);

            Assert.True(result.Ok, $"{result.Error?.Code}: {result.Error?.Message}\n{context}");
            foreach (var (path, text) in expected)
                Assert.True(text == File.ReadAllText(path), $"{path} differs from the plan\n{context}");

            // Load the renamed project afresh: every token denotes what it did, under the new name.
            var (again, after) = await LoadAsync(project);
            await using var __ = again;
            var renamed = target with { Name = new QualifiedName(target.Name.Parts.SetItem(target.Name.Parts.Length - 1, newName)) };
            var spans = plan.SelectMany(e => e.Spans.Select(s => (e.FilePath, s.Start))).ToList();
            int delta = newName.Length - oldName.Length;
            var expectedSites = Sites(before).Select(s =>
            {
                int shift = spans.Count(p => p.FilePath == s.File && p.Start < s.Start) * delta;
                return (s.File, s.Start + shift, s.Symbol == target ? newName.Length : s.Length, AfterRename(s.Symbol, target, renamed));
            }).ToHashSet();
            var actualSites = Sites(after);
            Assert.True(expectedSites.SetEquals(actualSites),
                $"lost: {string.Join("; ", expectedSites.Except(actualSites))}\ngained: {string.Join("; ", actualSites.Except(expectedSites))}\n{context}");
        }
    }

    [Theory]
    [MemberData(nameof(Batches))]
    public async Task A_rename_to_a_name_taken_in_the_same_scope_or_a_dry_run_changes_no_byte(int batch)
    {
        foreach (var seed in Enumerable.Range(BaseSeed + 1000 + batch * 10, 10))
        {
            using var project = Generate(seed);
            var rnd = new Random(seed ^ 0x5A5A);
            var (host, model) = await LoadAsync(project);
            await using var _ = host;
            var tools = new RenameTools(host, new MutationEngine(host));
            var bytes = project.Snapshot();

            var symbols = Renameable(model);
            var target = symbols[rnd.Next(symbols.Count)];
            var kind = target.Kind == SymbolKind.Station ? "station" : "survey";

            var dry = await tools.RenameSymbol(ToolName(target), "dry_run_name", kind: kind);
            Assert.True(dry.Ok, $"seed {seed}: {dry.Error?.Code}: {dry.Error?.Message}");
            AssertUnchanged(project, bytes, $"seed {seed}: dry run of {target}");

            foreach (var sibling in Siblings(model, target))
            {
                var refused = await tools.RenameSymbol(ToolName(target), sibling, kind: kind, dryRun: false);
                Assert.True(refused.Error?.Code == ToolErrorCodes.NameCollision,
                    $"seed {seed}: renaming {target} to its sibling '{sibling}' gave {refused.Error?.Code ?? "success"}");
                AssertUnchanged(project, bytes, $"seed {seed}: refused rename of {target} to {sibling}");
            }
        }
    }

    private static void AssertUnchanged(Project project, Dictionary<string, byte[]> before, string context)
    {
        var now = project.Snapshot();
        Assert.True(before.Keys.Order(StringComparer.Ordinal).SequenceEqual(now.Keys.Order(StringComparer.Ordinal)),
            $"{context}: files appeared or went: {string.Join(", ", now.Keys.Except(before.Keys).Concat(before.Keys.Except(now.Keys)))}");
        foreach (var (path, content) in before)
            Assert.True(content.AsSpan().SequenceEqual(now[path]), $"{context}: {path} changed");
    }
}
