// Table content of Th2Symbols: the expected lists below are transcribed by hand from the Therion book
// (v6.4.0, sections 'point', 'line' and 'area', the "following types are supported" lists), not
// from the code under test. Where the code deliberately differs from the book, the difference is
// named in an explicit Extra*/BookOnly* list so a change on either side shows up here.

using System.Collections.Generic;
using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Tables;

public class Th2SymbolsTableTests
{
    // ---- thbook 'point' <type> (grouped as the book groups them) -------------------------------
    private static readonly string[] BookPointTypes =
    {
        // special objects, labels
        "dimensions", "section", "station",
        "altitude", "date", "height", "label", "passage-height", "remark", "station-name",
        // symbolic passage fills
        "bedrock", "blocks", "clay", "debris", "guano", "ice", "mudcrack", "mud", "pebbles", "raft",
        "sand", "snow", "water",
        // speleothems
        "anastomosis", "aragonite", "cave-pearl", "clay-tree", "crystal", "curtains", "curtain",
        "disc-pillar", "disc-stalactite", "disc-stalagmite", "disc-pillars", "disc-stalactites",
        "disc-stalagmites", "disk", "flowstone", "flute", "gypsum-flower", "gypsum", "helictites",
        "helictite", "karren", "moonmilk", "pendant", "pillar-with-curtains", "pillars-with-curtains",
        "pillar", "popcorn", "raft-cone", "rimstone-dam", "rimstone-pool", "scallop", "soda-straw",
        "stalactite-stalagmite", "stalactites-stalagmites", "stalactite", "stalactites", "stalagmite",
        "stalagmites", "volcano", "wall-calcite",
        // equipment
        "anchor", "bridge", "camp", "fixed-ladder", "gate", "handrail", "masonry", "nameplate",
        "no-equipment", "no-wheelchair", "rope-ladder", "rope", "steps", "traverse", "via-ferrata",
        "walkway", "wheelchair",
        // passage ends
        "breakdown-choke", "clay-choke", "continuation", "entrance", "flowstone-choke", "low-end",
        "narrow-end",
        // others
        "air-draught", "altar", "archeo-excavation", "archeo-material", "audio", "bat", "bones",
        "borehole", "danger", "dig", "electric-light", "ex-voto", "extra", "gradient", "human-bones",
        "ice-pillar", "ice-stalactite", "ice-stalagmite", "map-connection", "paleo-material", "photo",
        "root", "seed-germination", "sink", "spring", "tree-trunk", "vegetable-debris", "water-drip",
        "water-flow",
    };

    // In the code but not in the book's point list.
    private static readonly string[] ExtraPointTypes = { "pillars" };

    // ---- thbook 'line' <type> -------------------------------------------------------------------
    private static readonly string[] BookLineTypes =
    {
        // passages
        "wall", "contour", "slope", "floor-step", "pit", "pitch", "ceiling-step", "chimney", "overhang",
        "ceiling-meander", "floor-meander", "low-ceiling", "pit-chimney",
        // passage fills
        "flowstone", "moonmilk", "rock-border", "rock-edge", "water-flow", "abyss-entrance", "dripline",
        "fault", "gradient", "joint", "rimstone-dam", "rimstone-pool",
        // equipment
        "fixed-ladder", "handrail", "rope", "rope-ladder", "steps", "via-ferrata", "walkway",
        // labels, special
        "label", "border", "arrow", "section", "survey", "map-connection",
    };

    // Kept on purpose "for corpus tolerance" (see the comment on LineTypes).
    private static readonly string[] ExtraLineTypes = { "water", "wall-calcite" };

    // ---- thbook 'area' <type> -------------------------------------------------------------------
    private static readonly string[] BookAreaTypes =
    {
        "water", "sump", "sand", "debris", "blocks", "flowstone", "moonmilk", "snow", "ice", "clay",
        "pebbles", "bedrock", "mudcrack", "pillar", "pillar-with-curtains", "pillars",
        "pillars-with-curtains", "stalactite", "stalactite-stalagmite", "stalagmite",
    };

    // The area table in the code follows the compiler's own table (tharea.h, per the comment on
    // AreaTypes), not the book: it has no "pillars"/"pillars-with-curtains" and does have "dimensions".
    private static readonly string[] BookOnlyAreaTypes = { "pillars", "pillars-with-curtains" };
    private static readonly string[] ExtraAreaTypes = { "dimensions" };

    // ---- thbook options of point/line/area (and line points) ---------------------------------------
    private static readonly string[] BookObjectOptions =
    {
        // point
        "id", "subtype", "orientation", "orient", "align", "scale", "place", "clip", "visibility",
        "context", "dist", "name", "scrap", "explored", "text", "value",
        // line / line data
        "close", "mark", "outline", "reverse", "smooth", "adjust", "altitude", "anchors", "border",
        "direction", "gradient", "head", "l-size", "rebelays", "size", "height",
    };

    // Accepted by the code beyond the book's option lists (alternate spellings and positions kept
    // for the corpus).
    private static readonly string[] ExtraObjectOptions = { "visible", "from", "attr", "r-size", "station", "extend" };

    private static string[] Union(string[] book, string[] extra, string[] bookOnly) =>
        book.Except(bookOnly).Concat(extra).ToArray();

    private static readonly string[] AllPoint = Union(BookPointTypes, ExtraPointTypes, []);
    private static readonly string[] AllLine = Union(BookLineTypes, ExtraLineTypes, []);
    private static readonly string[] AllArea = Union(BookAreaTypes, ExtraAreaTypes, BookOnlyAreaTypes);

    public static IEnumerable<object[]> Names(string[] names) => names.Select(n => new object[] { n });
    public static IEnumerable<object[]> PointNames() => Names(AllPoint);
    public static IEnumerable<object[]> LineNames() => Names(AllLine);
    public static IEnumerable<object[]> AreaNames() => Names(AllArea);
    public static IEnumerable<object[]> OptionNames() => Names(BookObjectOptions.Concat(ExtraObjectOptions).ToArray());

    [Fact]
    public void Expected_lists_have_no_duplicates()
    {
        // Guards the hand-written lists themselves: a repeated entry would hide a count mismatch.
        Assert.Equal(AllPoint.Length, AllPoint.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AllLine.Length, AllLine.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AllArea.Length, AllArea.Distinct(StringComparer.Ordinal).Count());
        var options = BookObjectOptions.Concat(ExtraObjectOptions).ToArray();
        Assert.Equal(options.Length, options.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Point_table_is_exactly_the_book_list_plus_the_documented_extras() =>
        Assert.Equal(AllPoint.OrderBy(x => x, StringComparer.Ordinal), Th2Symbols.PointTypes.OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Line_table_is_exactly_the_book_list_plus_the_documented_extras() =>
        Assert.Equal(AllLine.OrderBy(x => x, StringComparer.Ordinal), Th2Symbols.LineTypes.OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Area_table_is_the_compiler_table_not_the_book_list() =>
        Assert.Equal(AllArea.OrderBy(x => x, StringComparer.Ordinal), Th2Symbols.AreaTypes.OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Object_option_table_is_exactly_the_book_list_plus_the_documented_extras() =>
        Assert.Equal(BookObjectOptions.Concat(ExtraObjectOptions).OrderBy(x => x, StringComparer.Ordinal),
            Th2Symbols.ObjectOptions.OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Table_sizes_are_the_expected_counts()
    {
        Assert.Equal(AllPoint.Length, Th2Symbols.PointTypes.Count);
        Assert.Equal(AllLine.Length, Th2Symbols.LineTypes.Count);
        Assert.Equal(AllArea.Length, Th2Symbols.AreaTypes.Count);
        Assert.Equal(38, Th2Symbols.ObjectOptions.Count);
        Assert.Equal(Th2Symbols.PointTypes.Count, Th2Symbols.PointTypeNames.Count);
        Assert.Equal(Th2Symbols.LineTypes.Count, Th2Symbols.LineTypeNames.Count);
        Assert.Equal(Th2Symbols.AreaTypes.Count, Th2Symbols.AreaTypeNames.Count);
    }

    [Fact]
    public void Name_lists_for_completion_enumerate_the_tables()
    {
        Assert.True(Th2Symbols.PointTypes.SetEquals(Th2Symbols.PointTypeNames));
        Assert.True(Th2Symbols.LineTypes.SetEquals(Th2Symbols.LineTypeNames));
        Assert.True(Th2Symbols.AreaTypes.SetEquals(Th2Symbols.AreaTypeNames));
    }

    [Theory]
    [MemberData(nameof(PointNames))]
    public void Every_point_type_is_known_alone_with_a_subtype_and_in_any_case(string name)
    {
        Assert.True(Th2Symbols.IsKnownPointType(name));
        Assert.True(Th2Symbols.IsKnownPointType(name + ":whatever"));
        Assert.True(Th2Symbols.IsKnownPointType(name.ToUpperInvariant()));
    }

    [Theory]
    [MemberData(nameof(LineNames))]
    public void Every_line_type_is_known_alone_with_a_subtype_and_in_any_case(string name)
    {
        Assert.True(Th2Symbols.IsKnownLineType(name));
        Assert.True(Th2Symbols.IsKnownLineType(name + ":whatever"));
        Assert.True(Th2Symbols.IsKnownLineType(name.ToUpperInvariant()));
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public void Every_area_type_is_known_alone_with_a_subtype_and_in_any_case(string name)
    {
        Assert.True(Th2Symbols.IsKnownAreaType(name));
        Assert.True(Th2Symbols.IsKnownAreaType(name + ":whatever"));
        Assert.True(Th2Symbols.IsKnownAreaType(name.ToUpperInvariant()));
    }

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Every_object_option_is_known_in_any_case(string name)
    {
        Assert.True(Th2Symbols.IsKnownOption(name));
        Assert.True(Th2Symbols.IsKnownOption(name.ToUpperInvariant()));
    }

    [Fact]
    public void A_name_known_for_one_object_kind_is_not_thereby_known_for_another()
    {
        // Every name in any table must be known for exactly the kinds whose table lists it.
        var all = AllPoint.Concat(AllLine).Concat(AllArea).Distinct().ToArray();
        foreach (var name in all)
        {
            Assert.Equal(AllPoint.Contains(name), Th2Symbols.IsKnownPointType(name));
            Assert.Equal(AllLine.Contains(name), Th2Symbols.IsKnownLineType(name));
            Assert.Equal(AllArea.Contains(name), Th2Symbols.IsKnownAreaType(name));
        }
        // Spot checks that make the loop above meaningful.
        Assert.False(Th2Symbols.IsKnownPointType("sump"));
        Assert.False(Th2Symbols.IsKnownLineType("sump"));
        Assert.True(Th2Symbols.IsKnownAreaType("sump"));
        Assert.True(Th2Symbols.IsKnownLineType("pit"));
        Assert.False(Th2Symbols.IsKnownPointType("pit"));
        Assert.False(Th2Symbols.IsKnownAreaType("pit"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("wal")]
    [InlineData("walls")]
    [InlineData("wall-")]
    [InlineData(" wall")]
    [InlineData("wall ")]
    [InlineData("-wall")]
    [InlineData("explorer")]
    [InlineData("not-a-symbol")]
    [InlineData(":blocks")]
    [InlineData("uu")]
    [InlineData("user")]
    public void Unknown_names_are_not_known_for_any_kind(string name)
    {
        Assert.False(Th2Symbols.IsKnownPointType(name));
        Assert.False(Th2Symbols.IsKnownLineType(name));
        Assert.False(Th2Symbols.IsKnownAreaType(name));
        Assert.False(Th2Symbols.IsKnownOption(name));
    }

    [Theory]
    [InlineData("u")]
    [InlineData("U")]
    [InlineData("u:foo")]
    [InlineData("u:")]
    public void The_user_type_is_known_for_every_kind(string name)
    {
        Assert.True(Th2Symbols.IsKnownPointType(name));
        Assert.True(Th2Symbols.IsKnownLineType(name));
        Assert.True(Th2Symbols.IsKnownAreaType(name));
    }

    [Fact]
    public void An_unknown_base_with_a_known_subtype_is_unknown()
    {
        Assert.False(Th2Symbols.IsKnownLineType("blocks:wall"));
        Assert.False(Th2Symbols.IsKnownPointType("nonsense:blocks"));
        Assert.False(Th2Symbols.IsKnownAreaType("x:u"));
    }

    [Fact]
    public void A_dash_is_not_stripped_from_a_type_but_is_from_an_option_by_the_caller()
    {
        // IsKnownOption takes the name with the dash already removed.
        Assert.False(Th2Symbols.IsKnownOption("-id"));
        Assert.True(Th2Symbols.IsKnownOption("id"));
    }

    [Theory]
    [InlineData("wall", "wall", null)]
    [InlineData("wall:blocks", "wall", "blocks")]
    [InlineData("a:b:c", "a", "b:c")]
    [InlineData("wall:", "wall", "")]
    [InlineData(":x", "", "x")]
    [InlineData("", "", null)]
    public void SplitType_cuts_at_the_first_colon(string type, string baseType, string? subtype)
    {
        var (b, s) = Th2Symbols.SplitType(type);
        Assert.Equal(baseType, b);
        Assert.Equal(subtype, s);
    }

    [Theory]
    [InlineData("u", true)]
    [InlineData("U", true)]
    [InlineData("u:x", false)]   // the caller passes the base, not the whole type
    [InlineData("", false)]
    [InlineData("wall", false)]
    public void IsUserType_matches_only_the_bare_sentinel(string baseType, bool expected) =>
        Assert.Equal(expected, Th2Symbols.IsUserType(baseType));

    [Fact]
    public void UserType_sentinel_is_u() => Assert.Equal("u", Th2Symbols.UserType);
}
