using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented data. Survex names a station in a sub-survey as `sub.station`; Therion writes it as
// `station@sub`, and a bare `sub.station` is just a new station whose name holds a dot.
public class SurvexStationReferenceTests
{
    [Fact]
    public void A_dotted_equate_names_the_station_in_the_sub_survey()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *data normal from to tape compass clino
            1 2 10.0 100 -5
            2 3 8.5 110 0
            *equate 3 upper.1
            *begin upper
            1 2 5 0 0
            *end upper
            *end cave
            """);

        Assert.DoesNotContain("upper.1", th);
        Assert.Contains("1@upper", th);
    }

    [Fact]
    public void A_dotted_station_in_a_data_row_names_the_station_in_the_sub_survey()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *data normal from to tape compass clino
            1 2 10.0 100 -5
            2 upper.1 4.0 90 0
            *begin upper
            1 2 5 0 0
            *end upper
            *end cave
            """);

        Assert.DoesNotContain("upper.1", th);
        Assert.Contains("2 1@upper 4.0 90 0", th);
    }
}
