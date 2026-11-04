// Data styles x readings. The thbook 'centreline' section lists the styles and the reading keywords
// (with their aliases) but gives no per-style table; the matrix below restates the one the code
// cites (thdata.cxx set_data_data) as an explicit test-side table, so a changed cell is a visible
// diff. On top of it: hand-written whole-order cases (duplicates, station vs from/to, newline
// position, completeness, the 21-reading limit, ignoreall), a seeded differential run of random
// non-interleaved orders against a small reference, and the value ranges of CheckValue
// (compass 0-360, clino +-90 with the 270-360 wrap for fore readings, grad/mil scaling).

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Tests.Properties;
using Kind = Therion.Syntax.DataStyles.OrderProblemKind;

namespace Therion.Syntax.Tests.Tables;

public class DataStylesMatrixTests
{
    private static readonly DataStyle[] Styles =
    {
        DataStyle.Normal, DataStyle.Topofil, DataStyle.Diving, DataStyle.Cylpolar,
        DataStyle.Cartesian, DataStyle.Dimensions, DataStyle.Nosurvey,
    };

    // ---- names and aliases ---------------------------------------------------------------------
    [Theory]
    [InlineData("normal", DataStyle.Normal)]
    [InlineData("topofil", DataStyle.Topofil)]
    [InlineData("diving", DataStyle.Diving)]
    [InlineData("cartesian", DataStyle.Cartesian)]
    [InlineData("cylpolar", DataStyle.Cylpolar)]
    [InlineData("dimensions", DataStyle.Dimensions)]
    [InlineData("nosurvey", DataStyle.Nosurvey)]
    [InlineData("NORMAL", DataStyle.Normal)]
    [InlineData("Diving", DataStyle.Diving)]
    [InlineData("passage", DataStyle.Unknown)]   // Survex's name for dimensions
    [InlineData("norma", DataStyle.Unknown)]
    [InlineData("normals", DataStyle.Unknown)]
    [InlineData("", DataStyle.Unknown)]
    [InlineData("unknown", DataStyle.Unknown)]
    public void ParseStyle_names(string name, DataStyle expected) =>
        Assert.Equal(expected, DataStyles.ParseStyle(name));

    [Fact]
    public void ParseStyle_of_null_is_unknown() => Assert.Equal(DataStyle.Unknown, DataStyles.ParseStyle(null!));

    [Fact]
    public void StyleNames_are_the_seven_book_styles() =>
        Assert.Equal(new[] { "cartesian", "cylpolar", "diving", "dimensions", "normal", "nosurvey", "topofil" }.OrderBy(x => x),
            DataStyles.StyleNames.OrderBy(x => x));

    // thbook: station, from, to, [back]tape/[back]length, [back]compass/[back]bearing,
    // [back]clino/[back]gradient, depth, fromdepth, todepth, depthchange, counter, fromcount, tocount,
    // northing, easting, altitude, up/ceiling, down/floor, left, right, ignore, ignoreall, newline, direction.
    private static readonly string[] BookReadings =
    {
        "station", "from", "to", "tape", "length", "backtape", "backlength", "compass", "bearing",
        "backcompass", "backbearing", "clino", "gradient", "backclino", "backgradient", "depth",
        "fromdepth", "todepth", "depthchange", "counter", "fromcount", "tocount", "northing", "easting",
        "altitude", "up", "ceiling", "down", "floor", "left", "right", "ignore", "ignoreall", "newline",
        "direction",
    };
    // Accepted besides: the other spellings of the count readings and the coordinate aliases of the
    // roles table (count, dx/dy/dz, gps) and the sd quantities x/y/z/position.
    private static readonly string[] ExtraReadings =
        { "count", "fromcounter", "tocounter", "dx", "dy", "dz", "x", "y", "z", "position", "gps" };

    [Fact]
    public void ReadingKeywords_are_the_book_list_plus_the_named_extras()
    {
        var expected = BookReadings.Concat(ExtraReadings).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Length, expected.Distinct().Count());
        Assert.Equal(expected, DataStyles.ReadingKeywords.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void IsKnownReading_follows_the_table()
    {
        foreach (var r in BookReadings.Concat(ExtraReadings))
        {
            Assert.True(DataStyles.IsKnownReading(r), r);
            Assert.True(DataStyles.IsKnownReading(r.ToUpperInvariant()), r);
        }
        foreach (var r in new[] { "", "wibble", "lengths", "fromdepths", "back", "new line", "explorer" })
            Assert.False(DataStyles.IsKnownReading(r), r);
    }

    public static IEnumerable<object[]> CanonicalRows() => new[]
    {
        new[] { "tape", "length" }, new[] { "backtape", "backlength" }, new[] { "compass", "bearing" },
        new[] { "backcompass", "backbearing" }, new[] { "clino", "gradient" }, new[] { "backclino", "backgradient" },
        new[] { "count", "count" }, new[] { "counter", "count" }, new[] { "fromcounter", "fromcount" },
        new[] { "tocounter", "tocount" }, new[] { "ceiling", "up" }, new[] { "floor", "down" },
        new[] { "dx", "easting" }, new[] { "dy", "northing" }, new[] { "dz", "altitude" }, new[] { "gps", "position" },
        // not aliases: unchanged (and lower-cased)
        new[] { "length", "length" }, new[] { "bearing", "bearing" }, new[] { "gradient", "gradient" },
        new[] { "backlength", "backlength" }, new[] { "fromcount", "fromcount" }, new[] { "tocount", "tocount" },
        new[] { "up", "up" }, new[] { "down", "down" }, new[] { "easting", "easting" }, new[] { "northing", "northing" },
        new[] { "altitude", "altitude" }, new[] { "position", "position" }, new[] { "station", "station" },
        new[] { "x", "x" }, new[] { "wibble", "wibble" }, new[] { "FROM", "from" }, new[] { "TAPE", "length" },
        new[] { "Counter", "count" }, new[] { "", "" },
    }.Select(r => new object[] { r[0], r[1] });

    [Theory]
    [MemberData(nameof(CanonicalRows))]
    public void CanonicalReading_maps_aliases_and_lower_cases(string reading, string expected) =>
        Assert.Equal(expected, DataStyles.CanonicalReading(reading));

    // ---- column counting and from/to lookup ----------------------------------------------------
    private static ImmutableArray<string> O(string order) =>
        order.Length == 0 ? ImmutableArray<string>.Empty : order.Split(' ').ToImmutableArray();

    [Theory]
    [InlineData("", 0)]
    [InlineData("from to length compass clino", 5)]
    [InlineData("from to length compass clino ignore", 6)]
    [InlineData("from to ignore ignore", 4)]
    [InlineData("station newline length", -1)]
    [InlineData("NEWLINE", -1)]
    [InlineData("from to length ignoreall", -1)]
    [InlineData("IGNOREALL", -1)]
    [InlineData("ignoreall from", -1)]
    [InlineData("from newline to", -1)]
    [InlineData("from to wibble", 3)]
    public void ExpectedColumnCount_counts_value_columns(string order, int expected) =>
        Assert.Equal(expected, DataStyles.ExpectedColumnCount(O(order)));

    [Theory]
    [InlineData("from to length", 0, 1)]
    [InlineData("length from to", 1, 2)]
    [InlineData("to from", 1, 0)]
    [InlineData("FROM TO", 0, 1)]
    [InlineData("from from to", 0, 2)]
    [InlineData("to to from", 2, 0)]
    [InlineData("station length", -1, -1)]
    [InlineData("from", 0, -1)]
    [InlineData("to", -1, 0)]
    [InlineData("", -1, -1)]
    public void FindFromTo_gives_the_first_index_of_each(string order, int from, int to) =>
        Assert.Equal((from, to), DataStyles.FindFromTo(O(order)));

    // ---- the style x reading validity matrix ---------------------------------------------------
    private static readonly string[] Lrud = { "up", "down", "left", "right" };

    // canonical readings valid per style (newline/ignore/ignoreall are handled before the matrix)
    private static HashSet<string> ValidFor(DataStyle style)
    {
        var set = new HashSet<string> { "station" };
        set.UnionWith(Lrud);
        string[] fromTo = { "from", "to", "direction" };
        string[] lengthBearing = { "length", "backlength", "count", "fromcount", "tocount", "bearing", "backbearing" };
        string[] depth = { "depth", "fromdepth", "todepth", "depthchange" };
        switch (style)
        {
            case DataStyle.Normal:
            case DataStyle.Topofil:
                set.UnionWith(fromTo); set.UnionWith(lengthBearing); set.UnionWith(new[] { "gradient", "backgradient" });
                break;
            case DataStyle.Diving:
            case DataStyle.Cylpolar:
                set.UnionWith(fromTo); set.UnionWith(lengthBearing); set.UnionWith(depth);
                break;
            case DataStyle.Cartesian:
                set.UnionWith(fromTo); set.UnionWith(new[] { "easting", "northing", "altitude" });
                break;
            case DataStyle.Nosurvey:
                set.UnionWith(fromTo);
                break;
            case DataStyle.Dimensions:
                break;
        }
        return set;
    }

    // Readings that take part in the matrix: every canonical name the table knows, minus the
    // markers decided earlier (ignore, ignoreall, newline) and the quantities that are never readings.
    private static readonly string[] MatrixReadings =
        BookReadings.Concat(ExtraReadings)
            .Select(DataStyles.CanonicalReading).Distinct()
            .Except(new[] { "ignore", "ignoreall", "newline" }).ToArray();

    public static IEnumerable<object[]> StyleByReading() =>
        Styles.SelectMany(s => MatrixReadings.Select(r => new object[] { s, r }));

    [Theory]
    [MemberData(nameof(StyleByReading))]
    public void A_single_reading_is_invalid_for_a_style_exactly_when_the_matrix_says_so(DataStyle style, string reading)
    {
        bool valid = ValidFor(style).Contains(reading);   // x/y/z/position are never valid
        var problems = DataStyles.ValidateOrder(style, ImmutableArray.Create(reading));
        bool flagged = problems.Any(p => p.Kind == Kind.InvalidForStyle);
        Assert.True(valid != flagged, $"{style} {reading}: matrix says {(valid ? "valid" : "invalid")}, got {Describe(problems)}");
        if (flagged)
        {
            var p = problems.Single(p => p.Kind == Kind.InvalidForStyle);
            Assert.Equal(0, p.Index);
            Assert.Contains(reading, p.Message);
            Assert.Contains(style == DataStyle.Topofil ? "normal" : style.ToString().ToLowerInvariant(), p.Message);
        }
    }

    [Fact]
    public void Aliases_are_valid_exactly_where_their_canonical_reading_is()
    {
        foreach (var style in Styles)
            foreach (var (alias, canon) in new[] { ("tape", "length"), ("backtape", "backlength"), ("compass", "bearing"),
                         ("backcompass", "backbearing"), ("clino", "gradient"), ("backclino", "backgradient"),
                         ("counter", "count"), ("fromcounter", "fromcount"), ("tocounter", "tocount"), ("ceiling", "up"),
                         ("floor", "down"), ("dx", "easting"), ("dy", "northing"), ("dz", "altitude"), ("gps", "position") })
            {
                var a = DataStyles.ValidateOrder(style, ImmutableArray.Create(alias)).Any(p => p.Kind == Kind.InvalidForStyle);
                var c = DataStyles.ValidateOrder(style, ImmutableArray.Create(canon)).Any(p => p.Kind == Kind.InvalidForStyle);
                Assert.True(a == c, $"{style}: {alias} -> {a}, {canon} -> {c}");
            }
    }

    [Fact]
    public void An_unknown_style_validates_nothing_and_unknown_readings_are_skipped()
    {
        Assert.Empty(DataStyles.ValidateOrder(DataStyle.Unknown, O("from from tape x newline wibble")));
        Assert.Empty(DataStyles.ValidateOrder(DataStyle.Nosurvey, O("from to wibble explorer")));
        Assert.Empty(DataStyles.ValidateOrder(DataStyle.Normal, O("from to length compass clino wibble")));
    }

    // ---- whole orders ---------------------------------------------------------------------------
    private static string Describe(IEnumerable<DataStyles.OrderProblem> problems) =>
        "[" + string.Join(", ", problems.Select(p => $"{p.Kind}@{p.Index}")) + "]";

    public static IEnumerable<object[]> OrderRows() => new[]
    {
        // normal, complete and with aliases / back readings / counters
        Row(DataStyle.Normal, "from to length compass clino", ""),
        Row(DataStyle.Normal, "from to tape compass clino", ""),
        Row(DataStyle.Normal, "FROM TO LENGTH COMPASS CLINO", ""),
        Row(DataStyle.Normal, "from to length compass clino backcompass backclino", ""),
        Row(DataStyle.Normal, "from to length backcompass backclino", ""),
        Row(DataStyle.Normal, "from to counter compass clino", ""),
        Row(DataStyle.Normal, "from to fromcount tocount compass clino", ""),
        Row(DataStyle.Topofil, "from to counter compass clino", ""),
        Row(DataStyle.Normal, "from to length compass clino ignore ignore", ""),
        Row(DataStyle.Normal, "from to length compass clino direction", ""),
        // incomplete
        Row(DataStyle.Normal, "from to length compass", "Incomplete@-1"),
        Row(DataStyle.Normal, "from to compass clino", "Incomplete@-1"),
        Row(DataStyle.Normal, "from to length clino", "Incomplete@-1"),
        Row(DataStyle.Normal, "from to fromcount compass clino", "Incomplete@-1"),
        Row(DataStyle.Normal, "from to tocount compass clino", "Incomplete@-1"),
        Row(DataStyle.Normal, "", "Incomplete@-1"),
        // duplicates and alternatives
        Row(DataStyle.Normal, "from to length compass clino length", "Duplicate@5"),
        Row(DataStyle.Normal, "from to length tape compass clino", "Duplicate@3"),        // tape is length
        Row(DataStyle.Normal, "from from to length compass clino", "Duplicate@1"),
        Row(DataStyle.Normal, "from to length compass clino ignore ignore", ""),          // ignore may repeat
        Row(DataStyle.Normal, "from to length fromcount tocount compass clino", "Duplicate@3,Duplicate@4"),
        Row(DataStyle.Normal, "from to fromcount tocount length compass clino", "Duplicate@4"),
        Row(DataStyle.Normal, "from to count length compass clino", ""),                  // count and length coexist
        // station vs from/to
        Row(DataStyle.Normal, "station from newline length compass clino", "InterleavedMix@1"),
        Row(DataStyle.Normal, "from station newline length compass clino", "InterleavedMix@1"),
        Row(DataStyle.Normal, "to station newline length compass clino", "InterleavedMix@1"),
        // interleaved data: per-shot readings after newline
        Row(DataStyle.Normal, "station newline length compass clino", ""),
        Row(DataStyle.Normal, "station length compass clino", "InterleavedMix@1,InterleavedMix@2,InterleavedMix@3"),
        Row(DataStyle.Normal, "station newline length station compass clino", "Duplicate@3"),
        Row(DataStyle.Normal, "station newline count compass clino", "InterleavedMix@2"),
        Row(DataStyle.Diving, "station depth newline length compass", ""),
        Row(DataStyle.Diving, "station depth length compass", "InterleavedMix@2,InterleavedMix@3"),
        Row(DataStyle.Diving, "from to length compass depth", "InterleavedMix@2,InterleavedMix@3"),
        // newline position
        Row(DataStyle.Normal, "newline from to length compass clino", "NewlinePosition@0"),
        Row(DataStyle.Normal, "from to length compass clino newline", "NewlinePosition@5"),
        Row(DataStyle.Normal, "from to newline length compass clino", ""),
        Row(DataStyle.Normal, "from to newline newline length compass clino", "Duplicate@3"),
        // diving / cylpolar completeness and alternatives
        Row(DataStyle.Diving, "station depth newline length compass", ""),
        Row(DataStyle.Diving, "station fromdepth todepth newline length compass", "InterleavedMix@1,InterleavedMix@2"),   // per-shot readings belong after newline
        Row(DataStyle.Diving, "station newline fromdepth todepth length compass", ""),
        Row(DataStyle.Diving, "station depthchange newline length compass", "InterleavedMix@1"),
        Row(DataStyle.Diving, "station newline length compass", "Incomplete@-1"),
        Row(DataStyle.Diving, "station fromdepth newline length compass", "Incomplete@-1,InterleavedMix@1"),
        Row(DataStyle.Diving, "station depth fromdepth todepth newline length compass", "Duplicate@2,Duplicate@3,InterleavedMix@2,InterleavedMix@3"),
        Row(DataStyle.Diving, "station newline length compass gradient depth", "InvalidForStyle@4,InterleavedMix@5"),
        Row(DataStyle.Cylpolar, "station depth newline length compass", ""),
        Row(DataStyle.Cylpolar, "station newline length compass", "Incomplete@-1"),
        // cartesian
        Row(DataStyle.Cartesian, "from to easting northing altitude", ""),
        Row(DataStyle.Cartesian, "from to dx dy dz", ""),
        Row(DataStyle.Cartesian, "from to easting", ""),
        Row(DataStyle.Cartesian, "from to", "Incomplete@-1"),
        Row(DataStyle.Cartesian, "from to length", "InvalidForStyle@2,Incomplete@-1"),
        Row(DataStyle.Cartesian, "from to easting dx", "Duplicate@3"),
        // dimensions
        Row(DataStyle.Dimensions, "station left right up down", ""),
        Row(DataStyle.Dimensions, "station ceiling floor", ""),
        Row(DataStyle.Dimensions, "station", "Incomplete@-1"),
        Row(DataStyle.Dimensions, "from to left", "InvalidForStyle@0,InvalidForStyle@1"),
        // nosurvey never lacks anything
        Row(DataStyle.Nosurvey, "from to", ""),
        Row(DataStyle.Nosurvey, "station", ""),
        Row(DataStyle.Nosurvey, "", ""),
        Row(DataStyle.Nosurvey, "from to length", "InvalidForStyle@2"),
        // ignoreall ends the order
        Row(DataStyle.Normal, "from to length compass clino ignoreall wibble length station", ""),
        Row(DataStyle.Normal, "from to length compass clino ignoreall", ""),
        Row(DataStyle.Normal, "ignoreall from to length", "Incomplete@-1"),
        Row(DataStyle.Normal, "from to ignoreall length compass clino", "Incomplete@-1"),
    };

    private static object[] Row(DataStyle style, string order, string expected) => new object[] { style, order, expected };

    [Theory]
    [MemberData(nameof(OrderRows))]
    public void Whole_orders_give_the_expected_problems_in_order(DataStyle style, string order, string expected)
    {
        var got = string.Join(",", DataStyles.ValidateOrder(style, O(order)).Select(p => $"{p.Kind}@{p.Index}"));
        Assert.Equal(expected, got);
    }

    [Fact]
    public void More_than_21_readings_is_too_many_and_21_is_not()
    {
        string Order(int n) => "from to" + string.Concat(Enumerable.Repeat(" ignore", n - 2));
        Assert.Empty(DataStyles.ValidateOrder(DataStyle.Nosurvey, O(Order(21))));
        var p = Assert.Single(DataStyles.ValidateOrder(DataStyle.Nosurvey, O(Order(22))));
        Assert.Equal(Kind.TooManyReadings, p.Kind);
        Assert.Equal(-1, p.Index);
        Assert.Contains("22", p.Message);
        Assert.Contains("21", p.Message);
    }

    [Fact]
    public void Problem_messages_name_the_offending_reading()
    {
        foreach (var (style, order, kind, word) in new[]
        {
            (DataStyle.Normal, "from to length compass clino length", Kind.Duplicate, "length"),
            (DataStyle.Normal, "from to LENGTH compass clino length", Kind.Duplicate, "length"),
            (DataStyle.Cartesian, "from to Length", Kind.InvalidForStyle, "Length"),
            (DataStyle.Normal, "station from", Kind.InterleavedMix, "station"),
            (DataStyle.Normal, "newline from to", Kind.NewlinePosition, "newline"),
            (DataStyle.Diving, "station newline length compass", Kind.Incomplete, "diving"),
        })
        {
            var p = DataStyles.ValidateOrder(style, O(order)).First(x => x.Kind == kind);
            Assert.Contains(word, p.Message);
        }
    }

    // ---- seeded differential run: random non-interleaved orders vs a small reference -----------
    // Alphabet without station/newline/depth, so the only rules in play are the style matrix, the
    // duplicate rule (no repeats are drawn) and completeness.
    private static readonly (string Canon, string[] Spellings)[] Alphabet =
    {
        ("from", new[] { "from" }), ("to", new[] { "to" }), ("direction", new[] { "direction" }),
        ("length", new[] { "length", "tape" }), ("backlength", new[] { "backlength", "backtape" }),
        ("count", new[] { "count", "counter" }), ("bearing", new[] { "bearing", "compass" }),
        ("backbearing", new[] { "backbearing", "backcompass" }), ("gradient", new[] { "gradient", "clino" }),
        ("backgradient", new[] { "backgradient", "backclino" }), ("easting", new[] { "easting", "dx" }),
        ("northing", new[] { "northing", "dy" }), ("altitude", new[] { "altitude", "dz" }),
        ("up", new[] { "up", "ceiling" }), ("down", new[] { "down", "floor" }), ("left", new[] { "left" }),
        ("right", new[] { "right" }), ("ignore", new[] { "ignore" }), ("x", new[] { "x" }), ("position", new[] { "position", "gps" }),
    };

    private static bool ReferenceIncomplete(DataStyle style, HashSet<string> have)
    {
        bool lengthLike = have.Contains("count") || have.Contains("length") || have.Contains("backlength");
        bool bearingLike = have.Contains("bearing") || have.Contains("backbearing");
        return style switch
        {
            DataStyle.Normal or DataStyle.Topofil =>
                !(lengthLike && bearingLike && (have.Contains("gradient") || have.Contains("backgradient"))),
            DataStyle.Cartesian => !(have.Contains("easting") || have.Contains("northing") || have.Contains("altitude")),
            DataStyle.Dimensions => !Lrud.Any(have.Contains),
            _ => false,   // diving/cylpolar need depth readings the alphabet lacks: handled below
        };
    }

    [Fact]
    public void Random_non_interleaved_orders_agree_with_the_reference()
    {
        const int baseSeed = 64_000;
        int runs = PropertyRuns.Count(1500);
        for (int i = 0; i < runs; i++)
        {
            int seed = baseSeed + i;
            var rnd = new SeededRandom(seed);
            var style = rnd.Pick(Styles);
            var picked = Alphabet.Where(_ => rnd.Chance(0.45)).OrderBy(_ => rnd.Next(1000)).ToList();
            var order = picked.Select(a => rnd.Pick(a.Spellings)).ToImmutableArray();
            var canons = picked.Select(a => a.Canon).ToList();

            var got = DataStyles.ValidateOrder(style, order);
            string ctx = $"seed {baseSeed + i}, {style}, order [{string.Join(" ", order)}], got {Describe(got)}";

            var valid = ValidFor(style);
            var expectedInvalid = Enumerable.Range(0, order.Length)
                .Where(k => canons[k] != "ignore" && !valid.Contains(canons[k])).ToList();
            Assert.True(expectedInvalid.SequenceEqual(got.Where(p => p.Kind == Kind.InvalidForStyle).Select(p => p.Index)), ctx);

            var have = canons.ToHashSet();
            bool incomplete = style is DataStyle.Diving or DataStyle.Cylpolar || ReferenceIncomplete(style, have);
            Assert.True(incomplete == got.Any(p => p.Kind == Kind.Incomplete), ctx);

            // nothing else can arise from this alphabet
            Assert.True(got.All(p => p.Kind is Kind.InvalidForStyle or Kind.Incomplete), ctx);
            Assert.True(got.All(p => p.Index >= -1 && p.Index < Math.Max(order.Length, 1)), ctx);

            // the verdict does not depend on the order of the readings
            var shuffled = order.OrderBy(_ => rnd.Next(1000)).ToImmutableArray();
            var again = DataStyles.ValidateOrder(style, shuffled);
            Assert.True(got.Count(p => p.Kind == Kind.InvalidForStyle) == again.Count(p => p.Kind == Kind.InvalidForStyle)
                        && got.Any(p => p.Kind == Kind.Incomplete) == again.Any(p => p.Kind == Kind.Incomplete), ctx + " (reordered)");
        }
    }

    [Fact]
    public void Random_orders_of_any_known_reading_never_throw_and_keep_indices_in_range()
    {
        const int baseSeed = 65_000;
        var all = DataStyles.ReadingKeywords.Append("wibble").ToList();
        int runs = PropertyRuns.Count(1500);
        for (int i = 0; i < runs; i++)
        {
            var rnd = new SeededRandom(baseSeed + i);
            var style = rnd.Pick(Styles.Append(DataStyle.Unknown).ToList());
            var order = Enumerable.Range(0, rnd.Next(0, 26)).Select(_ => rnd.Pick(all)).ToImmutableArray();
            ImmutableArray<DataStyles.OrderProblem> got = default;
            var thrown = Record.Exception(() => got = DataStyles.ValidateOrder(style, order));
            string ctx = $"seed {baseSeed + i}, {style}, order [{string.Join(" ", order)}]";
            Assert.True(thrown is null, ctx + ": " + thrown);
            Assert.True(got.All(p => p.Index >= -1 && p.Index < Math.Max(order.Length, 1) && p.Message.Length > 0), ctx);
            Assert.True(got.Any(p => p.Kind == Kind.TooManyReadings) == (style != DataStyle.Unknown && order.Length > 21), ctx);
        }
    }

    // ---- CheckValue: ranges --------------------------------------------------------------------
    // reading, value, compass unit, clino unit, expected: ok | warn | error
    public static IEnumerable<object[]> ValueRows() => new[]
    {
        // lengths and dimensions: negative is a warning
        V("length", "0", "ok"), V("length", "-0", "ok"), V("length", "12.5", "ok"), V("length", "-0.001", "warn"),
        V("backtape", "-1", "warn"), V("left", "-1", "warn"), V("ceiling", "0", "ok"), V("floor", "-0.5", "warn"),
        V("length", "-", "ok"), V("up", "-", "ok"), V("length", "1e2", "ok"), V("length", "5,5", "error"),
        V("length", "5.15x", "error"), V("right", "x", "error"),
        // signed readings: any number
        V("depth", "-3.25", "ok"), V("fromdepth", "7", "ok"), V("depthchange", "-1e-3", "ok"), V("easting", "-123456.789", "ok"),
        V("dz", "-2", "ok"), V("fromcount", "1200", "ok"), V("counter", "-5", "ok"), V("depth", "abc", "error"),
        V("altitude", "1.2.3", "error"), V("tocount", "-", "ok"),
        // compass, degrees
        V("compass", "0", "ok"), V("compass", "360", "ok"), V("compass", "359.99", "ok"), V("compass", "360.01", "warn"),
        V("compass", "-0.01", "warn"), V("bearing", "720", "warn"), V("backcompass", "361", "warn"), V("backbearing", "-", "ok"),
        V("compass", "14x1.4", "error"),
        // compass, other units
        V("compass", "400", "ok", AngleUnit.Grad), V("compass", "400.1", "warn", AngleUnit.Grad), V("compass", "-1", "warn", AngleUnit.Grad),
        V("compass", "6400", "ok", AngleUnit.Mil), V("compass", "6400.1", "warn", AngleUnit.Mil),
        V("compass", "7000", "ok", AngleUnit.Minute), V("compass", "99999", "ok", AngleUnit.PercentSlope),
        V("compass", "361", "ok", AngleUnit.Minute),
        // clino, degrees: +-90, fore readings wrap 270..360
        V("clino", "0", "ok"), V("clino", "90", "ok"), V("clino", "-90", "ok"), V("clino", "90.01", "warn"), V("clino", "-90.01", "warn"),
        V("clino", "180", "warn"), V("clino", "269.99", "warn"), V("clino", "270", "ok"), V("clino", "300", "ok"), V("clino", "360", "ok"),
        V("clino", "360.01", "warn"), V("gradient", "-270", "warn"),
        V("backclino", "270", "warn"), V("backclino", "300", "warn"), V("backgradient", "90", "ok"), V("backgradient", "-91", "warn"),
        V("clino", "up", "ok"), V("clino", "DOWN", "ok"), V("clino", "+v", "ok"), V("clino", "-v", "ok"), V("clino", "level", "ok"),
        V("clino", "h", "ok"), V("clino", "-", "ok"), V("clino", "+", "ok"), V("clino", "plumb", "ok"), V("clino", "vertical", "ok"),
        V("clino", "sideways", "error"), V("clino", "z", "error"),
        // clino, other units
        V("clino", "100", "ok", AngleUnit.Degree, AngleUnit.Grad), V("clino", "100.1", "warn", AngleUnit.Degree, AngleUnit.Grad),
        V("clino", "-100", "ok", AngleUnit.Degree, AngleUnit.Grad), V("clino", "-100.1", "warn", AngleUnit.Degree, AngleUnit.Grad),
        V("clino", "299.9", "warn", AngleUnit.Degree, AngleUnit.Grad), V("clino", "300", "ok", AngleUnit.Degree, AngleUnit.Grad),
        V("clino", "400", "ok", AngleUnit.Degree, AngleUnit.Grad), V("clino", "400.1", "warn", AngleUnit.Degree, AngleUnit.Grad),
        V("clino", "1600", "ok", AngleUnit.Degree, AngleUnit.Mil), V("clino", "1600.1", "warn", AngleUnit.Degree, AngleUnit.Mil),
        V("clino", "4800", "ok", AngleUnit.Degree, AngleUnit.Mil), V("clino", "6400", "ok", AngleUnit.Degree, AngleUnit.Mil),
        V("clino", "6400.1", "warn", AngleUnit.Degree, AngleUnit.Mil),
        V("clino", "250", "ok", AngleUnit.Degree, AngleUnit.PercentSlope), V("clino", "100000", "ok", AngleUnit.Degree, AngleUnit.PercentSlope),
        V("clino", "-100000", "ok", AngleUnit.Degree, AngleUnit.Minute),
        // never checked
        V("from", "anything", "ok"), V("to", ".", "ok"), V("station", "1@a", "ok"), V("ignore", "x", "ok"),
        V("newline", "x", "ok"), V("direction", "x", "ok"), V("wibble", "x", "ok"), V("length", "", "ok"),
    }.Select(r => r);

    private static object[] V(string reading, string value, string verdict,
        AngleUnit compass = AngleUnit.Degree, AngleUnit clino = AngleUnit.Degree) =>
        new object[] { reading, value, compass, clino, verdict };

    [Theory]
    [MemberData(nameof(ValueRows))]
    public void CheckValue_verdicts_at_the_range_edges(string reading, string value, AngleUnit compass, AngleUnit clino, string verdict)
    {
        var p = DataReadingValidation.CheckValue(reading, value, compass, clino);
        switch (verdict)
        {
            case "ok":
                Assert.True(p is null, $"{reading} {value} {compass}/{clino}: {p?.Message}");
                break;
            case "warn":
                Assert.True(p is { IsError: false }, $"{reading} {value} {compass}/{clino}: {(p is null ? "accepted" : "error " + p.Value.Message)}");
                Assert.Contains(reading, p!.Value.Message);
                Assert.Contains(value, p.Value.Message);
                break;
            default:
                Assert.True(p is { IsError: true }, $"{reading} {value}: {(p is null ? "accepted" : "warning " + p.Value.Message)}");
                Assert.Contains(reading, p!.Value.Message);
                Assert.Contains(value, p.Value.Message);
                break;
        }
    }

    [Theory]
    [InlineData("compass", "361", AngleUnit.Degree, AngleUnit.Degree, "0–360")]
    [InlineData("compass", "401", AngleUnit.Grad, AngleUnit.Degree, "0–400")]
    [InlineData("compass", "6401", AngleUnit.Mil, AngleUnit.Degree, "0–6400")]
    [InlineData("clino", "91", AngleUnit.Degree, AngleUnit.Degree, "−90…90")]
    [InlineData("clino", "101", AngleUnit.Degree, AngleUnit.Grad, "−100…100")]
    [InlineData("clino", "1601", AngleUnit.Degree, AngleUnit.Mil, "−1600…1600")]
    [InlineData("length", "-1", AngleUnit.Degree, AngleUnit.Degree, "should not be negative")]
    public void Range_warnings_state_the_range_in_the_active_unit(string reading, string value,
        AngleUnit compass, AngleUnit clino, string text)
    {
        var p = DataReadingValidation.CheckValue(reading, value, compass, clino);
        Assert.True(p is { IsError: false });
        Assert.Contains(text, p!.Value.Message);
    }

    [Fact]
    public void Random_values_agree_with_the_reference_ranges()
    {
        const int baseSeed = 66_000;
        string[] readings = { "length", "backlength", "compass", "backcompass", "clino", "backclino", "gradient", "up", "depth" };
        var units = new[] { AngleUnit.Degree, AngleUnit.Grad, AngleUnit.Mil, AngleUnit.Minute, AngleUnit.PercentSlope };
        double[] edges = { 0, 1, 90, 100, 270, 300, 360, 400, 1600, 4800, 6400 };
        int runs = PropertyRuns.Count(3000);
        for (int i = 0; i < runs; i++)
        {
            var rnd = new SeededRandom(baseSeed + i);
            var reading = rnd.Pick(readings);
            var cu = rnd.Pick(units);
            var ku = rnd.Pick(units);
            double edge = rnd.Pick(edges);
            double v = edge + rnd.Pick(new[] { -0.5, -0.01, 0, 0, 0.01, 0.5 }) * (rnd.Chance(0.5) ? 1 : 1);
            if (rnd.Chance(0.3)) v = -v;
            var text = v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            double parsed = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

            string expected = Reference(reading, parsed, cu, ku);
            var p = DataReadingValidation.CheckValue(reading, text, cu, ku);
            string got = p is null ? "ok" : p.Value.IsError ? "error" : "warn";
            Assert.True(expected == got, $"seed {baseSeed + i}: {reading} {text} compass={cu} clino={ku}: expected {expected}, got {got}");
        }
    }

    // Reference ranges: lengths/dimensions >= 0; compass 0..full circle; clino +-quarter circle, and
    // fore clino readings of 3/4..full circle wrap to negative; minutes and percent are unbounded.
    private static string Reference(string reading, double v, AngleUnit compass, AngleUnit clino)
    {
        static double Full(AngleUnit u) => u switch { AngleUnit.Degree => 360, AngleUnit.Grad => 400, AngleUnit.Mil => 6400, _ => double.PositiveInfinity };
        switch (DataReadingValidation.Classify(reading))
        {
            case ReadingValueKind.Length:
            case ReadingValueKind.Dimension:
                return v < 0 ? "warn" : "ok";
            case ReadingValueKind.Signed:
                return "ok";
            case ReadingValueKind.Bearing:
                return v < 0 || v > Full(compass) ? "warn" : "ok";
            case ReadingValueKind.Clino:
                double quarter = Full(clino) / 4;
                bool back = reading.StartsWith("back", StringComparison.Ordinal);
                if (!back && v >= 3 * quarter && v <= 4 * quarter) return "ok";
                return v < -quarter || v > quarter ? "warn" : "ok";
            default:
                return "ok";
        }
    }

    // ---- Classify: aliases classify alike --------------------------------------------------------
    [Theory]
    [InlineData("tape", "length")]
    [InlineData("backtape", "backlength")]
    [InlineData("compass", "bearing")]
    [InlineData("backcompass", "backbearing")]
    [InlineData("clino", "gradient")]
    [InlineData("backclino", "backgradient")]
    [InlineData("counter", "count")]
    [InlineData("fromcounter", "fromcount")]
    [InlineData("tocounter", "tocount")]
    [InlineData("ceiling", "up")]
    [InlineData("floor", "down")]
    [InlineData("dx", "easting")]
    [InlineData("dy", "northing")]
    [InlineData("dz", "altitude")]
    [InlineData("gps", "position")]
    public void Aliases_are_classified_like_their_canonical_reading(string alias, string canonical)
    {
        var kind = DataReadingValidation.Classify(canonical);
        Assert.NotEqual(ReadingValueKind.None, kind);
        Assert.Equal(kind, DataReadingValidation.Classify(alias));
        // ... so a bad value is caught under either spelling
        Assert.True(DataReadingValidation.CheckValue(alias, "q1") is { IsError: true }, alias);
        Assert.True(DataReadingValidation.CheckValue(canonical, "q1") is { IsError: true }, canonical);
    }

    [Theory]
    [InlineData("station", ReadingValueKind.Station)]
    [InlineData("FROM", ReadingValueKind.Station)]
    [InlineData("backlength", ReadingValueKind.Length)]
    [InlineData("backcompass", ReadingValueKind.Bearing)]
    [InlineData("backclino", ReadingValueKind.Clino)]
    [InlineData("backgradient", ReadingValueKind.Clino)]
    [InlineData("fromdepth", ReadingValueKind.Signed)]
    [InlineData("todepth", ReadingValueKind.Signed)]
    [InlineData("depthchange", ReadingValueKind.Signed)]
    [InlineData("northing", ReadingValueKind.Signed)]
    [InlineData("x", ReadingValueKind.Signed)]
    [InlineData("y", ReadingValueKind.Signed)]
    [InlineData("z", ReadingValueKind.Signed)]
    [InlineData("position", ReadingValueKind.Signed)]
    [InlineData("down", ReadingValueKind.Dimension)]
    [InlineData("right", ReadingValueKind.Dimension)]
    [InlineData("ignore", ReadingValueKind.Ignore)]
    [InlineData("ignoreall", ReadingValueKind.None)]
    [InlineData("newline", ReadingValueKind.None)]
    [InlineData("direction", ReadingValueKind.None)]
    [InlineData("", ReadingValueKind.None)]
    public void Classify_more_readings(string reading, ReadingValueKind expected) =>
        Assert.Equal(expected, DataReadingValidation.Classify(reading));

    [Fact]
    public void Classify_of_null_is_none() => Assert.Equal(ReadingValueKind.None, DataReadingValidation.Classify(null));
}
