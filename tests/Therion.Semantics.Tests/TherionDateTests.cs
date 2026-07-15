using System;
using Therion.Semantics;
using Xunit;

namespace Therion.Semantics.Tests;

/// <summary>Therion date parsing to [min,max] intervals, with partials as whole spans (CQ-03).</summary>
public class TherionDateTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void FullDate_IsASingleDay()
    {
        var i = TherionDate.Parse("2000.07.15");
        Assert.NotNull(i);
        Assert.Equal(D(2000, 7, 15), i!.Value.Min);
        Assert.Equal(D(2000, 7, 15), i.Value.Max);
    }

    [Fact]
    public void YearOnly_SpansTheWholeYear()
    {
        var i = TherionDate.Parse("2003")!.Value;
        Assert.Equal(D(2003, 1, 1), i.Min);
        Assert.Equal(D(2003, 12, 31), i.Max);
    }

    [Fact]
    public void YearMonth_SpansTheWholeMonth_IncludingLeapFebruary()
    {
        var feb = TherionDate.Parse("2000.02")!.Value;   // 2000 is a leap year
        Assert.Equal(D(2000, 2, 1), feb.Min);
        Assert.Equal(D(2000, 2, 29), feb.Max);
    }

    [Fact]
    public void Interval_RunsFromStartOfFirstToEndOfLast()
    {
        var i = TherionDate.Parse("2000.07.15 - 2000.07.20")!.Value;
        Assert.Equal(D(2000, 7, 15), i.Min);
        Assert.Equal(D(2000, 7, 20), i.Max);
    }

    [Fact]
    public void Interval_WithPartialEnds_ExpandsBothSides()
    {
        var i = TherionDate.Parse("2000 - 2002")!.Value;
        Assert.Equal(D(2000, 1, 1), i.Min);
        Assert.Equal(D(2002, 12, 31), i.Max);
    }

    [Fact]
    public void TokenizerArtefactSpaces_AreTolerated()
    {
        var i = TherionDate.Parse("2024.07 .01")!.Value;
        Assert.Equal(D(2024, 7, 1), i.Min);
        Assert.Equal(D(2024, 7, 1), i.Max);
    }

    [Fact]
    public void TimeOfDay_IsIgnored()
    {
        var i = TherionDate.Parse("2011.03.04@10:30:00")!.Value;
        Assert.Equal(D(2011, 3, 4), i.Min);
        Assert.Equal(D(2011, 3, 4), i.Max);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("2000.13")]     // month out of range
    [InlineData("2000.02.30")]  // day out of range
    [InlineData("2000.07.15 - nonsense")]
    public void Unparseable_IsNull(string? raw)
    {
        Assert.Null(TherionDate.Parse(raw));
    }

    [Fact]
    public void Overlaps_UsesInclusiveEnds()
    {
        var y2003 = TherionDate.Parse("2003")!.Value;              // whole of 2003
        var range = new TherionDateInterval(D(2000, 1, 1), D(2005, 12, 31));
        Assert.True(y2003.Overlaps(range));
        Assert.True(y2003.Overlaps(D(2000, 1, 1), D(2005, 12, 31)));
        // Touching at a single day still counts (inclusive).
        Assert.True(TherionDate.Parse("2005.12.31")!.Value.Overlaps(D(2005, 12, 31), D(2010, 1, 1)));
        Assert.False(TherionDate.Parse("2006")!.Value.Overlaps(D(2000, 1, 1), D(2005, 12, 31)));
    }

    [Fact]
    public void Span_UnionsEveryParseableDate()
    {
        var span = TherionDate.Span(new[] { "2001.05", "bad", "1999.12.20", "2003" })!.Value;
        Assert.Equal(D(1999, 12, 20), span.Min);
        Assert.Equal(D(2003, 12, 31), span.Max);
        Assert.Null(TherionDate.Span(new[] { "bad", "also bad" }));
    }
}
