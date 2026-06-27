// LANG-02 / LANG-03 — typed .thconfig model: layout (with options + opaque code blocks),
// cs (output CRS), select/unselect, export (-fmt/-output), maps, and the source…endsource block.
// Also covers the line-continuation-with-trailing-whitespace tokenizer fix.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ThconfigTypedModelTests
{
    private static TherionFile Parse(string text)
    {
        var r = new ThconfigParser().Parse("x.thconfig", text);
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return r.Value!;
    }

    [Fact]
    public void Cs_is_typed_and_validated()
    {
        var file = Parse("cs EPSG:3794");
        var cs = file.Children.OfType<CsCommand>().Single();
        Assert.Equal("EPSG:3794", cs.System);
    }

    [Fact]
    public void Unknown_output_cs_warns()
    {
        var r = new ThconfigParser().Parse("x.thconfig", "cs notacrs");
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownCoordinateSystem);
    }

    [Fact]
    public void Select_and_unselect_are_typed()
    {
        var file = Parse("""
            select cave -recursive on
            unselect cave.spur
            """);
        var sel = file.Children.OfType<SelectCommand>().ToList();
        Assert.Equal(2, sel.Count);
        Assert.False(sel[0].IsUnselect);
        Assert.Equal("cave", sel[0].Object);
        Assert.True(sel[1].IsUnselect);
    }

    [Fact]
    public void Export_captures_format_and_output()
    {
        var file = Parse("export model -fmt survex -output rez/cave.3d");
        var exp = file.Children.OfType<ExportCommand>().Single();
        Assert.Equal("model", exp.ExportType);
        Assert.Equal("survex", exp.Format);
        Assert.Equal("rez/cave.3d", exp.Output);
    }

    [Fact]
    public void Maps_on_off_is_typed()
    {
        var file = Parse("""
            maps off
            maps-offset on
            """);
        var maps = file.Children.OfType<MapsCommand>().ToList();
        Assert.Equal(2, maps.Count);
        Assert.False(maps[0].On);
        Assert.False(maps[0].IsOffset);
        Assert.True(maps[1].On);
        Assert.True(maps[1].IsOffset);
    }

    [Fact]
    public void Layout_block_is_typed_with_options_and_opaque_code()
    {
        var file = Parse("""
            layout l1
              copy base
              scale 1 500
              code metapost
                def foo = enddef;
              endcode
              legend on
            endlayout
            """);
        var layout = file.Children.OfType<LayoutCommand>().Single();
        Assert.Equal("l1", layout.Id);
        Assert.True(layout.IsTerminated);
        Assert.Equal("base", layout.CopyFrom);
        Assert.Contains(layout.Options, o => o.Key == "scale" && o.Value == "1 500");
        Assert.Contains(layout.Options, o => o.Key == "legend" && o.Value == "on");
        // The metapost body line ("def foo = enddef;") must NOT become a layout option.
        Assert.DoesNotContain(layout.Options, o => o.Key == "def");
        Assert.Single(layout.CodeBlocks);
        Assert.Equal("metapost", layout.CodeBlocks[0].Language);
    }

    [Fact]
    public void Layout_captures_inner_cs()
    {
        var file = Parse("""
            layout l1
              cs UTM33
            endlayout
            """);
        Assert.Equal("UTM33", file.Children.OfType<LayoutCommand>().Single().CoordinateSystem);
    }

    [Fact]
    public void Header_only_layout_without_endlayout_does_not_swallow_following_commands()
    {
        // A bare `layout id` with no endlayout must not consume the export below it.
        var file = Parse("""
            layout default
            export model -o cave.lox
            """);
        Assert.Single(file.Children.OfType<LayoutCommand>());
        Assert.Single(file.Children.OfType<ExportCommand>());
        Assert.False(file.Children.OfType<LayoutCommand>().Single().IsTerminated);
    }

    [Fact]
    public void Inline_source_endsource_block_is_consumed_without_warnings()
    {
        var r = new ThconfigParser().Parse("x.thconfig", """
            source
              survey s
                centreline
                  data normal from to length compass clino
                  1 2 5 0 0
                endcentreline
              endsurvey
            endsource
            select s
            """);
        Assert.Empty(r.Diagnostics); // no "unknown command" leakage from the inline body
        Assert.Single(r.Value!.Children.OfType<SelectCommand>());
    }

    [Fact]
    public void Single_line_source_is_still_followable_for_traversal()
    {
        // Must stay an UnknownCommand (keyword "source") so SourceGraph keeps following includes.
        var file = Parse("source cave.th");
        var src = file.Children.OfType<UnknownCommand>()
            .Single(c => string.Equals(c.Keyword, "source", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains("cave.th", SourceGraph.DependencyTokens(file));
        Assert.NotNull(src);
    }

    [Fact]
    public void Export_with_trailing_whitespace_after_backslash_continues()
    {
        // Regression: a line ending with "\   " (backslash + spaces) must continue onto the
        // next line, so -layout-scale is part of the export, not a stray top-level command.
        var r = new ThconfigParser().Parse("x.thconfig", "export map -projection plan \\  \n  -layout-scale 1 500\n");
        Assert.Empty(r.Diagnostics);
        var exp = r.Value!.Children.OfType<ExportCommand>().Single();
        Assert.Equal("map", exp.ExportType);
    }
}
