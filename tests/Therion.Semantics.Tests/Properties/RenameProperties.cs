// Rename, checked as a property over generated caves rather than a handful of examples.
//
// For a generated cave the generator knows which station or survey every name token denotes. For a
// randomly chosen symbol and a new name, the rename plan is spliced into the files and the workspace
// model rebuilt. Then:
//   - the plan rewrites exactly the tokens of that symbol, and as many as the occurrence index holds;
//   - every token still denotes the same symbol afterwards (the renamed one under its new name), and
//     the index has no token more or less than before;
//   - the diagnostics are the same codes on the same lines;
//   - renaming back restores every file byte for byte.
// Seeds are fixed, so a failure names the seed that reproduces it.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests.Properties.Rename;

public class RenameProperties
{
    private const int Caves = 200;
    private const int BaseSeed = 0x5EED_0400;
    private const int RenamesPerCave = 3;

    private static readonly string[] FreshNames = ["r1", "renamed", "q-7", "Z99", "n0", "endx", "longer_name_42", "k"];

    public static IEnumerable<object[]> Seeds() =>
        Enumerable.Range(0, Caves / 25).Select(batch => new object[] { batch });

    private static WorkspaceSemanticModel Build(IReadOnlyDictionary<string, string> files)
    {
        var parsed = files.ToDictionary(f => f.Key, f => new ThParser().Parse(f.Key, f.Value));
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => true);
    }

    /// <summary>The station and survey tokens the index holds, without the per-file placeholders of `x@sub` tokens.</summary>
    private static HashSet<OracleSite> IndexedSites(WorkspaceSemanticModel ws) =>
        ws.StationOccurrences
            .Where(kv => kv.Key.Kind is SymbolKind.Station or SymbolKind.Survey && !kv.Key.Name.Last.Contains('@'))
            .SelectMany(kv => kv.Value.Select(o => new OracleSite(o.Span.FilePath, o.Span.StartOffset, o.Span.Length, kv.Key)))
            .ToHashSet();

    private static List<(string Code, DiagnosticSeverity Severity, string File, int Line)> DiagnosticShape(WorkspaceSemanticModel ws)
    {
        var all = ws.Diagnostics.Concat(ws.PerFile.Values.SelectMany(m => ws.ValidateEquateReferences(m)));
        return all.Select(d => (d.Code.Value, d.Severity, d.Span.FilePath, d.Span.Start.Line))
            .OrderBy(d => d.Item3, StringComparer.Ordinal).ThenBy(d => d.Item4).ThenBy(d => d.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, string> Apply(IReadOnlyDictionary<string, string> files,
        IReadOnlyList<RenameFileEdits> plan, string newName)
    {
        var result = files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        foreach (var edit in plan)
        {
            var text = result[edit.FilePath];
            foreach (var (start, length) in edit.Spans.OrderByDescending(s => s.Start))
                text = string.Concat(text.AsSpan(0, start), newName, text.AsSpan(start + length));
            result[edit.FilePath] = text;
        }
        return result;
    }

    /// <summary>Where a token starting at <paramref name="site"/> lands after the renamed tokens change length.</summary>
    private static OracleSite Shift(OracleSite site, IReadOnlyList<OracleSite> renamed, int delta, SymbolId target, SymbolId renamedTo, int newLength)
    {
        int before = renamed.Count(r => r.File == site.File && r.Start < site.Start);
        bool isTarget = site.Symbol == target;
        return new OracleSite(site.File, site.Start + before * delta, isTarget ? newLength : site.Length,
            isTarget ? renamedTo : RenameAncestor(site.Symbol, target, renamedTo));
    }

    /// <summary>A symbol under a renamed survey changes its qualified name too.</summary>
    private static SymbolId RenameAncestor(SymbolId symbol, SymbolId target, SymbolId renamedTo)
    {
        if (target.Kind != SymbolKind.Survey) return symbol;
        var prefix = target.Name.Parts;
        var parts = symbol.Name.Parts;
        if (parts.Length <= prefix.Length || !parts.Take(prefix.Length).SequenceEqual(prefix)) return symbol;
        return symbol with { Name = new QualifiedName(renamedTo.Name.Parts.AddRange(parts.Skip(prefix.Length))) };
    }

    private static string PickNewName(GeneratedRenameCave cave, SymbolId target, Random rnd)
    {
        var old = target.Name.Last;
        var parentPath = target.Name.Parts.RemoveAt(target.Name.Parts.Length - 1);
        HashSet<string> taken;
        List<string> candidates;
        if (target.Kind == SymbolKind.Station)
        {
            var survey = cave.Surveys.Single(s => s.ModelPath.SequenceEqual(parentPath));
            taken = survey.Stations.ToHashSet(StringComparer.Ordinal);
            // A name another survey already uses is not a collision; mix those in with fresh ones.
            var usedElsewhere = cave.Surveys.Where(s => s != survey).SelectMany(s => s.Stations).Distinct().ToList();
            candidates = rnd.NextDouble() < 0.5 && usedElsewhere.Count > 0 ? usedElsewhere : FreshNames.ToList();
        }
        else
        {
            // Survey names are unique in a generated cave; keep them so.
            taken = cave.Surveys.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            candidates = FreshNames.ToList();
        }
        var free = candidates.Where(c => !taken.Contains(c) && c != old).ToList();
        if (free.Count == 0) free = FreshNames.Where(c => !taken.Contains(c) && c != old).ToList();
        return free[rnd.Next(free.Count)];
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_generated_index_matches_what_each_token_denotes(int batch)
    {
        foreach (var seed in Enumerable.Range(BaseSeed + batch * 25, 25))
        {
            var cave = RenameCaveGenerator.Generate(seed);
            var ws = Build(cave.Files);

            var unresolved = ws.PerFile.Values.SelectMany(m => ws.ValidateEquateReferences(m)).ToList();
            Assert.True(unresolved.Count == 0,
                $"a reference the generator wrote does not resolve: {string.Join("; ", unresolved)}\n{cave}");

            var expected = cave.Sites.ToHashSet();
            var actual = IndexedSites(ws);
            var missing = expected.Except(actual).ToList();
            var extra = actual.Except(expected).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"occurrence index differs from the tokens written.\nmissing: {Describe(cave, missing)}\nextra: {Describe(cave, extra)}\n{cave}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_station_declaration_resolves_back_to_its_own_station(int batch)
    {
        foreach (var seed in Enumerable.Range(BaseSeed + batch * 25, 25))
        {
            var cave = RenameCaveGenerator.Generate(seed);
            var ws = Build(cave.Files);

            foreach (var symbol in cave.Symbols)
            {
                var name = symbol.Name.ToString();
                var declaration = symbol.Kind == SymbolKind.Station
                    ? ws.StationsByQn[name].DeclarationSpan
                    : ws.SurveysByFullName[name].DeclarationSpan;
                var kind = symbol.Kind == SymbolKind.Station ? ReferenceKind.Station : ReferenceKind.Survey;

                var resolved = SymbolReferences.ResolveDeclaration(ws, kind, declaration);

                Assert.True(resolved?.Symbol == symbol,
                    $"the declaration of {symbol} resolves to {resolved?.Symbol}\n{cave}");
                Assert.Equal(symbol.Name.Last, resolved!.Value.Name);
                Assert.Single(SymbolReferences.Find(ws, symbol), r => r.IsDeclaration);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Rename_keeps_every_reference_on_the_same_symbol_and_renaming_back_restores_the_bytes(int batch)
    {
        foreach (var seed in Enumerable.Range(BaseSeed + batch * 25, 25))
        {
            var cave = RenameCaveGenerator.Generate(seed);
            var rnd = new Random(seed ^ 0x2A2A);
            var ws = Build(cave.Files);
            for (int pick = 0; pick < RenamesPerCave; pick++)
                CheckRename(cave, ws, cave.Symbols[rnd.Next(cave.Symbols.Length)], rnd);
        }
    }

    private static void CheckRename(GeneratedRenameCave cave, WorkspaceSemanticModel ws, SymbolId target, Random rnd)
    {
        var oldName = target.Name.Last;
        var newName = PickNewName(cave, target, rnd);
        var renamedTo = target with { Name = new QualifiedName(target.Name.Parts.SetItem(target.Name.Parts.Length - 1, newName)) };
        string context = $"rename {target} -> {newName}\n{cave}";

        // Coverage: the plan is exactly the tokens of the symbol, and drops none the index holds.
        var plan = SymbolRenamePlan.Compute(ws, target, oldName, p => cave.Files.GetValueOrDefault(p));
        var planned = plan.SelectMany(e => e.Spans.Select(s => (e.FilePath, s.Start, s.Length))).ToHashSet();
        var expected = cave.Sites.Where(s => s.Symbol == target).Select(s => (s.File, s.Start, s.Length)).ToHashSet();
        Assert.True(planned.SetEquals(expected),
            $"plan differs from the symbol's tokens. planned-only: {string.Join(",", planned.Except(expected))}; " +
            $"missed: {string.Join(",", expected.Except(planned))}\n{context}");
        Assert.Equal(ws.FindOccurrences(target).Select(o => o.Span).Distinct().Count(), planned.Count);

        // Resolution isomorphism.
        var renamedFiles = Apply(cave.Files, plan, newName);
        var after = Build(renamedFiles);
        var renamedSites = cave.Sites.Where(s => s.Symbol == target).ToList();
        int delta = newName.Length - oldName.Length;
        var expectedAfter = cave.Sites.Select(s => Shift(s, renamedSites, delta, target, renamedTo, newName.Length)).ToHashSet();
        var actualAfter = IndexedSites(after);
        var lost = expectedAfter.Except(actualAfter).ToList();
        var gained = actualAfter.Except(expectedAfter).ToList();
        Assert.True(lost.Count == 0 && gained.Count == 0,
            $"rename changed what a token denotes.\nexpected but absent: {string.Join("; ", lost)}\n" +
            $"present but unexpected: {string.Join("; ", gained)}\n{context}\n--- after ---\n" +
            string.Join("\n", renamedFiles.Select(f => $"=== {f.Key}\n{f.Value}")));
        Assert.Empty(after.PerFile.Values.SelectMany(m => after.ValidateEquateReferences(m)));

        // Same diagnostics, modulo the name.
        Assert.Equal(DiagnosticShape(ws), DiagnosticShape(after));

        // Renaming back restores the original bytes.
        var back = SymbolRenamePlan.Compute(after, renamedTo, newName, p => renamedFiles.GetValueOrDefault(p));
        var restored = Apply(renamedFiles, back, oldName);
        foreach (var (path, text) in cave.Files)
            Assert.True(text == restored[path], $"renaming back did not restore {path}\n{context}");
    }

    [Fact]
    public void The_generator_reaches_every_shape_the_properties_rely_on()
    {
        int multiFile = 0, eofToken = 0, crlf = 0, atPaths = 0, deepAtPaths = 0, keywordLike = 0, fixes = 0;
        foreach (var seed in Enumerable.Range(BaseSeed, Caves))
        {
            var cave = RenameCaveGenerator.Generate(seed);
            if (cave.Files.Count > 1) multiFile++;
            if (!cave.Files["/gen/cave.th"].EndsWith('\n')) eofToken++;
            if (cave.Files.Values.Any(t => t.Contains("\r\n"))) crlf++;
            foreach (var (path, text) in cave.Files)
            {
                foreach (var site in cave.Sites.Where(s => s.File == path))
                {
                    if (site.Start > 0 && text[site.Start - 1] == '@') atPaths++;
                    if (site.Start > 0 && text[site.Start - 1] == '.' && site.Symbol.Kind == SymbolKind.Survey) deepAtPaths++;
                    var token = text.Substring(site.Start, site.Length);
                    if (site.Symbol.Kind == SymbolKind.Station && token.StartsWith("end", StringComparison.OrdinalIgnoreCase)) keywordLike++;
                }
                fixes += text.Split('\n').Count(l => l.TrimStart().StartsWith("fix ", StringComparison.Ordinal));
            }
        }

        Assert.InRange(multiFile, Caves / 5, Caves);
        Assert.InRange(eofToken, Caves / 10, Caves);
        Assert.InRange(crlf, Caves / 10, Caves);
        Assert.True(atPaths > Caves, $"only {atPaths} `@` paths");
        Assert.True(deepAtPaths > Caves / 4, $"only {deepAtPaths} multi-level `@` paths");
        Assert.True(keywordLike > Caves, $"only {keywordLike} end-like station tokens");
        Assert.True(fixes > Caves / 2, $"only {fixes} fix lines");
    }

    // A survey that `input`s a file, inside the root survey: a path written in the root reaches through it.
    private const string NestedInputMain = """
        survey cave
          survey system
            input hall.th
          endsurvey
          centreline
            data normal from to length compass clino
            0 1@hall.system 5.0 10 0
          endcentreline
        endsurvey
        """;

    private const string NestedInputHall = """
        survey hall
          centreline
            data normal from to length compass clino
            1 2 4.0 90 0
          endcentreline
        endsurvey
        """;

    [Fact(Skip = "Known bug: an @ path component above an input file's root survey is not an occurrence of that survey, so renaming it leaves the path stale")]
    public void Renaming_a_survey_rewrites_its_name_in_an_at_path_that_continues_into_an_input_file()
    {
        var files = new Dictionary<string, string> { ["/gen/cave.th"] = NestedInputMain, ["/gen/hall.th"] = NestedInputHall };
        var ws = Build(files);
        var system = new SymbolId(SymbolKind.Survey, QualifiedName.Of("cave", "system"));
        int inPath = NestedInputMain.IndexOf("1@hall.system", StringComparison.Ordinal) + "1@hall.".Length;

        var plan = SymbolRenamePlan.Compute(ws, system, "system", p => files.GetValueOrDefault(p));

        var spans = plan.Where(e => e.FilePath == "/gen/cave.th").SelectMany(e => e.Spans).ToList();
        Assert.Contains((NestedInputMain.IndexOf("system", StringComparison.Ordinal), 6), spans);
        Assert.Contains((inPath, 6), spans);
    }

    // Two sub-surveys with the same name under different parents, each referenced from its own parent.
    private static string SameNamedSubSurvey(string parent) => $"""
          survey {parent}
            survey ent
              centreline
                data normal from to length compass clino
                1 2 3.0 0 0
              endcentreline
            endsurvey
            centreline
              data normal from to length compass clino
              5 1@ent 2.0 90 0
            endcentreline
          endsurvey

        """;

    [Fact(Skip = "Known bug: x@sub resolves by the innermost survey name only, so with two same-named sub-surveys every parent's reference goes to the first one")]
    public void An_at_reference_resolves_to_the_sub_survey_of_its_own_parent_when_another_parent_has_one_of_the_same_name()
    {
        var text = "survey cave\n" + SameNamedSubSurvey("c1") + SameNamedSubSurvey("c2") + "endsurvey\n";
        var ws = Build(new Dictionary<string, string> { ["/gen/cave.th"] = text });
        int c2Ref = text.IndexOf("1@ent", text.IndexOf("survey c2", StringComparison.Ordinal), StringComparison.Ordinal);
        int c1Ref = text.IndexOf("1@ent", StringComparison.Ordinal);

        foreach (var (parent, own, other) in new[] { ("c1", c1Ref, c2Ref), ("c2", c2Ref, c1Ref) })
        {
            var occurrences = ws.FindOccurrences(new SymbolId(SymbolKind.Station, QualifiedName.Of("cave", parent, "ent", "1")))
                .Select(o => o.Span.StartOffset).ToList();
            Assert.Contains(own, occurrences);
            Assert.DoesNotContain(other, occurrences);
        }
    }

    private static string Describe(GeneratedRenameCave cave, IEnumerable<OracleSite> sites) =>
        string.Join("; ", sites.Select(s =>
        {
            var text = cave.Files.TryGetValue(s.File, out var t) && s.Start + s.Length <= t.Length
                ? t.Substring(s.Start, s.Length) : "?";
            return $"{s.Symbol} '{text}' at {s.File}:{s.Start}";
        }));
}
