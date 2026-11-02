using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented data. Survex settings (*units, *data, *calibrate, …) stay in force inside a nested *begin
// and after a child *end. Every Therion centreline starts from defaults, so each centreline the
// importer opens must restate the settings that apply to its rows.
public class SurvexSettingsScopeTests
{
    [Fact]
    public void Units_set_in_the_parent_reach_the_rows_of_a_nested_begin()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *units tape feet
            *data normal from to tape compass clino
            *begin a
            1 2 30 100 -5
            *end a
            *end cave
            """);

        int inner = th.IndexOf("survey a", StringComparison.Ordinal);
        int row = th.IndexOf("1 2 30", inner, StringComparison.Ordinal);
        int units = th.IndexOf("units tape feet", inner, StringComparison.Ordinal);

        Assert.True(inner >= 0 && row > inner, "expected `survey a` holding the row 1 2 30:\n" + th);
        Assert.True(units >= 0 && units < row, "the centreline of `survey a` has no `units tape feet` before its row, so Therion reads 30 as metres:\n" + th);
    }

    [Fact]
    public void Data_order_set_before_a_child_block_reaches_the_rows_after_it()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *data normal from to compass clino tape
            *begin a
            1 2 100 -5 30
            *end a
            3 4 100 -5 30
            *end cave
            """);

        int afterChild = th.IndexOf("endsurvey", StringComparison.Ordinal);
        int row = th.IndexOf("3 4 100 -5 30", afterChild, StringComparison.Ordinal);
        int data = th.IndexOf("data normal from to compass clino tape", afterChild, StringComparison.Ordinal);

        Assert.True(afterChild >= 0 && row > afterChild, "expected the row 3 4 100 -5 30 after `endsurvey` of a:\n" + th);
        Assert.True(data >= 0 && data < row, "the centreline reopened after `survey a` has no `data` line before its row, so Therion reads 100 as the length:\n" + th);
    }

    [Fact]
    public void A_setting_changed_in_a_child_does_not_leak_back_to_the_parent()
    {
        var th = SurvexImporter.Import("""
            *begin cave
            *units tape feet
            *begin a
            *units tape metres
            1 2 30 100 -5
            *end a
            3 4 30 100 -5
            *end cave
            """);

        int afterChild = th.IndexOf("endsurvey", StringComparison.Ordinal);
        int row = th.IndexOf("3 4 30", afterChild, StringComparison.Ordinal);
        int units = th.IndexOf("units tape feet", afterChild, StringComparison.Ordinal);

        Assert.True(units >= 0 && units < row, th);
        Assert.DoesNotContain("units tape metres", th[afterChild..]);
    }
}
