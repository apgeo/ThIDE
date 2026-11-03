// Span fidelity in the semantic model, over generated caves: rename and find-references splice
// by occurrence spans, and go-to-definition turns a declaration span back into a station. So every
// occurrence must slice the source to its symbol's name, every station must be found again from
// its own declaration span, and no two stations may share one.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics.Tests.Properties.CaveGen;

namespace Therion.Semantics.Tests.Properties;

public class OccurrenceSpanProperties
{
    private const int CaveSeed = 81_000;

    private static IEnumerable<(int Seed, List<PrintedFile> Files, WorkspaceSemanticModel Ws)> Cases(int runs)
    {
        for (int seed = CaveSeed; seed < CaveSeed + PropertyRuns.Count(runs); seed++)
        {
            var files = CavePrinter.Print(CaveGenerator.Generate(seed), PrintFeatures.All, seed * 5 + 1);
            yield return (seed, files, BinderOracle.Build(files));
        }
    }

    /// <summary>The text an occurrence of <paramref name="symbol"/> must cover: a station's point name, a survey's name.</summary>
    private static string ExpectedText(SymbolId symbol) =>
        symbol.Kind == SymbolKind.Station ? StationRef.Parse(symbol.Name.Last).PointWithoutMark : symbol.Name.Last;

    private static void Check(int seed, List<PrintedFile> files, List<string> problems)
    {
        if (problems.Count > 0)
            Assert.Fail($"cave seed {seed}:\n  {string.Join("\n  ", problems.Take(8))}\n{BinderOracle.Dump(files)}");
    }

    private static string? Slice(Dictionary<string, string> texts, SourceSpan span)
    {
        if (!texts.TryGetValue(span.FilePath, out var text)) return null;
        if (span.StartOffset < 0 || span.StartOffset + span.Length > text.Length) return null;
        return text.Substring(span.StartOffset, span.Length);
    }

    [Fact]
    public void Every_occurrence_slices_the_source_to_its_symbol_name()
    {
        foreach (var (seed, files, ws) in Cases(150))
        {
            var texts = files.ToDictionary(f => f.Path, f => f.Text);
            var problems = new List<string>();
            void Expect(SymbolOccurrence o, string where)
            {
                var slice = Slice(texts, o.Span);
                var expected = ExpectedText(o.Symbol);
                if (slice != expected)
                    problems.Add($"{where}: {o.Symbol} occurrence at {o.Span} slices to '{slice ?? "<outside the file>"}', expected '{expected}'");
            }
            foreach (var (path, model) in ws.PerFile)
                foreach (var o in model.Occurrences.All)
                {
                    if (o.Span.FilePath != path) problems.Add($"{o.Symbol} occurrence of {path} points into {o.Span.FilePath}");
                    Expect(o, "file index");
                }
            foreach (var (_, list) in ws.StationOccurrences)
                foreach (var o in list) Expect(o, "workspace index");
            Check(seed, files, problems);
        }
    }

    [Fact]
    public void The_occurrence_at_an_offset_is_the_one_whose_span_holds_it()
    {
        foreach (var (seed, files, ws) in Cases(100))
        {
            var problems = new List<string>();
            foreach (var model in ws.PerFile.Values)
                foreach (var o in model.Occurrences.All)
                    foreach (var offset in new[] { o.Span.StartOffset, o.Span.StartOffset + o.Span.Length - 1 })
                    {
                        var at = model.Occurrences.At(offset);
                        if (at is null || at.Value.Span.StartOffset != o.Span.StartOffset || at.Value.Span.Length != o.Span.Length)
                            problems.Add($"At({offset}) gives {at?.Span.ToString() ?? "nothing"}, expected the {o.Symbol} occurrence at {o.Span}+{o.Span.Length}");
                    }
            Check(seed, files, problems);
        }
    }

    [Fact]
    public void No_two_stations_share_a_declaration_span_and_each_is_found_again_from_it()
    {
        foreach (var (seed, files, ws) in Cases(150))
        {
            var problems = new List<string>();
            var all = ws.PerFile.Values.SelectMany(m => m.Stations.Values).ToList();
            foreach (var g in all.GroupBy(s => (s.DeclarationSpan.FilePath, s.DeclarationSpan.StartOffset)).Where(g => g.Count() > 1))
                problems.Add($"stations {string.Join(", ", g.Select(s => s.Name))} share the declaration span at {g.First().DeclarationSpan}");
            foreach (var s in all)
            {
                var found = ws.FindStationByDeclaration(s.DeclarationSpan);
                if (found is null || !found.Name.Equals(s.Name))
                    problems.Add($"go-to-definition span of {s.Name} ({s.DeclarationSpan}) leads to {found?.Name.ToString() ?? "nothing"}");
            }
            var texts = files.ToDictionary(f => f.Path, f => f.Text);
            foreach (var s in all)
            {
                var slice = Slice(texts, s.DeclarationSpan);
                var point = StationRef.Parse(s.Name.Last).Point;
                if (slice is null || !slice.Contains(point, StringComparison.Ordinal))
                    problems.Add($"declaration span of {s.Name} slices to '{slice ?? "<outside the file>"}'");
            }
            Check(seed, files, problems);
        }
    }

    [Fact]
    public void Every_survey_is_found_again_from_its_declaration_and_its_name_occurrence_slices_to_its_name()
    {
        foreach (var (seed, files, ws) in Cases(100))
        {
            var problems = new List<string>();
            var texts = files.ToDictionary(f => f.Path, f => f.Text);
            foreach (var model in ws.PerFile.Values)
                foreach (var sv in model.Surveys.Values)
                {
                    var found = ws.FindSurveyByDeclaration(sv.DeclarationSpan);
                    if (found is null || !found.Name.Equals(sv.Name))
                        problems.Add($"survey {sv.Name} declared at {sv.DeclarationSpan} leads to {found?.Name.ToString() ?? "nothing"}");
                    var decl = model.Occurrences.Of(new SymbolId(SymbolKind.Survey, sv.Name))
                        .Where(o => o.Role == OccurrenceRole.Declaration).ToList();
                    if (decl.Count != 1)
                        problems.Add($"survey {sv.Name} has {decl.Count} declaration occurrences");
                    else if (Slice(texts, decl[0].Span) != sv.Name.Last)
                        problems.Add($"survey {sv.Name} name occurrence slices to '{Slice(texts, decl[0].Span)}'");
                }
            Check(seed, files, problems);
        }
    }
}
