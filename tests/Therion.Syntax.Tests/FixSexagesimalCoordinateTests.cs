using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class FixSexagesimalCoordinateTests
{
    private static StationFix ParseFix(string fixLine)
    {
        var r = new ThParser().Parse("/p/a.th",
            $"survey s\n centreline\n  cs lat-long\n  {fixLine}\n endcentreline\nendsurvey\n");
        return r.Value!.Children.OfType<SurveyCommand>().Single()
            .Children.OfType<CentrelineCommand>().Single()
            .Children.OfType<StationFix>().Single();
    }

    [Fact]
    public void Deg_min_sec_fix_coordinates_are_converted_to_decimal_degrees()
    {
        var fix = ParseFix("fix 1 45:30:00 25:13:43.7 500");
        Assert.True(System.Math.Abs(fix.X - 45.5) < 1e-5, $"X was {fix.X}, expected 45.5");
        Assert.True(System.Math.Abs(fix.Y - (25 + 13 / 60.0 + 43.7 / 3600)) < 1e-5,
            $"Y was {fix.Y}, expected 25.22881");
        Assert.Equal(500, fix.Z);
    }
}
