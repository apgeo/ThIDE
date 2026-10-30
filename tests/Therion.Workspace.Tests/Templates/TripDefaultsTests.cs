using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class TripDefaultsTests
{
    private static string Trip(string name, string date, string units, string team) =>
        $"survey {name}\n  centreline\n    date {date}\n    team \"{team}\"\n    units {units}\n    data normal from to length compass clino\n    1 2 5 0 0\n  endcentreline\nendsurvey\n";

    [Fact]
    public void Takes_the_readings_of_the_most_recently_dated_trip()
    {
        var files = new Dictionary<string, string>
        {
            ["/c/a.th"] = Trip("a", "2025.08.01", "length feet", "Ana"),
            ["/c/b.th"] = Trip("b", "2026.9.3", "length meters", "Bo") + "survey c\n  centreline\n    date 2024.01.01\n    units length yards\n  endcentreline\nendsurvey\n",
        };
        var parsed = files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value));
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());

        var d = TripDefaults.From(ws, p => files.GetValueOrDefault(p));

        Assert.Equal("/c/b.th", d.SourceFile);
        Assert.Equal("2026.09.03", d.LatestDate);
        Assert.Equal("units length meters", d.Units);
        Assert.Equal("data normal from to length compass clino", d.Data);
        Assert.Equal("team \"Bo\"", d.Team);
        Assert.Contains("Ana", d.TeamMembers);
    }

    [Theory]
    [InlineData("2026.10.21", "2026.10.21")]
    [InlineData("2026.1.2@10:00", "2026.01.02")]
    [InlineData("2026.10.21 - 2026.10.22", "2026.10.21")]
    [InlineData("2026", "2026.00.00")]
    [InlineData("-", null)]
    public void Sortable_dates(string value, string? expected) => Assert.Equal(expected, TripDefaults.SortableDate(value));

    [Fact]
    public void Every_calibration_and_instrument_line_of_the_latest_trip_is_kept()
    {
        var text =
            "survey a\n  centreline\n    date 2026.10.21\n    calibrate compass 1.5\n    calibrate clino -0.5\n" +
            "    instrument compass \"X\"\n    instrument clino \"Y\"\n    data normal from to length compass clino\n  endcenterline\n" +
            "  centreline\n    date 2020.01.01\n    units length feet\n  endcentreline\nendsurvey\n";
        var files = new Dictionary<string, string> { ["/c/a.th"] = text };
        var parsed = files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value));

        var d = TripDefaults.From(WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>()), p => files.GetValueOrDefault(p));

        Assert.Equal("calibrate compass 1.5\ncalibrate clino -0.5", d.Calibrate);
        Assert.Equal("instrument compass \"X\"\ninstrument clino \"Y\"", d.Instrument);
        Assert.Null(d.Units); // not the older block's
    }

    [Theory]
    [InlineData("2026", true)]
    [InlineData("2026.10.27", true)]
    [InlineData("2026.10.27@14:30", true)]
    [InlineData("2026.10.27@14:30:05.5", true)]
    [InlineData("2026.10.27 - 2026.10.28", true)]
    [InlineData("2026.13.45", false)]
    [InlineData("2026.10.27@25:00", false)]
    [InlineData("27.10.2026", false)]
    [InlineData("2026.10.27 - ", false)]
    public void IsDate_accepts_what_Therion_accepts(string value, bool ok) => Assert.Equal(ok, TripDefaults.IsDate(value));
}
