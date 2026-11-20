// Axial mean and Rayleigh test: opposite bearings are one axis, a mean straddling north is found,
// and the Rayleigh probability reproduces the tabulated five per cent critical value for ten
// observations (Z = 2.9 approximately; Mardia and Jupp, "Directional Statistics").

using System;
using Therion.Orientation;
using Xunit;

namespace Therion.Orientation.Tests;

public class AxialStatisticsTests
{
    [Fact]
    public void An_empty_sample_has_no_mean_and_no_test()
    {
        var m = AxialStatistics.Compute([], []);
        Assert.Null(m.MeanAxisDegrees);
        Assert.Equal(0d, m.ResultantLength);
        Assert.Null(m.RayleighZ);
        Assert.Null(m.RayleighP);
    }

    [Fact]
    public void Axes_straddling_north_average_to_north()
    {
        var m = AxialStatistics.Compute([175d, 5d], [1d, 1d]);
        Assert.Equal(Math.Cos(10d * Math.PI / 180d), m.ResultantLength, 1e-9);
        var mean = m.MeanAxisDegrees!.Value;
        Assert.True(mean < 1e-6 || mean > 180d - 1e-6, $"mean {mean}");
    }

    [Fact]
    public void Identical_axes_give_a_unit_resultant_and_the_textbook_effective_size()
    {
        var m = AxialStatistics.Compute([40d, 40d, 40d, 40d], [1d, 1d, 1d, 1d]);
        Assert.Equal(40d, m.MeanAxisDegrees!.Value, 1e-9);
        Assert.Equal(1d, m.ResultantLength, 1e-9);
        Assert.Equal(4d, m.EffectiveSampleSize, 1e-9);
        Assert.Equal(4d, m.RayleighZ!.Value, 1e-9);
    }

    [Fact]
    public void Uneven_weights_shrink_the_effective_sample_size()
    {
        var m = AxialStatistics.Compute([10d, 20d, 30d], [100d, 1d, 1d]);
        Assert.True(m.EffectiveSampleSize < 1.1d);
        Assert.Null(m.RayleighZ);
    }

    [Fact]
    public void The_probability_at_the_tabulated_critical_value_for_ten_observations_is_five_per_cent()
    {
        Assert.Equal(0.05, AxialStatistics.RayleighProbability(2.9, 10), 0.003);
    }

    [Fact]
    public void The_probability_is_clamped_to_the_unit_interval()
    {
        var p = AxialStatistics.RayleighProbability(0d, 2d);
        Assert.InRange(p, 0d, 1d);
    }

    [Fact]
    public void Mismatched_lengths_are_refused()
    {
        Assert.Throws<ArgumentException>(() => AxialStatistics.Compute([1d], [1d, 2d]));
    }
}
