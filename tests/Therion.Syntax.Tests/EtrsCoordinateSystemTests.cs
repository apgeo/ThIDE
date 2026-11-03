using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class EtrsCoordinateSystemTests
{
    [Theory]
    [InlineData("ETRS33")]
    [InlineData("etrs28")]
    [InlineData("etrs37")]
    [InlineData("eur79z30")]
    public void Therion_output_systems_are_known(string cs) =>
        Assert.True(CoordinateSystems.IsKnown(cs), $"'{cs}' reported as unknown");

    [Fact]
    public void Cs_etrs_zone_in_centreline_has_no_unknown_system_diagnostic()
    {
        var r = new ThParser().Parse("/p/a.th", "survey s\n centreline\n  cs ETRS33\n endcentreline\nendsurvey\n");
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownCoordinateSystem);
    }
}
