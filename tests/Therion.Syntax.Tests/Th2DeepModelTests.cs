// LANG-07 — .th2 deep model: typed point/line/area options, inline subtypes, type validation.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class Th2DeepModelTests
{
    private static TherionFile Parse(string text) => new Th2Parser().Parse("/p/a.th2", text).Value!;

    private static T Single<T>(string text) where T : TherionNode =>
        Descend(Parse(text)).OfType<T>().Single();

    private static System.Collections.Generic.IEnumerable<TherionNode> Descend(TherionNode node)
    {
        yield return node;
        var kids = node switch
        {
            TherionFile f => f.Children,
            ScrapBlock s => s.Children,
            _ => System.Collections.Immutable.ImmutableArray<TherionNode>.Empty,
        };
        foreach (var k in kids)
            foreach (var d in Descend(k))
                yield return d;
    }

    [Fact]
    public void Point_options_are_parsed_typed()
    {
        var p = Single<PointObject>("""
            scrap s
              point 12.0 34.0 station -name 4@cave -scale s -clip off
            endscrap
            """);
        Assert.Equal("station", p.PointType);
        Assert.Equal("4@cave", p.Options.Name);
        Assert.Equal("s", p.Options.Scale);
        Assert.False(p.Options.Clip);
    }

    [Fact]
    public void Inline_subtype_is_split_from_base_type()
    {
        var p = Single<PointObject>("""
            scrap s
              point 0 0 station:fixed
            endscrap
            """);
        Assert.Equal("station", p.BaseType);
        Assert.Equal("fixed", p.InlineSubtype);
        Assert.Equal("fixed", p.Subtype);
    }

    [Fact]
    public void Subtype_option_used_when_no_inline_subtype()
    {
        var p = Single<PointObject>("""
            scrap s
              point 0 0 water-flow -subtype intermittent
            endscrap
            """);
        Assert.Null(p.InlineSubtype);
        Assert.Equal("intermittent", p.Subtype);
    }

    [Fact]
    public void Point_orientation_and_value_parse()
    {
        var p = Single<PointObject>("""
            scrap s
              point 0 0 passage-height -value [10 5] -orientation 270
            endscrap
            """);
        Assert.Equal(270, p.Options.Orientation);
        Assert.Equal("[10 5]", p.Options.Value);
    }

    [Fact]
    public void Line_id_subtype_and_outline_parse()
    {
        var l = Single<LineObject>("""
            scrap s
              line wall -id w1 -outline out -close on
                0 0
                1 1
              endline
            endscrap
            """);
        Assert.Equal("wall", l.LineType);
        Assert.Equal("w1", l.Id);
        Assert.Equal("out", l.Outline);
        Assert.Equal("on", l.Close);
        Assert.Equal(2, l.Vertices.Length);
    }

    [Fact]
    public void User_defined_line_type_is_accepted_without_warning()
    {
        var r = new Th2Parser().Parse("/p/a.th2", """
            scrap s
              line u:splay
                0 0
                1 1
              endline
            endscrap
            """);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.Th2UnknownLineType);
        Assert.Equal("u", r.Value!.Children.OfType<ScrapBlock>().Single()
            .Children.OfType<LineObject>().Single().BaseType);
    }

    [Fact]
    public void Inline_subtype_line_type_is_accepted()
    {
        var r = new Th2Parser().Parse("/p/a.th2", """
            scrap s
              area water
                wall:blocks
              endarea
            endscrap
            """);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.Th2UnknownAreaType);
    }

    [Fact]
    public void Unknown_point_line_area_types_warn()
    {
        var r = new Th2Parser().Parse("/p/a.th2", """
            scrap s
              point 0 0 wibble
              line wobble
                0 0
              endline
              area wubble
                l1
              endarea
            endscrap
            """);
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.Th2UnknownPointType);
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.Th2UnknownLineType);
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.Th2UnknownAreaType);
    }

    [Fact]
    public void Line_point_options_are_parsed()
    {
        var l = Single<LineObject>("""
            scrap s
              line wall
                0 0
                1 1 -smooth off
                2 2 -mark p1
              endline
            endscrap
            """);
        Assert.Equal("off", l.Vertices[1].Options.Get("smooth"));
        Assert.Equal("p1", l.Vertices[2].Options.Mark);
    }

    [Fact]
    public void Quoted_text_option_is_unquoted()
    {
        var p = Single<PointObject>("""
            scrap s
              point 0 0 label -text "Main Chamber"
            endscrap
            """);
        Assert.Equal("Main Chamber", p.Options.Text);
    }
}
