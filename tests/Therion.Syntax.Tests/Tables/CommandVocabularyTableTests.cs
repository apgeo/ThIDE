// Table content of CommandVocabulary and MeasurementUnits against lists transcribed by hand from the
// Therion book (v6.4.0: 'centreline' options team/instrument/sd/units/mark/flags/station/extend,
// 'export' and the "units" value type). Entries the code adds or leaves out on purpose are named in
// Extra*/BookOnly* lists, each with the reason, so a change on either side shows up here.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Tables;

public class CommandVocabularyTableTests
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static string[] Sorted(IEnumerable<string> names) => names.OrderBy(x => x, Ord).ToArray();

    private static void AssertTable(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var e = Sorted(expected);
        Assert.Equal(e.Length, e.Distinct(Ord).Count()); // the expected list itself has no repeats
        Assert.Equal(e, Sorted(actual));
    }

    public static IEnumerable<object[]> Each(IEnumerable<string> names) => names.Select(n => new object[] { n });

    // ---- team roles (book table "role / alias") ------------------------------------------------
    private static readonly string[] BookRoles =
    {
        "tape", "length", "compass", "bearing", "clino", "gradient",
        "backtape", "backlength", "backcompass", "backbearing", "backclino", "backgradient",
        "instruments", "insts", "counter", "count", "depth", "station", "position", "gps",
        "notes", "notebook", "pictures", "pics", "assistant", "dog", "altitude", "dz",
        "dimensions", "left", "right", "up", "ceiling", "down", "floor",
    };
    // The book also lists "explorer", which the compiler rejects (see the comment on TeamRoles).
    private static readonly string[] RolesTheCompilerRejects = { "explorer" };

    // instrument takes the team roles except these (the code cites thdata.cxx for the difference).
    private static readonly string[] NotInstrumentQuantities = { "altitude", "dz", "dimensions" };

    // ---- sd / units quantities (book: "length, tape, bearing, compass, gradient, clino, counter,
    //      depth, x, y, z, position, easting, dx, northing, dy, altitude, dz") ---------------------
    private static readonly string[] BookSdLength =
    {
        "length", "tape", "counter", "depth", "x", "y", "z", "position", "easting", "dx", "northing",
        "dy", "altitude", "dz",
    };
    private static readonly string[] BookSdAngle = { "bearing", "compass", "gradient", "clino" };
    // The aliases the team table gives (count, gps), accepted by sd too.
    private static readonly string[] ExtraSdLength = { "count", "gps" };
    // units also takes the passage-dimension quantities and depthchange (comment on the table).
    private static readonly string[] ExtraUnitsLength =
        { "depthchange", "dimensions", "up", "ceiling", "down", "floor", "left", "right" };

    [Fact]
    public void TeamRoles_are_the_book_roles_without_explorer() =>
        AssertTable(BookRoles, CommandVocabulary.TeamRoles);

    [Fact]
    public void TeamRoles_reject_what_the_compiler_rejects()
    {
        foreach (var r in RolesTheCompilerRejects)
            Assert.DoesNotContain(r, CommandVocabulary.TeamRoles);
    }

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void Every_book_role_is_a_team_role_in_any_case(string role)
    {
        Assert.Contains(role, CommandVocabulary.TeamRoles);
        Assert.Contains(role.ToUpperInvariant(), CommandVocabulary.TeamRoles);
    }
    public static IEnumerable<object[]> RoleNames() => Each(BookRoles);

    [Fact]
    public void InstrumentQuantities_are_the_team_roles_minus_altitude_dz_dimensions() =>
        AssertTable(BookRoles.Except(NotInstrumentQuantities), CommandVocabulary.InstrumentQuantities);

    [Fact]
    public void Sd_and_units_tables_match_the_book_quantity_list()
    {
        AssertTable(BookSdLength.Concat(ExtraSdLength), CommandVocabulary.SdLengthQuantities);
        AssertTable(BookSdAngle, CommandVocabulary.SdAngleQuantities);
        AssertTable(BookSdLength.Concat(ExtraSdLength).Concat(ExtraUnitsLength), CommandVocabulary.UnitsLengthQuantities);
    }

    [Fact]
    public void Length_and_angle_classes_do_not_overlap()
    {
        Assert.Empty(CommandVocabulary.SdLengthQuantities.Intersect(CommandVocabulary.SdAngleQuantities));
        Assert.Empty(CommandVocabulary.UnitsLengthQuantities.Intersect(CommandVocabulary.SdAngleQuantities));
    }

    // ---- flags, marks, extend, export -----------------------------------------------------------
    [Fact]
    public void ShotFlags_are_surface_duplicate_splay_and_approx_in_both_spellings() =>
        AssertTable(new[] { "surface", "duplicate", "splay", "approx", "approximate" }, CommandVocabulary.ShotFlags);

    [Fact]
    public void MarkTypes_are_the_book_types_plus_natural_and_temp() =>
        // book: fixed, painted, temporary; the code also takes natural and the short temp (thdataleg.h table).
        AssertTable(new[] { "fixed", "painted", "temporary", "natural", "temp" }, CommandVocabulary.MarkTypes);

    [Fact]
    public void StationFlags_are_the_book_flags_plus_fixed() =>
        // book: entrance, continuation, air-draught, sink, spring, doline, dig, arch, overhang.
        // "fixed" is accepted for the 'not fixed' form only (comment on the table).
        AssertTable(new[]
        {
            "entrance", "continuation", "air-draught", "sink", "spring", "doline", "dig", "arch", "overhang",
            "fixed",
        }, CommandVocabulary.StationFlags);

    [Fact]
    public void ExtendSpecs_are_the_eight_book_keywords() =>
        AssertTable(new[] { "normal", "reverse", "left", "right", "vertical", "start", "ignore", "hide" },
            CommandVocabulary.ExtendSpecs);

    [Fact]
    public void ExportTypes_are_the_seven_book_types() =>
        AssertTable(new[]
        {
            "model", "map", "atlas", "cave-list", "survey-list", "continuation-list", "database",
        }, CommandVocabulary.ExportTypes);

    // Book: "File formats summary" plus the -fmt text of each export type. Extras are named.
    [Theory]
    [InlineData("model", "loch,dxf,esri,compass,survex,vrml,3dmf,kml", "3d,shapefile,shapefiles,shp")]
    [InlineData("map", "pdf,svg,xhtml,dxf,esri,survex,xvi,kml,bbox", "shapefile,shapefiles,shp,th2")]
    [InlineData("atlas", "pdf", "")]
    [InlineData("database", "sql,csv", "")]
    [InlineData("cave-list", "html,txt,kml,dbf", "text")]
    [InlineData("survey-list", "html,txt,kml,dbf", "text")]
    [InlineData("continuation-list", "html,txt,kml,dbf", "text")]
    public void Export_formats_are_the_book_list_plus_the_named_extras(string type, string book, string extra)
    {
        var expected = book.Split(',').Concat(extra.Length == 0 ? [] : extra.Split(','));
        AssertTable(expected, CommandVocabulary.ExportFormats(type));
        AssertTable(expected, CommandVocabulary.ExportFormats(type.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("models")]
    public void An_unknown_export_type_has_no_formats(string type) =>
        Assert.Empty(CommandVocabulary.ExportFormats(type));

    [Fact]
    public void Phantom_model_formats_stay_rejected()
    {
        // lox/plt/wrl are file extensions, not -fmt values (comment above ModelFormats).
        foreach (var f in new[] { "lox", "plt", "wrl" })
            Assert.False(CommandVocabulary.IsExportFormat("model", f));
    }

    [Fact]
    public void IsExportType_and_IsExportFormat_follow_the_tables()
    {
        foreach (var t in CommandVocabulary.ExportTypes)
        {
            Assert.True(CommandVocabulary.IsExportType(t));
            foreach (var f in CommandVocabulary.ExportFormats(t))
                Assert.True(CommandVocabulary.IsExportFormat(t, f), t + " " + f);
            Assert.False(CommandVocabulary.IsExportFormat(t, "wibble"), t);
        }
        Assert.False(CommandVocabulary.IsExportType("wibble"));
        // An unknown type is reported elsewhere, so its format is not flagged a second time.
        Assert.True(CommandVocabulary.IsExportFormat("wibble", "wibble"));
        Assert.False(CommandVocabulary.IsExportFormat("map", "loch"));
        Assert.True(CommandVocabulary.IsExportFormat("model", "loch"));
    }

    // ---- predicates -----------------------------------------------------------------------------
    [Theory]
    [InlineData("surface", true)]
    [InlineData("DUPLICATE", true)]
    [InlineData("splay", true)]
    [InlineData("approx", true)]
    [InlineData("approximate", true)]
    [InlineData("not", true)]
    [InlineData("NOT", true)]
    [InlineData("nots", false)]
    [InlineData("approx.", false)]
    [InlineData("", false)]
    [InlineData("entrance", false)]
    public void IsShotFlag(string t, bool expected) =>
        Assert.Equal(expected, CommandVocabulary.IsShotFlag(t));

    [Theory]
    [InlineData("fixed", true)]
    [InlineData("Painted", true)]
    [InlineData("temp", true)]
    [InlineData("temporary", true)]
    [InlineData("natural", true)]
    [InlineData("not", false)]
    [InlineData("", false)]
    [InlineData("paint", false)]
    public void IsMarkType(string t, bool expected) =>
        Assert.Equal(expected, CommandVocabulary.IsMarkType(t));

    [Theory]
    [InlineData("not", true)]
    [InlineData("NOT", true)]
    [InlineData("attr", true)]
    [InlineData("Attr", true)]
    [InlineData("explored", true)]
    [InlineData("EXPLORED", true)]
    [InlineData("entrance", true)]
    [InlineData("continuation", true)]
    [InlineData("air-draught", true)]
    [InlineData("air-draught:winter", true)]
    [InlineData("air-draught:summer", true)]
    [InlineData("AIR-DRAUGHT:Winter", true)]
    [InlineData("sink", true)]
    [InlineData("spring", true)]
    [InlineData("doline", true)]
    [InlineData("dig", true)]
    [InlineData("arch", true)]
    [InlineData("overhang", true)]
    [InlineData("fixed", true)]
    [InlineData("", false)]
    [InlineData(":winter", false)]
    [InlineData("notentrance", false)]
    [InlineData("not-entrance", false)]
    [InlineData("air draught", false)]
    [InlineData("airdraught", false)]
    [InlineData("entrances", false)]
    [InlineData("attrs", false)]
    [InlineData("explore", false)]
    [InlineData("surface", false)]
    public void IsStationFlag(string t, bool expected) =>
        Assert.Equal(expected, CommandVocabulary.IsStationFlag(t));

    [Fact]
    public void Every_station_flag_name_is_a_station_flag_and_no_shot_flag_is()
    {
        foreach (var f in CommandVocabulary.StationFlags) Assert.True(CommandVocabulary.IsStationFlag(f), f);
        foreach (var f in CommandVocabulary.ShotFlags) Assert.False(CommandVocabulary.IsStationFlag(f), f);
    }

    [Theory]
    [InlineData("normal", true)]
    [InlineData("REVERSE", true)]
    [InlineData("left", true)]
    [InlineData("right", true)]
    [InlineData("vertical", true)]
    [InlineData("start", true)]
    [InlineData("ignore", true)]
    [InlineData("hide", true)]
    [InlineData("0", true)]
    [InlineData("0.0", true)]
    [InlineData("-0", true)]
    [InlineData("1", true)]
    [InlineData("50", true)]
    [InlineData("100", true)]
    [InlineData("199.9", true)]
    [InlineData("200", true)]
    [InlineData("200.0", true)]
    [InlineData("-0.1", false)]
    [InlineData("-1", false)]
    [InlineData("200.1", false)]
    [InlineData("201", false)]
    [InlineData("1000", false)]
    [InlineData("NaN", false)]
    [InlineData("Infinity", false)]
    [InlineData("-Infinity", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("50%", false)]
    [InlineData("normals", false)]
    public void IsExtendSpec_keywords_and_the_0_to_200_range(string t, bool expected) =>
        Assert.Equal(expected, CommandVocabulary.IsExtendSpec(t));

    [Fact]
    public void IsExtendSpec_reads_numbers_with_the_invariant_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(CommandVocabulary.IsExtendSpec("150.5"));
            Assert.False(CommandVocabulary.IsExtendSpec("150,5"));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    // ---- FindIncompatibleQuantity ----------------------------------------------------------------
    [Theory]
    [InlineData("length bearing", false, "bearing")]
    [InlineData("bearing length", false, "length")]
    [InlineData("length tape count", false, null)]
    [InlineData("bearing compass clino gradient", false, null)]
    [InlineData("length foo bearing", false, "bearing")]
    [InlineData("foo length bearing", false, "bearing")]
    [InlineData("foo bearing length", false, "length")]
    [InlineData("foo bar", false, null)]
    [InlineData("length foo", false, null)]
    [InlineData("", false, null)]
    [InlineData("LENGTH Bearing", false, "Bearing")]
    [InlineData("length bearing compass", false, "bearing")]      // the first offender is reported
    [InlineData("length dz bearing", false, "bearing")]
    [InlineData("dz bearing", false, "bearing")]
    [InlineData("up bearing", false, null)]                       // up is unknown to sd
    [InlineData("up bearing", true, "bearing")]                   // ... but a length for units
    [InlineData("bearing up", true, "up")]
    [InlineData("depthchange compass", true, "compass")]
    [InlineData("dimensions clino", true, "clino")]
    [InlineData("left right up down ceiling floor dimensions depthchange length", true, null)]
    [InlineData("depthchange length", false, null)]               // depthchange unknown to sd: skipped
    [InlineData("length foo bearing", true, "bearing")]
    public void FindIncompatibleQuantity_table(string quantities, bool unitsSet, string? expected)
    {
        var list = quantities.Length == 0 ? new List<string>() : quantities.Split(' ').ToList();
        Assert.Equal(expected, CommandVocabulary.FindIncompatibleQuantity(list, unitsSet));
    }

    [Fact]
    public void FindIncompatibleQuantity_flags_every_length_angle_pair_in_either_order()
    {
        foreach (var unitsSet in new[] { false, true })
        {
            var lengths = unitsSet ? CommandVocabulary.UnitsLengthQuantities : CommandVocabulary.SdLengthQuantities;
            foreach (var l in lengths)
                foreach (var a in CommandVocabulary.SdAngleQuantities)
                {
                    Assert.Equal(a, CommandVocabulary.FindIncompatibleQuantity(new[] { l, a }, unitsSet));
                    Assert.Equal(l, CommandVocabulary.FindIncompatibleQuantity(new[] { a, l }, unitsSet));
                    // The same class on its own is fine.
                    Assert.Null(CommandVocabulary.FindIncompatibleQuantity(new[] { l, l }, unitsSet));
                    Assert.Null(CommandVocabulary.FindIncompatibleQuantity(new[] { a, a }, unitsSet));
                }
        }
    }

    // ---- MeasurementUnits ------------------------------------------------------------------------
    // Book "units" value type: meter[s], centimeter[s], inch[es], feet[s], yard[s] (also m, cm, in,
    // ft, yd); degree[s], minute[s] (also deg, min), grad[s], mil[s], percent[age].
    [Theory]
    [InlineData("meter", LengthUnit.Metre)]
    [InlineData("meters", LengthUnit.Metre)]
    [InlineData("m", LengthUnit.Metre)]
    [InlineData("centimeter", LengthUnit.Centimetre)]
    [InlineData("centimeters", LengthUnit.Centimetre)]
    [InlineData("cm", LengthUnit.Centimetre)]
    [InlineData("inch", LengthUnit.Inch)]
    [InlineData("inches", LengthUnit.Inch)]
    [InlineData("in", LengthUnit.Inch)]
    [InlineData("feet", LengthUnit.Foot)]
    [InlineData("feets", LengthUnit.Foot)]
    [InlineData("ft", LengthUnit.Foot)]
    [InlineData("yard", LengthUnit.Yard)]
    [InlineData("yards", LengthUnit.Yard)]
    [InlineData("yd", LengthUnit.Yard)]
    // spellings beyond the book that the table keeps (British spelling, the source's metric/millimetre, tolerated extras)
    [InlineData("metre", LengthUnit.Metre)]
    [InlineData("metres", LengthUnit.Metre)]
    [InlineData("metric", LengthUnit.Metre)]
    [InlineData("centimetre", LengthUnit.Centimetre)]
    [InlineData("centimetres", LengthUnit.Centimetre)]
    [InlineData("millimeter", LengthUnit.Millimetre)]
    [InlineData("millimeters", LengthUnit.Millimetre)]
    [InlineData("millimetre", LengthUnit.Millimetre)]
    [InlineData("millimetres", LengthUnit.Millimetre)]
    [InlineData("mm", LengthUnit.Millimetre)]
    [InlineData("foot", LengthUnit.Foot)]
    [InlineData("kilometer", LengthUnit.Kilometre)]
    [InlineData("kilometre", LengthUnit.Kilometre)]
    [InlineData("km", LengthUnit.Kilometre)]
    public void Length_unit_names(string name, LengthUnit expected)
    {
        Assert.Equal(expected, MeasurementUnits.TryLength(name));
        Assert.Equal(expected, MeasurementUnits.TryLength(name.ToUpperInvariant()));
        Assert.True(MeasurementUnits.IsUnit(name));
        Assert.Null(MeasurementUnits.TryAngle(name));
    }

    [Theory]
    [InlineData("degree", AngleUnit.Degree)]
    [InlineData("degrees", AngleUnit.Degree)]
    [InlineData("deg", AngleUnit.Degree)]
    [InlineData("minute", AngleUnit.Minute)]
    [InlineData("minutes", AngleUnit.Minute)]
    [InlineData("min", AngleUnit.Minute)]
    [InlineData("grad", AngleUnit.Grad)]
    [InlineData("grads", AngleUnit.Grad)]
    [InlineData("mil", AngleUnit.Mil)]
    [InlineData("mils", AngleUnit.Mil)]
    [InlineData("percent", AngleUnit.PercentSlope)]
    [InlineData("percentage", AngleUnit.PercentSlope)]
    // tolerated beyond the book
    [InlineData("grades", AngleUnit.Grad)]
    [InlineData("gon", AngleUnit.Grad)]
    public void Angle_unit_names(string name, AngleUnit expected)
    {
        Assert.Equal(expected, MeasurementUnits.TryAngle(name));
        Assert.Equal(expected, MeasurementUnits.TryAngle(name.ToUpperInvariant()));
        Assert.True(MeasurementUnits.IsUnit(name));
        Assert.Null(MeasurementUnits.TryLength(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("meterz")]
    [InlineData("deg.")]
    [InlineData("furlong")]
    [InlineData("rad")]
    public void Unknown_unit_names_resolve_to_nothing(string name)
    {
        Assert.Null(MeasurementUnits.TryLength(name));
        Assert.Null(MeasurementUnits.TryAngle(name));
        Assert.False(MeasurementUnits.IsUnit(name));
    }

    [Fact]
    public void AllUnitNames_lists_every_accepted_name_once_per_table()
    {
        var all = MeasurementUnits.AllUnitNames.ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(all, n => Assert.True(MeasurementUnits.IsUnit(n), n));
        Assert.Equal(29 + 14, all.Count);   // length names + angle names
        Assert.Contains("metric", all);
        Assert.Contains("gon", all);
    }

    [Fact]
    public void Quantities_cover_every_sd_and_units_name_and_the_back_forms()
    {
        foreach (var q in CommandVocabulary.SdLengthQuantities.Concat(CommandVocabulary.SdAngleQuantities)
                     .Concat(CommandVocabulary.UnitsLengthQuantities).Where(q => q is not ("count" or "gps" or "depthchange" or "dimensions")))
            Assert.True(MeasurementUnits.IsQuantity(q), q);
        foreach (var q in new[] { "backtape", "backlength", "backcompass", "backbearing", "backclino", "backgradient" })
            Assert.True(MeasurementUnits.IsQuantity(q), q);
        Assert.True(MeasurementUnits.IsQuantity("LENGTH"));
        Assert.False(MeasurementUnits.IsQuantity("lengths"));
        Assert.False(MeasurementUnits.IsQuantity(""));
        Assert.False(MeasurementUnits.IsQuantity("notes"));
    }
}
