// TherionWriter's two promises: it never leaves a node out of the text without saying so (every
// concrete node type is either written or refused with NotSupportedException), and for what it
// writes, parse → write → parse gives back the same tree, spans aside. The round trip runs over
// generated .th text built from the commands the writer knows and over generated .th2 scraps.

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Therion.Core;
using Therion.Syntax.Tests.Properties;

namespace Therion.Syntax.Tests;

public class WriterRoundTripTests
{
    private static readonly SourceSpan Span = SourceSpan.None;

    private static TherionFile FileOf(params TherionNode[] nodes) =>
        new(Span, "w.th", ImmutableArray.Create(nodes), TherionSyntaxVersion.Default);

    private static string Write(params TherionNode[] nodes) => new TherionWriter().Write(FileOf(nodes));

    [Fact]
    public void Every_node_type_is_written_or_refused_never_dropped()
    {
        var nodeTypes = typeof(TherionNode).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TherionNode)) && !t.IsAbstract && t != typeof(TherionFile))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(nodeTypes);

        var dropped = new List<string>();
        foreach (var type in nodeTypes)
        {
            // An instance with every field at its default: enough to see which branch the writer takes.
            var node = (TherionNode)RuntimeHelpers.GetUninitializedObject(type);
            try
            {
                if (Write(node).Length == 0) dropped.Add(type.Name);
            }
            catch (NotSupportedException) { }                  // refused: the caller learns it
            catch (Exception e) when (e is NullReferenceException or ArgumentNullException or InvalidOperationException)
            {
                // A writer case read a default field: it has a case for this type, which is the point.
            }
        }
        Assert.True(dropped.Count == 0, "written as nothing: " + string.Join(", ", dropped));
    }

    [Fact]
    public void Unsupported_node_names_its_type()
    {
        var map = new MapCommand(Span, "m", "", IsTerminated: true);
        var e = Assert.Throws<NotSupportedException>(() => Write(map));
        Assert.Contains(nameof(MapCommand), e.Message);
    }

    [Theory]
    [InlineData("units length feet")]
    [InlineData("units compass clino grads")]
    [InlineData("calibrate compass 0.5")]
    [InlineData("calibrate length 0.1 1.02")]
    [InlineData("declination 2.5 degrees")]
    [InlineData("declination -")]
    [InlineData("declination")]
    [InlineData("sd length 0.05 metres")]
    [InlineData("grid-angle 3.5 degrees")]
    [InlineData("join a b -smooth off")]
    [InlineData("mark 1 2 fixed")]
    [InlineData("extend left 3")]
    [InlineData("station 4 \"big \"\"hall\"\"\" entrance")]
    [InlineData("station 4")]
    [InlineData("cs UTM33")]
    [InlineData("walls off")]
    public void Command_writes_back_to_the_same_line(string line)
    {
        var text = $"centreline\n  {line}\nendcentreline\n";
        Assert.Equal(text, new TherionWriter().Write(new ThParser().Parse("w.th", text).Value!));
    }

    [Fact]
    public void Group_is_written_with_its_children_and_end_line()
    {
        const string text = "centreline\n  group\n    flags duplicate\n  endgroup\nendcentreline\n";
        Assert.Equal(text, new TherionWriter().Write(new ThParser().Parse("w.th", text).Value!));
    }

    [Fact]
    public void Bezier_vertex_writes_its_control_points_before_its_end_point()
    {
        const string text = "scrap s\n  line wall\n    0 0\n    1 2 3 4 5 6\n    7 8 -subtype pit\n  endline\nendscrap\n";
        Assert.Equal(text, new TherionWriter().Write(new Th2Parser().Parse("w.th2", text).Value!));
    }

    [Fact]
    public void Unterminated_scrap_is_written_without_an_end_line()
    {
        var written = new TherionWriter().Write(new Th2Parser().Parse("w.th2", "scrap s\n  point 1 2 station\n").Value!);
        Assert.DoesNotContain("endscrap", written);
    }

    // ---- parse → write → parse ----

    private const int ThSeed = 151_000;
    private const int Th2Seed = 152_000;

    private static void AssertRoundTrip(string what, int seed, string text, Func<string, ParseResult<TherionFile>> parse)
    {
        var first = parse(text);
        string written;
        try { written = new TherionWriter().Write(first.Value!); }
        catch (NotSupportedException e) { Assert.Fail($"{what} seed {seed}: {e.Message}\nsource: \"{SpanChecks.Escape(text)}\""); return; }
        var second = parse(written);
        var a = AstShape.Dump(first.Value!.Children, spans: false);
        var b = AstShape.Dump(second.Value!.Children, spans: false);
        if (a != b)
        {
            int at = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));
            Assert.Fail($"{what} seed {seed}: the re-parsed tree differs near\n  original: …{a.Substring(Math.Max(0, at - 80), Math.Min(200, a.Length - Math.Max(0, at - 80)))}\n" +
                        $"  re-read:  …{b.Substring(Math.Max(0, at - 80), Math.Min(200, b.Length - Math.Max(0, at - 80)))}\n" +
                        $"source: \"{SpanChecks.Escape(text)}\"\nwritten: \"{SpanChecks.Escape(written)}\"");
        }
        Assert.Equal(written, new TherionWriter().Write(second.Value!));
    }

    [Fact]
    public void Generated_th_reads_back_to_the_same_tree_after_writing()
    {
        var parser = new ThParser();
        for (int seed = ThSeed; seed < ThSeed + PropertyRuns.Count(300); seed++)
            AssertRoundTrip("th", seed, WritableTh(seed), t => parser.Parse("gen/w.th", t));
    }

    [Fact]
    public void Generated_th2_reads_back_to_the_same_tree_after_writing()
    {
        var parser = new Th2Parser();
        for (int seed = Th2Seed; seed < Th2Seed + PropertyRuns.Count(300); seed++)
            AssertRoundTrip("th2", seed, Th2Soup.Generate(seed, missingTerminators: false).Text, t => parser.Parse("gen/w.th2", t));
    }

    /// <summary>Survey / centreline / group text made only of the commands the writer has a form for.</summary>
    private static string WritableTh(int seed)
    {
        var rnd = new SeededRandom(seed);
        var sb = new StringBuilder();
        string N() => (rnd.Next(-4000, 4000) / 8.0).ToString(CultureInfo.InvariantCulture);
        string St() => rnd.Pick(new[] { "1", "2", "a3", "4@sub", "end1", "x-1" });
        void L(int depth, string text) => sb.Append(new string(' ', depth * 2)).Append(text).Append('\n');

        void Centreline(int depth, bool group)
        {
            int n = rnd.Next(1, 10);
            for (int i = 0; i < n; i++)
            {
                switch (rnd.Next(17))
                {
                    case 0: L(depth, "data normal from to length compass clino"); break;
                    case 1: case 2: L(depth, $"{St()} {St()} {Math.Abs(rnd.Next(1, 400) / 4.0).ToString(CultureInfo.InvariantCulture)} {rnd.Next(360)} {rnd.Next(-90, 91)}" + (rnd.Chance(0.2) ? " # leg" : "")); break;
                    case 3: L(depth, $"fix {St()} {N()} {N()} {N()}"); break;
                    case 4: L(depth, $"equate {St()} {St()}"); break;
                    case 5: L(depth, rnd.Chance(0.5) ? "flags duplicate" : "flags not splay"); break;
                    case 6: L(depth, rnd.Pick(new[] { "units length feet", "units compass clino grads", "units tape 2 metres" })); break;
                    case 7: L(depth, rnd.Pick(new[] { "calibrate compass 0.5", "calibrate length 0.1 1.02" })); break;
                    case 8: L(depth, rnd.Pick(new[] { "declination 2.5 degrees", "declination -", "declination 2000 1 deg 2010 2 deg" })); break;
                    case 9: L(depth, "sd length 0.05 metres"); break;
                    case 10: L(depth, $"grid-angle {N()} degrees"); break;
                    case 11: L(depth, $"mark {St()} {rnd.Pick(new[] { "fixed", "painted", "temporary" })}"); break;
                    case 12: L(depth, $"extend {rnd.Pick(new[] { "left", "right", "reverse" })} {St()}"); break;
                    case 13: L(depth, $"station {St()} \"note {rnd.Next(9)}\"" + (rnd.Chance(0.5) ? " entrance" : "")); break;
                    case 14: L(depth, rnd.Pick(new[] { "team \"A. Person\"", "date 2024.05.01", "cs UTM33", "walls off", "break" })); break;
                    case 15 when !group && depth < 6:
                        L(depth, "group");
                        Centreline(depth + 1, group: true);
                        L(depth, "endgroup");
                        break;
                    default: L(depth, "# a comment"); break;
                }
            }
        }

        void Survey(int depth, string name)
        {
            L(depth, rnd.Chance(0.5) ? $"survey {name} -title \"T {name}\"" : $"survey {name}");
            int parts = rnd.Next(1, 4);
            for (int p = 0; p < parts; p++)
            {
                if (depth < 2 && rnd.Chance(0.3)) Survey(depth + 1, name + p);
                else if (rnd.Chance(0.2)) L(depth + 1, "input part.th");
                else
                {
                    L(depth + 1, "centreline");
                    Centreline(depth + 2, group: false);
                    L(depth + 1, "endcentreline");
                }
            }
            L(depth, "endsurvey");
        }

        Survey(0, "s");
        return sb.ToString();
    }
}
