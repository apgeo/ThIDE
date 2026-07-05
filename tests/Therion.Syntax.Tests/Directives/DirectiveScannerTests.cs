// Tests for DirectiveScanner: region pairing, nesting, fold offsets and the
// unclosed/unmatched warnings (TH_DIR_001/002).

using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Directives;

namespace Therion.Syntax.Tests.Directives;

public class DirectiveScannerTests
{
    [Fact]
    public void No_directives_returns_empty_fast()
    {
        var r = DirectiveScanner.Scan("survey s\n  centreline\n  endcentreline\nendsurvey\n");
        Assert.Same(DirectiveScanResult.Empty, r);
    }

    [Fact]
    public void Region_pairs_and_folds_the_block()
    {
        // Matches the request's example: a region bracketing centreline data rows.
        const string src =
            "    S4 -    2.33 165.0 45.6\n" +
            "    #@region 'Zone A'\n" +
            "    S4 -    2.39 237.1 15.8\n" +
            "    S4 -    3.12 239.0 62.2\n" +
            "    #@endregion\n" +
            "    S4 -    4.75 23.9 -9.7\n";
        var r = DirectiveScanner.Scan(src, "/d/a.th");

        Assert.Empty(r.Diagnostics);
        var region = Assert.Single(r.Regions);
        Assert.Equal("Zone A", region.Title);
        Assert.Equal(2, region.StartLine);
        Assert.Equal(5, region.EndLine);
        Assert.True(region.IsClosed);
        // Fold spans from the region line start to the end of the endregion line.
        Assert.Equal(src.IndexOf("    #@region", System.StringComparison.Ordinal), region.StartOffset);
        int endLineStart = src.IndexOf("    #@endregion", System.StringComparison.Ordinal);
        Assert.Equal(endLineStart + "    #@endregion".Length, region.EndOffset);
    }

    [Fact]
    public void Endregion_title_is_optional()
    {
        var r = DirectiveScanner.Scan("#@region 'A'\nx\n#@endregion 'A'\n");
        Assert.Empty(r.Diagnostics);
        Assert.Single(r.FoldableRegions());
    }

    [Fact]
    public void Nested_regions_pair_innermost_first()
    {
        const string src =
            "#@region 'outer'\n" +
            "a\n" +
            "#@region 'inner'\n" +
            "b\n" +
            "#@endregion\n" +
            "c\n" +
            "#@endregion\n";
        var r = DirectiveScanner.Scan(src);
        Assert.Empty(r.Diagnostics);
        Assert.Equal(2, r.Regions.Length);
        // innermost closes first
        var inner = r.Regions[0];
        var outer = r.Regions[1];
        Assert.Equal("inner", inner.Title);
        Assert.Equal("outer", outer.Title);
        Assert.True(outer.StartOffset < inner.StartOffset && inner.EndOffset < outer.EndOffset);
    }

    [Fact]
    public void Unclosed_region_warns_and_is_not_foldable()
    {
        var r = DirectiveScanner.Scan("#@region 'lonely'\nx\ny\n");
        var d = Assert.Single(r.Diagnostics);
        Assert.Equal(DiagnosticCodes.DirectiveUnclosedRegion, d.Code.Value);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Empty(r.FoldableRegions());
    }

    [Fact]
    public void Unmatched_endregion_warns()
    {
        var r = DirectiveScanner.Scan("x\n#@endregion\ny\n");
        var d = Assert.Single(r.Diagnostics);
        Assert.Equal(DiagnosticCodes.DirectiveUnmatchedEndRegion, d.Code.Value);
    }

    [Fact]
    public void Directive_type_matching_is_case_insensitive()
    {
        var r = DirectiveScanner.Scan("#@REGION 'A'\nx\n#@EndRegion\n");
        Assert.Empty(r.Diagnostics);
        Assert.Single(r.FoldableRegions());
    }

    [Fact]
    public void Crlf_offsets_are_correct()
    {
        const string src = "#@region 'A'\r\nx\r\n#@endregion\r\n";
        var r = DirectiveScanner.Scan(src);
        var region = Assert.Single(r.Regions);
        Assert.Equal(0, region.StartOffset);
        Assert.Equal(src.IndexOf("#@endregion", System.StringComparison.Ordinal) + "#@endregion".Length,
            region.EndOffset);
    }

    [Fact]
    public void All_directives_are_collected_even_unknown_types()
    {
        // Unknown directive types are kept (for future consumers) and never warned about.
        var r = DirectiveScanner.Scan("#@todo 'later'\n#@region 'A'\n#@endregion\n");
        Assert.Equal(3, r.Directives.Length);
        Assert.Equal("todo", r.Directives[0].Type);
        Assert.Empty(r.Diagnostics);
    }
}
