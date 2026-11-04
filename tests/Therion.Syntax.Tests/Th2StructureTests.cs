// .th2 structure: object spans, where the parser resumes after an object (terminated or not),
// honest scrap termination, Bézier vertices, unknown line options and string edge cases, plus a
// property over generated scraps: no scrap or object is lost or moved, and every missing end line
// is reported exactly once.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax.Tests.Properties;

namespace Therion.Syntax.Tests;

public class Th2StructureTests
{
    private static ParseResult<TherionFile> Parse(string src) => new Th2Parser().Parse("a.th2", src);

    private static ScrapBlock Scrap(ParseResult<TherionFile> r) => Assert.Single(r.Value!.Children.OfType<ScrapBlock>());

    // A logical line's span takes in its line break, so an object ends just after the break of its
    // last line (the last vertex for a line: `endline` is not part of it).
    [Fact]
    public void Line_span_runs_from_its_header_to_the_end_of_its_last_vertex()
    {
        const string src = "scrap s1\n  line wall\n    10 20\n    30 40 -subtype pit\n  endline\nendscrap\n";
        var line = Assert.Single(Scrap(Parse(src)).Children.OfType<LineObject>());

        Assert.Equal(src.IndexOf("line wall", StringComparison.Ordinal), line.Span.StartOffset);
        int lastVertexEnd = src.IndexOf("30 40 -subtype pit", StringComparison.Ordinal) + "30 40 -subtype pit\n".Length;
        Assert.Equal(lastVertexEnd, line.Span.StartOffset + line.Span.Length);
        Assert.Equal(2, line.Span.Start.Line);
    }

    [Fact]
    public void Line_without_vertices_spans_its_header_only()
    {
        const string src = "scrap s1\n  line wall -id w1\n  endline\nendscrap\n";
        var line = Assert.Single(Scrap(Parse(src)).Children.OfType<LineObject>());
        Assert.Equal(src.IndexOf("line wall", StringComparison.Ordinal), line.Span.StartOffset);
        Assert.Equal("line wall -id w1\n".Length, line.Span.Length);
    }

    [Fact]
    public void Scrap_span_runs_from_its_header_to_its_last_object()
    {
        const string src = "scrap s1\n  point 1 2 station\n  point 3 4 debris\nendscrap\n";
        var scrap = Scrap(Parse(src));
        Assert.Equal(0, scrap.Span.StartOffset);
        Assert.Equal(src.IndexOf("endscrap", StringComparison.Ordinal), scrap.Span.StartOffset + scrap.Span.Length);
    }

    [Fact]
    public void Area_span_starts_at_its_header()
    {
        const string src = "scrap s1\n  area water\n    w1\n  endarea\nendscrap\n";
        var area = Assert.Single(Scrap(Parse(src)).Children.OfType<AreaObject>());
        Assert.Equal(src.IndexOf("area water", StringComparison.Ordinal), area.Span.StartOffset);
    }

    [Fact]
    public void Object_after_endarea_is_the_next_sibling()
    {
        var r = Parse("scrap s1\n  area water\n    w1\n    w2\n  endarea\n  point 1 2 station\n  line wall\n    1 1\n  endline\nendscrap\n");
        var scrap = Scrap(r);
        var kinds = scrap.Children.Where(c => c is not TrivialComment).Select(c => c.GetType().Name).ToArray();
        Assert.Equal(new[] { nameof(AreaObject), nameof(PointObject), nameof(LineObject) }, kinds);
        Assert.Equal(new[] { "w1", "w2" }, scrap.Children.OfType<AreaObject>().Single().BorderLineIds);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void Unterminated_area_stops_at_the_next_object_and_is_reported()
    {
        var r = Parse("scrap s1\n  area water\n    w1\n  point 1 2 station\nendscrap\n");
        var scrap = Scrap(r);
        var area = Assert.Single(scrap.Children.OfType<AreaObject>());
        Assert.False(area.IsTerminated);
        Assert.Equal(new[] { "w1" }, area.BorderLineIds);
        Assert.Single(scrap.Children.OfType<PointObject>());
        Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedArea);
        Assert.True(scrap.IsTerminated);
    }

    [Fact]
    public void Unterminated_line_leaves_a_following_join_to_the_scrap()
    {
        var r = Parse("scrap s1\n  line wall -id a\n    1 1\n  join a b\n  line wall -id b\n    2 2\n  endline\nendscrap\n");
        var scrap = Scrap(r);
        var join = Assert.Single(scrap.Children.OfType<JoinCommand>());
        Assert.Equal(new[] { "a", "b" }, join.Targets);
        Assert.Equal(2, scrap.Children.OfType<LineObject>().Count());
        Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedLine);
    }

    [Fact]
    public void Unterminated_area_leaves_a_following_join_to_the_scrap()
    {
        var r = Parse("scrap s1\n  area water\n    w1\n  join a b\nendscrap\n");
        var scrap = Scrap(r);
        Assert.Equal(new[] { "w1" }, Assert.Single(scrap.Children.OfType<AreaObject>()).BorderLineIds);
        Assert.Single(scrap.Children.OfType<JoinCommand>());
    }

    [Fact]
    public void Scrap_without_endscrap_is_not_terminated_even_with_a_body()
    {
        var r = Parse("scrap s1\n  point 1 2 station\n");
        var scrap = Scrap(r);
        Assert.False(scrap.IsTerminated);
        Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2UnterminatedScrap);
    }

    [Theory]
    [InlineData("scrap s1\nendscrap\n")]
    [InlineData("scrap s1\nendscrap")]
    [InlineData("scrap s1\n  point 1 2 station\nendscrap s1\n")]
    public void Scrap_with_endscrap_is_terminated(string src)
    {
        var r = Parse(src);
        Assert.True(Scrap(r).IsTerminated);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2UnterminatedScrap);
    }

    [Fact]
    public void Bezier_vertex_ends_at_its_last_pair_and_keeps_both_control_points()
    {
        var line = Assert.Single(Scrap(Parse("scrap s1\n  line wall\n    0 0\n    1 2 3 4 5 6\n  endline\nendscrap\n"))
            .Children.OfType<LineObject>());
        Assert.Equal(2, line.Vertices.Length);
        Assert.Null(line.Vertices[0].Control1);
        var v = line.Vertices[1];
        Assert.Equal((5d, 6d), (v.X, v.Y));
        Assert.Equal((1d, 2d), v.Control1);
        Assert.Equal((3d, 4d), v.Control2);
        Assert.Equal("", v.OptionsRaw);
    }

    [Fact]
    public void Vertex_with_two_numbers_and_options_is_not_a_bezier_vertex()
    {
        var line = Assert.Single(Scrap(Parse("scrap s1\n  line wall\n    7 8 -subtype pit\n  endline\nendscrap\n"))
            .Children.OfType<LineObject>());
        var v = Assert.Single(line.Vertices);
        Assert.Equal((7d, 8d), (v.X, v.Y));
        Assert.Null(v.Control2);
        Assert.Equal("pit", v.Options.Subtype);
    }

    [Fact]
    public void Unknown_option_on_a_line_header_is_reported_at_the_option()
    {
        const string src = "scrap s1\n  line wall -bogus 1\n    1 1\n  endline\nendscrap\n";
        var r = Parse(src);
        var d = Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2UnknownOption);
        Assert.Contains("-bogus", d.Message);
        Assert.Equal(src.IndexOf("-bogus", StringComparison.Ordinal), d.Span.StartOffset);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Join_targets_stop_at_the_first_option_and_keep_marks()
    {
        var r = Parse("scrap s1\n  join a:end b@s2:0 c -smooth off\nendscrap\n");
        var join = Assert.Single(Scrap(r).Children.OfType<JoinCommand>());
        Assert.Equal(new[] { "a:end", "b@s2:0", "c" }, join.Targets);
        Assert.Equal("-smooth off", join.OptionsRaw);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void Th_join_targets_keep_marks_and_scrap_qualifiers()
    {
        var r = new ThParser().Parse("a.th", "survey s\n  join l1@s1:end l2@s2:0 -count 2\nendsurvey\n");
        var join = Assert.Single(Assert.Single(r.Value!.Children.OfType<SurveyCommand>()).Children.OfType<JoinCommand>());
        Assert.Equal(new[] { "l1@s1:end", "l2@s2:0" }, join.Targets);
        Assert.Equal("-count 2", join.OptionsRaw);
    }

    [Theory]
    [InlineData("\"", "")]            // a lone quote: an empty, unterminated string
    [InlineData("abc\"", "abc\"")]    // a quote at the end of a bare word is part of the word
    [InlineData("a\"b", "a\"b")]
    public void Unquote_edge_cases(string literal, string value) =>
        Assert.Equal(value, TherionStrings.Unquote(literal));

    [Fact]
    public void Sketch_path_in_quotes_is_unquoted()
    {
        var sk = Assert.Single(Scrap(Parse("scrap s1 -sketch \"bg 1.xvi\" 10 20\nendscrap\n")).Sketches);
        Assert.Equal("bg 1.xvi", sk.XviPath);
        Assert.Equal((10d, 20d), (sk.X, sk.Y));
    }

    // ---- property over generated scraps ----

    private const int Th2Seed = 131_000;

    [Fact]
    public void Generated_scraps_keep_every_scrap_and_object_and_report_each_missing_end_line()
    {
        var parser = new Th2Parser();
        for (int seed = Th2Seed; seed < Th2Seed + PropertyRuns.Count(400); seed++)
        {
            var file = Th2Soup.Generate(seed);
            var r = parser.Parse("gen.th2", file.Text);
            var problems = Th2Problems(file, r);
            if (problems.Count > 0)
                Assert.Fail($"th2 seed {seed}:\n  {string.Join("\n  ", problems.Take(8))}\nsource: \"{SpanChecks.Escape(file.Text)}\"");
        }
    }

    internal static List<string> Th2Problems(G2File file, ParseResult<TherionFile> r)
    {
        var problems = new List<string>();
        var scraps = r.Value!.Children.OfType<ScrapBlock>().ToList();
        if (scraps.Count != file.Scraps.Count)
            return new List<string> { $"{scraps.Count} scraps parsed, {file.Scraps.Count} written" };

        for (int i = 0; i < scraps.Count; i++)
        {
            var (got, want) = (scraps[i], file.Scraps[i]);
            if (got.Id != want.Id) problems.Add($"scrap {i} is '{got.Id}', expected '{want.Id}'");
            if (got.IsTerminated != want.Terminated) problems.Add($"scrap {want.Id}: IsTerminated {got.IsTerminated}");
            if (got.Span.StartOffset != want.HeaderOffset) problems.Add($"scrap {want.Id} starts at {got.Span.StartOffset}, header at {want.HeaderOffset}");

            var objects = got.Children.Where(c => c is not TrivialComment).ToList();
            var kinds = objects.Select(KindOf).ToList();
            if (!kinds.SequenceEqual(want.Objects.Select(o => (G2Kind?)o.Kind)))
            {
                problems.Add($"scrap {want.Id} holds [{string.Join(", ", kinds)}], written [{string.Join(", ", want.Objects.Select(o => o.Kind))}]");
                continue;
            }
            for (int k = 0; k < objects.Count; k++)
            {
                var w = want.Objects[k];
                if (objects[k].Span.StartOffset != w.HeaderOffset)
                    problems.Add($"scrap {want.Id} object {k} ({w.Kind}) starts at {objects[k].Span.StartOffset}, header at {w.HeaderOffset}");
                switch (objects[k])
                {
                    case LineObject l:
                        if (l.IsTerminated != w.Terminated) problems.Add($"scrap {want.Id} line {k}: IsTerminated {l.IsTerminated}");
                        var ends = l.Vertices.Select(v => (v.X, v.Y, v.Control1 is not null)).ToList();
                        if (!ends.SequenceEqual(w.Vertices))
                            problems.Add($"scrap {want.Id} line {k}: vertices [{string.Join(" ", ends)}], written [{string.Join(" ", w.Vertices)}]");
                        break;
                    case AreaObject a when a.IsTerminated != w.Terminated:
                        problems.Add($"scrap {want.Id} area {k}: IsTerminated {a.IsTerminated}");
                        break;
                }
            }
        }

        var objectsWritten = file.Scraps.SelectMany(s => s.Objects).ToList();
        void Count(string code, int expected)
        {
            int n = r.Diagnostics.Count(d => d.Code == code);
            if (n != expected) problems.Add($"{n} × {code}, expected {expected}");
        }
        Count(DiagnosticCodes.Th2MalformedLine, objectsWritten.Count(o => o.Kind == G2Kind.Line && !o.Terminated));
        Count(DiagnosticCodes.Th2MalformedArea, objectsWritten.Count(o => o.Kind == G2Kind.Area && !o.Terminated));
        Count(DiagnosticCodes.Th2UnterminatedScrap, file.Scraps.Count(s => !s.Terminated));
        foreach (var d in r.Diagnostics.Where(d => d.Code != DiagnosticCodes.Th2MalformedLine
                     && d.Code != DiagnosticCodes.Th2MalformedArea && d.Code != DiagnosticCodes.Th2UnterminatedScrap))
            problems.Add($"unexpected {d}");
        return problems;
    }

    private static G2Kind? KindOf(TherionNode n) => n switch
    {
        PointObject => G2Kind.Point,
        LineObject => G2Kind.Line,
        AreaObject => G2Kind.Area,
        JoinCommand => G2Kind.Join,
        _ => null,
    };
}
