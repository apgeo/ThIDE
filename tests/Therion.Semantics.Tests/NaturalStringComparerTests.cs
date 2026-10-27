using Therion.Core;

namespace Therion.Semantics.Tests;

public class NaturalStringComparerTests
{
    private static List<string> Sorted(params string[] items) => items.Order(NaturalStringComparer.Instance).ToList();

    [Fact]
    public void Numbers_sort_by_value()
    {
        Assert.Equal(["1", "2", "10", "100"], Sorted("10", "2", "100", "1"));
    }

    [Fact]
    public void Mixed_letters_and_digits_sort_per_run()
    {
        Assert.Equal(["a1b2", "a1b10", "a2b1", "a10b1"], Sorted("a10b1", "a1b10", "a2b1", "a1b2"));
    }

    [Fact]
    public void Dotted_station_names_sort_field_by_field()
    {
        Assert.Equal(["1.2", "1.12", "2.1", "10.0"], Sorted("1.12", "10.0", "2.1", "1.2"));
    }

    [Fact]
    public void Letters_compare_ignoring_case()
    {
        Assert.Equal(["alpha", "Beta", "gamma"], Sorted("gamma", "Beta", "alpha"));
    }

    [Fact]
    public void Zero_padding_and_case_variants_stay_distinct_and_ordered()
    {
        var c = NaturalStringComparer.Instance;

        Assert.True(c.Compare("7", "07") != 0);
        Assert.Equal(-c.Compare("7", "07"), c.Compare("07", "7"));
        Assert.True(c.Compare("A", "a") != 0);
        Assert.True(c.Compare("6", "07") < 0);
    }

    [Fact]
    public void Prefix_sorts_first_and_nulls_lead()
    {
        Assert.Equal([null, "st", "st1"], new string?[] { "st1", null, "st" }.Order(NaturalStringComparer.Instance).ToList());
    }

    [Fact]
    public void Very_long_digit_runs_do_not_overflow()
    {
        Assert.True(NaturalStringComparer.Instance.Compare("99999999999999999999999", "100000000000000000000000") < 0);
    }
}
