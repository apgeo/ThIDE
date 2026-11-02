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

    [Fact]
    public void A_dotted_fix_and_a_nested_path_follow_the_survey_nesting()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *fix upper.1 0 0 0
            *equate 1 upper.deep.7
            *begin upper
            *begin deep
            7 8 2 0 0
            *end deep
            *end upper
            *end cave
            """);

        Assert.Contains("fix 1@upper 0 0 0", th);
        Assert.Contains("equate 1 7@deep.upper", th);
    }

    [Fact]
    public void Station_columns_follow_the_data_order_and_measurements_are_left_alone()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *data normal tape compass clino from to
            10.5 100 -5 upper.1 2
            *begin upper
            1 2 5 0 0
            *end upper
            *end cave
            """);

        Assert.Contains("10.5 100 -5 1@upper 2", th);
    }

    [Fact]
    public void A_separator_set_in_the_file_is_honoured()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *set separator ":"
            *data normal from to tape compass clino
            2 upper:1 4.0 90 0
            *begin upper
            1 2 5 0 0
            *end upper
            *end cave
            """);

        Assert.Contains("2 1@upper 4.0 90 0", th);
    }
}
