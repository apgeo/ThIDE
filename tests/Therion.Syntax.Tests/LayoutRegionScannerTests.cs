// (embedded code) — LayoutRegionScanner tests. Mirrors the real corpus layout block
// (tests/Corpus/Synthetic/project/Vladusca.thconfig), including the greedy `code metapost` with no
// `endcode` that swallows a later `scale 1 100` into the metapost block.

using System.Collections.Generic;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class LayoutRegionScannerTests
{
    private static EmbeddedRegion? Region(IReadOnlyDictionary<int, EmbeddedRegion> map, int line) =>
        map.TryGetValue(line, out var r) ? r : (EmbeddedRegion?)null;

    [Fact]
    public void Classifies_options_metapost_and_tex_regions()
    {
        var lines = new[]
        {
            "layout l1",                              // 1  opener (absent → Therion)
            "  code metapost",                        // 2  fence (LayoutOption)
            "  def a_water (expr p) =",               // 3  MetaPost
            "    thfill p withcolor (0.0,0.5,1.0);",  // 4  MetaPost
            "  enddef;",                              // 5  MetaPost
            "",                                       // 6  blank
            "   scale 1 100",                         // 7  MetaPost (greedy: no endcode yet!)
            "",                                       // 8  blank
            "   code metapost",                       // 9  MetaPost (inner, still in code)
            "      #fonts_setup(3,4,5);",             // 10 MetaPost
            "   endcode",                             // 11 fence closes block 1 (LayoutOption)
            "  code tex-map",                         // 12 fence (LayoutOption)
            "     \\legendwidth=15cm",                // 13 Tex
            "  endcode",                              // 14 fence (LayoutOption)
            "  legend on",                            // 15 option (LayoutOption)
            "endlayout",                              // 16 closer (absent → Therion)
        };

        var map = LayoutRegionScanner.Scan(lines);

        Assert.Equal(EmbeddedRegion.LayoutOption, Region(map, 2));
        Assert.Equal(EmbeddedRegion.MetaPost, Region(map, 3));
        Assert.Equal(EmbeddedRegion.MetaPost, Region(map, 5));
        // Greedy code: `scale 1 100` is INSIDE the first (unterminated) metapost block.
        Assert.Equal(EmbeddedRegion.MetaPost, Region(map, 7));
        Assert.Equal(EmbeddedRegion.MetaPost, Region(map, 9));
        Assert.Equal(EmbeddedRegion.MetaPost, Region(map, 10));
        Assert.Equal(EmbeddedRegion.LayoutOption, Region(map, 11));
        Assert.Equal(EmbeddedRegion.LayoutOption, Region(map, 12));
        Assert.Equal(EmbeddedRegion.Tex, Region(map, 13));
        Assert.Equal(EmbeddedRegion.LayoutOption, Region(map, 14));
        Assert.Equal(EmbeddedRegion.LayoutOption, Region(map, 15));

        // Opener and closer stay ordinary Therion (absent from the map → global highlighter).
        Assert.Null(Region(map, 1));
        Assert.Null(Region(map, 16));
    }

    [Fact]
    public void Lookup_body_is_suppressed_but_layout_outside_is_not_affected()
    {
        var lines = new[] { "lookup t", "  1 2 3", "endlookup", "survey s" };
        var map = LayoutRegionScanner.Scan(lines);
        Assert.Equal(EmbeddedRegion.None, Region(map, 2));
        Assert.Null(Region(map, 1)); // lookup opener absent
        Assert.Null(Region(map, 4)); // ordinary line after the block
    }

    [Fact]
    public void No_layout_yields_empty_map()
    {
        var lines = new[] { "survey s", "  centreline", "  endcentreline", "endsurvey" };
        Assert.Empty(LayoutRegionScanner.Scan(lines));
    }

    [Fact]
    public void Tex_atlas_target_is_tex_region()
    {
        var lines = new[] { "layout l", "  code tex-atlas", "    \\foo", "  endcode", "endlayout" };
        var map = LayoutRegionScanner.Scan(lines);
        Assert.Equal(EmbeddedRegion.Tex, Region(map, 3));
    }
}
