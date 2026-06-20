// M4 — .th2 parser tests.
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class Th2ParserTests
{
    [Fact]
    public void Parses_scrap_with_point_and_line_block()
    {
        const string src = """
            scrap s1 -projection plan
              point 100 200 station -name "1.1"
              line wall
                100 200
                150 250
                200 200
              endline
            endscrap
            """;
        var r = new Th2Parser().Parse("a.th2", src);
        Assert.NotNull(r.Value);
        var scrap = Assert.Single(r.Value!.Children.OfType<ScrapBlock>());
        Assert.Equal("s1", scrap.Id);
        Assert.True(scrap.IsTerminated);
        var pt = Assert.Single(scrap.Children.OfType<PointObject>());
        Assert.Equal(100, pt.X);
        Assert.Equal("station", pt.PointType);
        var ln = Assert.Single(scrap.Children.OfType<LineObject>());
        Assert.Equal("wall", ln.LineType);
        Assert.Equal(3, ln.Vertices.Length);
        Assert.True(ln.IsTerminated);
    }

    [Fact]
    public void Extracts_sketch_reference_from_scrap_header()
    {
        const string src = """
            scrap s2 -sketch bg.xvi 0 0
            endscrap
            """;
        var r = new Th2Parser().Parse("a.th2", src);
        var scrap = Assert.Single(r.Value!.Children.OfType<ScrapBlock>());
        var sk = Assert.Single(scrap.Sketches);
        Assert.Equal("bg.xvi", sk.XviPath);
    }

    [Fact]
    public void Area_block_collects_border_ids()
    {
        const string src = """
            area water
              wall1
              wall2
            endarea
            """;
        var r = new Th2Parser().Parse("a.th2", src);
        var area = Assert.Single(r.Value!.Children.OfType<AreaObject>());
        Assert.Equal("water", area.AreaType);
        Assert.Contains("wall1", area.BorderLineIds);
        Assert.Contains("wall2", area.BorderLineIds);
        Assert.True(area.IsTerminated);
    }

    [Fact]
    public void Unterminated_line_emits_diagnostic()
    {
        const string src = "line wall\n 0 0\n 1 1\n";
        var r = new Th2Parser().Parse("a.th2", src);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedLine);
    }

    [Fact]
    public void Malformed_point_emits_diagnostic()
    {
        var r = new Th2Parser().Parse("a.th2", "point 1 2\n");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedPoint);
    }
}
