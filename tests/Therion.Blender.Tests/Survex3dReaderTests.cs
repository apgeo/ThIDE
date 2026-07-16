// .3d parser tests on spec-exact synthetic streams (built by Survex3dFixtureBuilder)
// covering the v8 and v3-v7 wire formats: header metadata, the stateful label buffer
// (nibble + escaped patches, append/trim/clear), stations/legs with flags, styles,
// all date encodings, XSECT passages and traverse error info — plus version gating.
// Real-file and cross-format tests live in Survex3dRealFileTests / batch-4 suites.

using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class Survex3dReaderTests
{
    private static readonly DateOnly Day1900 = new(1900, 1, 1);

    // ----- header -----

    [Fact]
    public void V8Header_ExtractsTitleCoordinateSystemSeparatorAndTimestamp()
    {
        var bytes = Survex3dFixtureBuilder
            .V8("Peștera Cerbul", coordinateSystem: "EPSG:31700", separator: '.', datestamp: "@1371300355")
            .V8Stop(styleAlreadyNormal: false)
            .Build();

        var model = Survex3dReader.Read(bytes, "cave.3d");

        Assert.Equal(CaveSourceFormat.Survex3d, model.SourceFormat);
        Assert.Equal(8, model.FormatVersion);
        Assert.Equal("Peștera Cerbul", model.Title);
        Assert.Equal("EPSG:31700", model.CoordinateSystem);
        Assert.Equal('.', model.SeparatorChar);
        Assert.Equal("@1371300355", model.Datestamp);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1371300355), model.Timestamp);
        Assert.False(model.IsExtendedElevation);
        Assert.Equal("cave.3d", model.SourcePath);
    }

    [Fact]
    public void V8Header_ExtendedElevationFlagByte()
    {
        var bytes = Survex3dFixtureBuilder.V8("t", fileFlags: 0x80).V8Stop(styleAlreadyNormal: false).Build();
        Assert.True(Survex3dReader.Read(bytes).IsExtendedElevation);
    }

    [Fact]
    public void OldHeader_ExtendedElevationTitleSuffix()
    {
        var bytes = Survex3dFixtureBuilder.Old(7, "Big Cave (extended)").OldStop(labelEmpty: true).Build();

        var model = Survex3dReader.Read(bytes);

        Assert.True(model.IsExtendedElevation);
        Assert.Equal("Big Cave", model.Title);
        Assert.Null(model.Timestamp); // human-readable datestamp: kept raw, not parsed
        Assert.Equal("Sun,2002.03.17 14:01:07 GMT", model.Datestamp);
    }

    [Theory]
    [InlineData("v0.01")]
    [InlineData("Bv0.01")]
    [InlineData("v2")]
    public void PreV3Versions_AreRejectedWithClearMessage(string version)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes($"Survex 3D Image File\n{version}\nt\n@0\n");
        var ex = Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(bytes));
        Assert.Contains("pre-1997", ex.Message);
    }

    [Fact]
    public void NonSurvexFile_IsRejected()
    {
        var ex = Assert.Throws<CaveFileFormatException>(
            () => Survex3dReader.Read(System.Text.Encoding.ASCII.GetBytes("PK\x03\x04 not a 3d file\nx\n")));
        Assert.Contains("Survex 3D Image File", ex.Message);
    }

    // ----- v8 items -----

    private static Survex3dFixtureBuilder V8Cave() => Survex3dFixtureBuilder.V8("cave", "EPSG:31700", '.');

    [Fact]
    public void V8_StationsWithFlagsAndPatchedLabels()
    {
        var bytes = V8Cave()
            .V8Label(0x04 | 0x10, 0, "cave.ent", 100.25, 200.5, 300.0)   // entrance+fixed
            .V8Label(0x02, 3, "1", 110.0, 210.0, 295.5)                  // "cave.ent" -3 +"1" = "cave.1"
            .V8Label(0x40 | 0x02, 1, "2b", 111.0, 211.0, 295.0)          // wall+underground → "cave.2b"
            .V8Stop(styleAlreadyNormal: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(3, model.Stations.Count);

        var entrance = model.Stations[0];
        Assert.Equal("cave.ent", entrance.Name);
        Assert.Equal(0u, entrance.Id);
        Assert.True(entrance.IsEntrance);
        Assert.True(entrance.IsFixed);
        Assert.Equal(CaveStationFlags.Entrance | CaveStationFlags.Fixed, entrance.Flags);
        Assert.Equal(0x14u, entrance.RawFlags);
        Assert.Equal(new CaveVector3(100.25, 200.5, 300.0), entrance.Position);

        Assert.Equal("cave.1", model.Stations[1].Name);
        Assert.Equal(CaveStationFlags.Underground, model.Stations[1].Flags);

        Assert.Equal("cave.2b", model.Stations[2].Name);
        Assert.Equal(CaveStationFlags.Wall | CaveStationFlags.Underground, model.Stations[2].Flags);
    }

    [Fact]
    public void V8_EscapedLabelPatchCounts()
    {
        var longName = "cave." + new string('x', 400); // add > 15 forces the escaped form
        var bytes = V8Cave()
            .V8Label(0, 0, longName, 1, 2, 3)
            .V8Label(0, 400 + 5, "cave.s", 4, 5, 6) // delete 405 ≥ 0xff exercises the u32-escaped count
            .V8Stop(styleAlreadyNormal: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(longName, model.Stations[0].Name);
        Assert.Equal("cave.s", model.Stations[1].Name);
    }

    [Fact]
    public void V8_LegsCarrySurveyLabelFlagsStyleAndDates()
    {
        var bytes = V8Cave()
            .V8Style(0x00)                          // NORMAL
            .V8SingleDate(45_000)
            .V8Move(0, 0, 0)
            .V8Line(0x00, (0, "cave"), 10, 0, 0)
            .V8Line(0x04, null, 20, 5, -2.5)        // splay, label unchanged
            .V8Style(0x01)                          // DIVING
            .Op(0x13).U16(45_100).U16(45_200)       // long date range
            .V8Line(0x01 | 0x02, null, 30, 5, -2.5) // surface+duplicate
            .V8Stop(styleAlreadyNormal: false)      // style is Diving here — needs the extra 0x00
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(3, model.Shots.Count);

        var first = model.Shots[0];
        Assert.Equal("cave", first.SurveyName);
        Assert.Equal(new CaveVector3(0, 0, 0), first.FromPosition);
        Assert.Equal(new CaveVector3(10, 0, 0), first.ToPosition);
        Assert.Equal(SurveyStyle.Normal, first.Style);
        Assert.Equal(Day1900.AddDays(45_000), first.DateFrom);
        Assert.Equal(first.DateFrom, first.DateTo);
        Assert.Equal(CaveShotFlags.None, first.Flags);

        var splay = model.Shots[1];
        Assert.Equal(CaveShotFlags.Splay, splay.Flags);
        Assert.Equal(new CaveVector3(10, 0, 0), splay.FromPosition); // chained from previous leg
        Assert.Equal(new CaveVector3(20, 5, -2.5), splay.ToPosition);

        var dived = model.Shots[2];
        Assert.Equal(SurveyStyle.Diving, dived.Style);
        Assert.Equal(CaveShotFlags.Surface | CaveShotFlags.Duplicate, dived.Flags);
        Assert.Equal(Day1900.AddDays(45_100), dived.DateFrom);
        Assert.Equal(Day1900.AddDays(45_200), dived.DateTo);
    }

    [Fact]
    public void V8_ShortDateRange_SpansFromByte()
    {
        var bytes = V8Cave()
            .V8Style(0x00)
            .Op(0x12).U16(50_000).Op(9) // date range: day 50000 .. 50000+9+1
            .V8Move(0, 0, 0)
            .V8Line(0, (0, "c"), 1, 1, 1)
            .V8Stop()
            .Build();

        var shot = Assert.Single(Survex3dReader.Read(bytes).Shots);
        Assert.Equal(Day1900.AddDays(50_000), shot.DateFrom);
        Assert.Equal(Day1900.AddDays(50_010), shot.DateTo);
    }

    [Fact]
    public void V8_XSectsGroupIntoPassages_AndKeepOmittedDimensionsNegative()
    {
        var bytes = V8Cave()
            .V8XSect(0, "cave.1", 1.5, 2.0, 0.5, 3.0)
            .V8XSect(1, "2", 1.0, 1.0, 1.0, 1.0, last: true)                // "cave.1" -1 +"2" → "cave.2"
            .V8XSect(1, "3", -0.01, 2.5, 0.25, 0.75, wide: true)            // → "cave.3"; L omitted (0xffffffff)
            .V8XSect(0, "", 9.99, 9.99, 9.99, 9.99, last: true)             // label unchanged
            .V8Stop(styleAlreadyNormal: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(2, model.Passages.Count);
        Assert.Equal(2, model.Passages[0].Stations.Count);
        Assert.Equal("cave.1", model.Passages[0].Stations[0].StationName);
        Assert.Equal(1.5, model.Passages[0].Stations[0].Left);
        Assert.Equal("cave.2", model.Passages[0].Stations[1].StationName);

        Assert.Equal(2, model.Passages[1].Stations.Count);
        Assert.Equal("cave.3", model.Passages[1].Stations[0].StationName);
        Assert.Equal(-0.01, model.Passages[1].Stations[0].Left); // omitted dimension, kept as stored
        Assert.Equal(0.25, model.Passages[1].Stations[0].Up);
        Assert.Equal("cave.3", model.Passages[1].Stations[1].StationName);
    }

    [Fact]
    public void V8_ErrorInfo_AttachesToPrecedingTraverse()
    {
        var bytes = V8Cave()
            .V8Style(0x00)
            .V8Move(0, 0, 0)
            .V8Line(0, (0, "cave"), 10, 0, 0)
            .V8Line(0, null, 20, 0, 0)
            .V8ErrorInfo(legs: 2, lengthMetres: 21.5, e: 0.42, h: 0.30, v: 0.12)
            .V8Move(100, 100, 0)
            .V8Line(0, null, 110, 100, 0)
            .V8ErrorInfo(legs: 1, lengthMetres: 10.0, e: 0.05, h: 0.05, v: 0.01)
            .V8Stop()
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(2, model.TraverseErrors.Count);
        var first = model.TraverseErrors[0];
        Assert.Equal(0, first.ShotStartIndex);
        Assert.Equal(2, first.ShotCount);
        Assert.Equal(2, first.LegCount);
        Assert.Equal(21.5, first.Length);
        Assert.Equal(0.42, first.Error);

        var second = model.TraverseErrors[1];
        Assert.Equal(2, second.ShotStartIndex);
        Assert.Equal(1, second.ShotCount);
    }

    [Fact]
    public void V8_TruncatedStream_Throws()
    {
        var complete = V8Cave().V8Style(0x00).V8Move(0, 0, 0).V8Line(0, (0, "cave"), 10, 0, 0).V8Stop().Build();
        // Cut inside the final LINE coordinates (before the STOP bytes).
        var truncated = complete[..^8];
        Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(truncated));
    }

    [Fact]
    public void V8_LabelPatchDeletingMoreThanBuffered_Throws()
    {
        var bytes = V8Cave().V8Label(0, 10, "x", 0, 0, 0).V8Stop(styleAlreadyNormal: false).Build();
        var ex = Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(bytes));
        Assert.Contains("deletes", ex.Message);
    }

    // ----- v3-v7 items -----

    [Fact]
    public void V7_LabelAppendTrimAndClear()
    {
        var bytes = Survex3dFixtureBuilder.Old(7, "old cave")
            .OldLabel(0x04, "cave.north.0123456789abcdef", 1, 2, 3) // entrance (old bit 0x04)
            .OldTrimLevels(1)                                       // → "cave."
            .OldLabel(0x10, "far", 4, 5, 6)                         // fixed → "cave.far"
            .OldTrimChars(3)                                        // → "cave."
            .OldLabel(0x02, "x", 7, 8, 9)                           // underground → "cave.x"
            .OldClearLabel()
            .OldLabel(0x00, "other", 10, 11, 12)
            .OldStop(labelEmpty: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(7, model.FormatVersion);
        Assert.Equal(
            new[] { "cave.north.0123456789abcdef", "cave.far", "cave.x", "other" },
            model.Stations.Select(s => s.Name).ToArray());
        Assert.True(model.Stations[0].IsEntrance);
        Assert.True(model.Stations[1].IsFixed);
        Assert.Equal(CaveStationFlags.Underground, model.Stations[2].Flags);
    }

    [Fact]
    public void V7_LegsAndDates()
    {
        var bytes = Survex3dFixtureBuilder.Old(7, "t")
            .Op(0x20).U16(44_000)                       // v7 single date: days since 1900
            .OldMove(0, 0, 0)
            .OldLine(0x02, "svy", 5, 0, 0)              // duplicate
            .Op(0x23).U16(44_100).U16(44_150)           // v7 long date range
            .OldLine(0x04, "", 10, 0, 0)                // splay, no label growth
            .Op(0x24)                                   // v7 no-date
            .OldLine(0x00, "", 15, 0, 0)
            .OldStop(labelEmpty: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(3, model.Shots.Count);
        Assert.Equal("svy", model.Shots[0].SurveyName);
        Assert.Equal(CaveShotFlags.Duplicate, model.Shots[0].Flags);
        Assert.Equal(Day1900.AddDays(44_000), model.Shots[0].DateFrom);
        Assert.Equal(Day1900.AddDays(44_100), model.Shots[1].DateFrom);
        Assert.Equal(Day1900.AddDays(44_150), model.Shots[1].DateTo);
        Assert.Null(model.Shots[2].DateFrom);
        Assert.Null(model.Shots[0].Style); // pre-v8 has no style information
    }

    [Fact]
    public void V4_TimeTDates()
    {
        var bytes = Survex3dFixtureBuilder.Old(4, "t")
            .OldTimeTDate(1_000_000_000)                // 2001-09-09 UTC
            .OldMove(0, 0, 0)
            .OldLine(0, "s", 1, 0, 0)
            .OldTimeTDate(0)                            // 0 = unknown
            .OldLine(0, "", 2, 0, 0)
            .OldStop(labelEmpty: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        Assert.Equal(new DateOnly(2001, 9, 9), model.Shots[0].DateFrom);
        Assert.Null(model.Shots[1].DateFrom);
    }

    [Fact]
    public void V7_XSectsAppendToSharedLabelBuffer()
    {
        var bytes = Survex3dFixtureBuilder.Old(7, "t")
            .OldXSect("cave.1", 1.0, 2.0, 3.0, 4.0)
            .OldTrimChars(1)
            .OldXSect("2", 1.1, 2.1, 3.1, 4.1, last: true)
            .OldStop(labelEmpty: false)
            .Build();

        var model = Survex3dReader.Read(bytes);

        var passage = Assert.Single(model.Passages);
        Assert.Equal("cave.1", passage.Stations[0].StationName);
        Assert.Equal("cave.2", passage.Stations[1].StationName);
        Assert.Equal(4.1, passage.Stations[1].Down);
    }

    [Fact]
    public void V3_XSectOpcode_IsReservedAndRejected()
    {
        var bytes = Survex3dFixtureBuilder.Old(3, "t")
            .OldXSect("cave.1", 1.0, 2.0, 3.0, 4.0)
            .OldStop(labelEmpty: false)
            .Build();

        Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(bytes));
    }

    [Fact]
    public void Old_TrimmingWholeLabel_Throws()
    {
        var bytes = Survex3dFixtureBuilder.Old(7, "t")
            .OldLabel(0, "abc", 0, 0, 0)
            .OldTrimChars(3) // would empty the label — the spec calls this incorrect
            .OldStop(labelEmpty: false)
            .Build();

        Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(bytes));
    }

    [Fact]
    public void UnexpectedEndBeforeStop_Throws()
    {
        var bytes = Survex3dFixtureBuilder.Old(7, "t").OldLabel(0, "abc", 0, 0, 0).Build();
        Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(bytes));
    }
}
